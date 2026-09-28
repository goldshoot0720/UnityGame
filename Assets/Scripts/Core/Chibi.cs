// Spawns one of the eight FBX characters (normalised to a given height, standing on its
// origin, facing +Z) and animates it. Generated animation clips from the registry are used
// when present ("<id>_<action>" or "any_<action>", e.g. whale_run / any_attack); otherwise a
// procedural fallback (bob, lean, hop, lunge, shake, spin) keeps everything readable.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace MoeGames
{
    public class Chibi : MonoBehaviour
    {
        public string Id { get; private set; }
        public float Height { get; private set; }
        public Transform Body { get; private set; }
        public Transform Model { get; private set; }
        /// <summary>Registry scope ("Game1".."Game12") to prefer a game's own clips.</summary>
        public string Scope;

        public enum Loop { Idle, Run, None }
        Loop loop = Loop.Idle;
        string oneShot;
        float oneT, oneDur;
        float t;
        float runSpeed = 1f;
        Animator animator;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable current, previous;
        float fade = 1f;
        const float FadeTime = 0.2f;
        string currentClipKey;
        readonly List<Renderer> renderers = new List<Renderer>();
        public bool UsingModel => Model != null && Model.name != "Fallback";

        /// <summary>Create a character under <paramref name="parent"/>.</summary>
        public static Chibi Spawn(string id, Transform parent, Vector3 localPos, float height = 1.6f, string scope = null)
        {
            id = Cast.Canonical(id);
            var root = new GameObject("chr_" + id);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            var c = root.AddComponent<Chibi>();
            c.Id = id;
            c.Height = height;
            c.Scope = scope;
            c.Body = new GameObject("Body").transform;
            c.Body.SetParent(root.transform, false);
            c.Build();
            return c;
        }

        void Build()
        {
            GameObject prefab = null;
            AnimationClip pose = null;
            var reg = MoeRegistry.I;
            var e = reg ? reg.Character(Id) : null;
            if (e != null) { prefab = e.model; pose = e.pose; }
#if UNITY_EDITOR
            if (!prefab)
            {
                int i = Cast.IndexOf(Id);
                if (i >= 0) prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/" + Cast.Fbx[i] + ".fbx");
                if (prefab && !pose)
                    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/Characters/" + Cast.Fbx[i] + ".fbx"))
                        if (o is AnimationClip ac && !ac.name.StartsWith("__preview")) { pose = ac; break; }
            }
#endif
            GameObject inst;
            if (prefab)
            {
                inst = Instantiate(prefab);
                inst.name = "Model";
                foreach (var col in inst.GetComponentsInChildren<Collider>()) Destroy(col);
            }
            else
            {
                inst = BuildFallback();
            }
            Model = inst.transform;
            Model.SetParent(Body, false);
            Model.localPosition = Vector3.zero;
            Model.localRotation = Quaternion.identity;
            Model.localScale = Vector3.one;
            renderers.AddRange(inst.GetComponentsInChildren<Renderer>());
            foreach (var r in renderers)
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;

            animator = inst.GetComponentInChildren<Animator>();
            if (prefab && !animator) animator = inst.AddComponent<Animator>();
            if (animator)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("chibi_" + Id);
                graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                PlayLoopClip();
                if (!current.IsValid() && pose) PlayClipInternal(pose, "pose", true);
                fade = 1f;
                ApplyWeights();
                graph.Evaluate(0);
            }
            Normalise();
        }

        /// <summary>Scale the model so it is <see cref="Height"/> tall, feet at y=0, centred.</summary>
        public void Normalise()
        {
            if (renderers.Count == 0) return;
            Model.localScale = Vector3.one;
            Model.localPosition = Vector3.zero;
            var b = WorldBounds();
            float h = Mathf.Max(0.001f, b.size.y);
            // Height is in the parent's local units; bounds are in world units.
            Model.localScale = Vector3.one * (Height * Mathf.Max(0.0001f, Body.lossyScale.y) / h);
            b = WorldBounds();
            var offWorld = new Vector3(b.center.x, b.min.y, b.center.z) - Body.position;
            Model.position -= offWorld;
        }

        Bounds WorldBounds()
        {
            bool any = false;
            var b = new Bounds(transform.position, Vector3.zero);
            foreach (var r in renderers)
            {
                if (!r || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        GameObject BuildFallback()
        {
            var go = new GameObject("Fallback");
            var col = Cast.Color(Id);
            var skin = Js.Hex("#ffe0c8");
            float H = 1.6f;
            Prim.Capsule(go.transform, new Vector3(0, H * 0.28f, 0), H * 0.34f, H * 0.56f, col, "Torso");
            Prim.Sphere(go.transform, new Vector3(0, H * 0.72f, 0), H * 0.46f, skin, "Head");
            Prim.Sphere(go.transform, new Vector3(0, H * 0.8f, -0.03f), H * 0.5f, Color.Lerp(col, Color.black, 0.35f), "Hair").transform.localScale = new Vector3(H * 0.5f, H * 0.36f, H * 0.46f);
            Prim.Sphere(go.transform, new Vector3(-H * 0.08f, H * 0.72f, H * 0.21f), H * 0.07f, Color.black, "EyeL");
            Prim.Sphere(go.transform, new Vector3(H * 0.08f, H * 0.72f, H * 0.21f), H * 0.07f, Color.black, "EyeR");
            return go;
        }

        // ── animation API ──

        public void SetLoop(Loop l, float speed = 1f)
        {
            runSpeed = speed;
            if (loop == l) return;
            loop = l;
            if (oneShot == null) PlayLoopClip();
        }

        /// <summary>One-shot action: "attack", "hit", "hop", "win", "lose", "throw", "swing", "cast", "jump", "spin".</summary>
        public void Act(string action, float duration = 0.45f)
        {
            oneShot = action;
            oneT = 0;
            oneDur = Mathf.Max(0.05f, duration);
            var clip = FindClip(action);
            // Generated one-shot clips are long (≈4 s) with the action near the start: play the
            // first part and cross-fade back to the loop.
            if (clip) { PlayClipInternal(clip, action, false); oneDur = Mathf.Min(clip.length, Mathf.Max(oneDur, 1.1f)); }
        }

        public bool Busy => oneShot != null;

        public void Face(Vector3 dir)
        {
            dir.y = 0;
            if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        public void FaceSmooth(Vector3 dir, float sharp, float dt)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir, Vector3.up), 1f - Mathf.Exp(-sharp * dt));
        }

        public void SetVisible(bool on)
        {
            foreach (var r in renderers) if (r) r.enabled = on;
        }

        AnimationClip FindClip(string action)
        {
            var reg = MoeRegistry.I;
            if (!reg) return null;
            return reg.Anim(Id + "_" + action, Scope) ?? reg.Anim("any_" + action, Scope);
        }

        void PlayLoopClip()
        {
            if (!graph.IsValid()) return;
            string key = loop == Loop.Run ? "run" : loop == Loop.Idle ? "idle" : null;
            var clip = key != null ? FindClip(key) : null;
            if (!clip)
            {
                var e = MoeRegistry.I ? MoeRegistry.I.Character(Id) : null;
                if (e != null && e.pose) PlayClipInternal(e.pose, "pose", true);
                return;
            }
            PlayClipInternal(clip, key, true);
        }

        void PlayClipInternal(AnimationClip clip, string key, bool looping)
        {
            if (!graph.IsValid() || !clip || !animator) return;
            if (currentClipKey == key && current.IsValid() && looping) return;
            if (!mixer.IsValid())
            {
                mixer = AnimationMixerPlayable.Create(graph, 2);
                var output = AnimationPlayableOutput.Create(graph, "anim", animator);
                output.SetSourcePlayable(mixer);
            }
            // Slot 0 = previous (fading out), slot 1 = current (fading in).
            if (previous.IsValid()) { graph.Disconnect(mixer, 0); previous.Destroy(); }
            if (current.IsValid())
            {
                graph.Disconnect(mixer, 1);
                previous = current;
                graph.Connect(previous, 0, mixer, 0);
            }
            current = AnimationClipPlayable.Create(graph, clip);
            current.SetApplyFootIK(false);
            current.SetTime(0);
            graph.Connect(current, 0, mixer, 1);
            fade = previous.IsValid() ? 0f : 1f;
            ApplyWeights();
            currentClipKey = key;
            if (!graph.IsPlaying()) graph.Play();
        }

        void ApplyWeights()
        {
            if (!mixer.IsValid()) return;
            mixer.SetInputWeight(1, fade);
            mixer.SetInputWeight(0, previous.IsValid() ? 1f - fade : 0f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            if (fade < 1f)
            {
                fade = Mathf.Min(1f, fade + dt / FadeTime);
                ApplyWeights();
                if (fade >= 1f && previous.IsValid()) { graph.Disconnect(mixer, 0); previous.Destroy(); }
            }
            // Procedural layer on the Body transform (applies on top of any clip).
            Vector3 pos = Vector3.zero;
            Vector3 euler = Vector3.zero;
            Vector3 scale = Vector3.one;
            bool hasRunClip = currentClipKey == "run";
            switch (loop)
            {
                case Loop.Idle:
                    scale.y = 1f + Mathf.Sin(t * 3.2f) * 0.018f;
                    scale.x = scale.z = 1f - Mathf.Sin(t * 3.2f) * 0.01f;
                    break;
                case Loop.Run:
                    if (!hasRunClip)
                    {
                        pos.y = Mathf.Abs(Mathf.Sin(t * 12f * runSpeed)) * Height * 0.06f;
                        euler.x = 10f;
                        euler.z = Mathf.Sin(t * 12f * runSpeed) * 5f;
                    }
                    break;
            }
            if (oneShot != null)
            {
                oneT += dt;
                float q = Mathf.Clamp01(oneT / oneDur);
                bool clipDriven = currentClipKey == oneShot;
                if (!clipDriven)
                {
                    float arc = Mathf.Sin(q * Mathf.PI);
                    switch (oneShot)
                    {
                        case "attack": pos.z += arc * Height * 0.35f; euler.x += arc * 20f; break;
                        case "swing": euler.y += Mathf.Lerp(-70f, 80f, q); euler.x += arc * 10f; break;
                        case "throw": euler.x += Mathf.Sin(q * Mathf.PI * 2f) * -25f; pos.z += arc * Height * 0.12f; break;
                        case "cast": pos.y += arc * Height * 0.1f; scale *= 1f + arc * 0.08f; break;
                        case "hit":
                            pos.x += Mathf.Sin(oneT * 60f) * (1f - q) * Height * 0.05f;
                            euler.x -= arc * 15f;
                            break;
                        case "hop": case "jump": pos.y += arc * Height * 0.4f; break;
                        case "win": pos.y += Mathf.Abs(Mathf.Sin(q * Mathf.PI * 3f)) * Height * 0.3f; euler.y += q * 360f; break;
                        case "spin": euler.y += q * 360f; break;
                        case "lose": euler.x -= arc * 5f; scale.y *= 1f - arc * 0.25f; break;
                        case "down": euler.x -= Mathf.Min(1f, oneT * 4f) * 80f; pos.y = 0; break;
                        case "squash": scale.y *= 1f - arc * 0.3f; scale.x *= 1f + arc * 0.2f; scale.z = scale.x; break;
                    }
                }
                if (oneT >= oneDur)
                {
                    oneShot = null;
                    PlayLoopClip();
                }
            }
            Body.localPosition = pos;
            Body.localRotation = Quaternion.Euler(euler);
            Body.localScale = scale;
        }

        /// <summary>Hold a lying-down / knocked-out pose until cleared with <see cref="Act"/> or SetLoop.</summary>
        public void KnockDown(bool down)
        {
            if (down) { Act("down", 99999f); }
            else if (oneShot == "down") { oneShot = null; PlayLoopClip(); }
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
