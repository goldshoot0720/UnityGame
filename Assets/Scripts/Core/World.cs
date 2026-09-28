// 3D building blocks shared by all games: pipeline-agnostic coloured materials, primitives
// without colliders, particle-ish bursts, camera shake and simple world-space labels.
using System.Collections.Generic;
using UnityEngine;

namespace MoeGames
{
    public static class Mats
    {
        static Material baseMat;
        static readonly Dictionary<Color32, Material> cache = new Dictionary<Color32, Material>();
        static readonly Dictionary<Color32, Material> glowCache = new Dictionary<Color32, Material>();

        /// <summary>The active render pipeline's default lit material (works for Built-in and URP).</summary>
        public static Material Base
        {
            get
            {
                if (baseMat == null)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    baseMat = go.GetComponent<Renderer>().sharedMaterial;
                    Object.DestroyImmediate(go);
                }
                return baseMat;
            }
        }

        public static Material Get(Color c)
        {
            Color32 k = c;
            if (cache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(Base) { color = c, name = "moe_" + ColorUtility.ToHtmlStringRGBA(c) };
            cache[k] = m;
            return m;
        }

        public static Material Get(string hex) => Get(Js.Hex(hex));

        /// <summary>Self-lit colour (emission) for projectiles, pickups and effects.</summary>
        public static Material Glow(Color c)
        {
            Color32 k = c;
            if (glowCache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(Base) { color = c, name = "moeglow_" + ColorUtility.ToHtmlStringRGBA(c) };
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * 1.2f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            glowCache[k] = m;
            return m;
        }

        /// <summary>A generated material from the registry (Assets/Generated/&lt;scope&gt;/…/name.mat), cloned with
        /// the given texture tiling; null when it has not been generated yet.</summary>
        public static Material Generated(string name, string scope, Vector2 tiling)
        {
            var reg = MoeRegistry.I;
            var src = reg ? reg.Material(name, scope) : null;
            if (!src) return null;
            var m = new Material(src) { mainTextureScale = tiling };
            return m;
        }

        /// <summary>Put a generated material on a renderer if it exists (keeps the colour otherwise).</summary>
        public static bool TryApply(GameObject go, string name, string scope, Vector2 tiling)
        {
            var m = Generated(name, scope, tiling);
            if (!m || !go) return false;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = m;
            return true;
        }

        /// <summary>A material showing a texture (e.g. a Unity-AI generated one), tinted.</summary>
        public static Material Textured(Texture tex, Color tint)
        {
            var m = new Material(Base) { color = tint, mainTexture = tex };
            return m;
        }
    }

    public static class Prim
    {
        public static GameObject Make(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Color color, string name = null)
        {
            var go = GameObject.CreatePrimitive(t);
            var col = go.GetComponent<Collider>();
            if (col) Object.Destroy(col);
            if (name != null) go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mats.Get(color);
            return go;
        }

        public static GameObject Box(Transform p, Vector3 pos, Vector3 size, Color c, string name = null) => Make(PrimitiveType.Cube, p, pos, size, c, name);
        public static GameObject Sphere(Transform p, Vector3 pos, float d, Color c, string name = null) => Make(PrimitiveType.Sphere, p, pos, Vector3.one * d, c, name);
        public static GameObject Cyl(Transform p, Vector3 pos, float d, float h, Color c, string name = null) => Make(PrimitiveType.Cylinder, p, pos, new Vector3(d, h * 0.5f, d), c, name);
        public static GameObject Capsule(Transform p, Vector3 pos, float d, float h, Color c, string name = null) => Make(PrimitiveType.Capsule, p, pos, new Vector3(d, h * 0.5f, d), c, name);

        /// <summary>A flat tile lying on the ground (a thin box, so it is lit and visible from above).</summary>
        public static GameObject Tile(Transform p, Vector3 center, float w, float d, Color c, float thick = 0.02f, string name = null)
            => Make(PrimitiveType.Cube, p, center + Vector3.up * (thick * 0.5f), new Vector3(w, thick, d), c, name);

        public static GameObject Empty(string name, Transform parent = null, Vector3 pos = default)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            return go;
        }

        public static void SetColor(GameObject go, Color c, bool glow = false)
        {
            if (!go) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = glow ? Mats.Glow(c) : Mats.Get(c);
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.Destroy(t.GetChild(i).gameObject);
        }
    }

    /// <summary>Tiny particle bursts made of primitives (no particle materials needed).</summary>
    public class Fx : MonoBehaviour
    {
        class P { public Transform t; public Vector3 v; public float life, max, size; public float grav; }
        readonly List<P> live = new List<P>();
        readonly Stack<GameObject> pool = new Stack<GameObject>();
        static Fx inst;

        public static Fx I
        {
            get
            {
                if (!inst)
                {
                    var go = new GameObject("MoeFx");
                    DontDestroyOnLoad(go);
                    inst = go.AddComponent<Fx>();
                }
                return inst;
            }
        }

        public static void Burst(Vector3 pos, int count, Color color, float speed = 4f, float life = 0.5f, float size = 0.12f, float gravity = 6f)
        {
            var fx = I;
            for (int i = 0; i < count; i++)
            {
                GameObject go = fx.pool.Count > 0 ? fx.pool.Pop() : null;
                if (!go)
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(fx.transform, false);
                }
                go.SetActive(true);
                go.GetComponent<Renderer>().sharedMaterial = Mats.Glow(color);
                go.transform.position = pos;
                go.transform.rotation = Random.rotation;
                go.transform.localScale = Vector3.one * size;
                var v = Random.onUnitSphere * speed * Random.Range(0.4f, 1f);
                v.y = Mathf.Abs(v.y) * 0.8f + speed * 0.2f;
                fx.live.Add(new P { t = go.transform, v = v, life = life * Random.Range(0.7f, 1f), max = life, size = size, grav = gravity });
            }
        }

        public static void Clear()
        {
            if (!inst) return;
            foreach (var p in inst.live) { p.t.gameObject.SetActive(false); inst.pool.Push(p.t.gameObject); }
            inst.live.Clear();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var p = live[i];
                p.life -= dt;
                if (p.life <= 0 || !p.t)
                {
                    if (p.t) { p.t.gameObject.SetActive(false); pool.Push(p.t.gameObject); }
                    live.RemoveAt(i);
                    continue;
                }
                p.v.y -= p.grav * dt;
                p.t.position += p.v * dt;
                p.t.Rotate(360f * dt, 200f * dt, 0);
                p.t.localScale = Vector3.one * p.size * Mathf.Clamp01(p.life / p.max * 1.5f);
            }
        }
    }

    /// <summary>Camera helper: place/aim, follow smoothing and screen shake.</summary>
    public class CamRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        Vector3 basePos;
        Quaternion baseRot;
        float shakeAmp, shakeTime, shakeDur;

        public static CamRig Create()
        {
            var cam = Camera.main;
            if (!cam)
            {
                var go = new GameObject("MoeCamera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            var rig = cam.gameObject.GetComponent<CamRig>();
            if (!rig) rig = cam.gameObject.AddComponent<CamRig>();
            rig.Cam = cam;
            return rig;
        }

        public void Set(Vector3 pos, Vector3 lookAt, float fov = 50f)
        {
            basePos = pos;
            baseRot = Quaternion.LookRotation(lookAt - pos, Vector3.up);
            Cam.orthographic = false;
            Cam.fieldOfView = fov;
            Apply();
        }

        public void SetOrtho(Vector3 pos, Vector3 lookAt, float size)
        {
            basePos = pos;
            baseRot = Quaternion.LookRotation(lookAt - pos, Vector3.up);
            Cam.orthographic = true;
            Cam.orthographicSize = size;
            Apply();
        }

        /// <summary>Smoothly move towards a target pose (call every frame).</summary>
        public void Follow(Vector3 pos, Vector3 lookAt, float sharpness, float dt)
        {
            float k = 1f - Mathf.Exp(-sharpness * dt);
            basePos = Vector3.Lerp(basePos, pos, k);
            var want = Quaternion.LookRotation(lookAt - pos, Vector3.up);
            baseRot = Quaternion.Slerp(baseRot, want, k);
        }

        /// <summary>Normalised viewport rect (e.g. render the 3D board only on the left 2/3).</summary>
        public void Viewport(Rect r) => Cam.rect = r;

        public void Background(Color c)
        {
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = c;
        }

        public void Shake(float amount, float duration)
        {
            shakeAmp = Mathf.Max(shakeAmp, amount);
            shakeTime = shakeDur = duration;
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            if (!Cam) Cam = GetComponent<Camera>();
            var off = Vector3.zero;
            if (shakeTime > 0)
            {
                shakeTime -= Time.unscaledDeltaTime;
                float a = shakeAmp * Mathf.Clamp01(shakeTime / Mathf.Max(0.001f, shakeDur));
                off = Random.insideUnitSphere * a;
                if (shakeTime <= 0) shakeAmp = 0;
            }
            transform.SetPositionAndRotation(basePos + off, baseRot);
        }
    }

    /// <summary>Floating world-space text (damage numbers etc.) drawn through <see cref="Gui"/>.</summary>
    public static class Popups
    {
        class Pop { public Vector3 pos; public string text; public Color color; public float size, life, max, rise; }
        static readonly List<Pop> list = new List<Pop>();

        public static void Add(Vector3 worldPos, string text, Color color, float size = 26f, float life = 1f, float rise = 1.2f)
            => list.Add(new Pop { pos = worldPos, text = text, color = color, size = size, life = life, max = life, rise = rise });

        public static void Clear() => list.Clear();

        internal static void Tick(float dt)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                list[i].life -= dt;
                if (list[i].life <= 0) list.RemoveAt(i);
            }
        }

        internal static void Draw(Camera cam)
        {
            if (!cam) return;
            foreach (var p in list)
            {
                float q = 1f - p.life / p.max;
                var sp = Gui.WorldToGui(cam, p.pos + Vector3.up * p.rise * q);
                if (sp.z < 0) continue;
                var c = p.color;
                c.a = Mathf.Clamp01(p.life / p.max * 2f);
                Gui.Label(p.text, sp.x, sp.y, p.size, c, 0.5f, 0.5f, Color.black);
            }
        }
    }
}
