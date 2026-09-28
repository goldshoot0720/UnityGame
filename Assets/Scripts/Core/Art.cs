// Original Phaser Game Agent art (Assets/PhaserAssets, via MoeRegistry) in 3D: sprites as
// camera-facing billboards or flat decals (SpriteRenderer = always-included, alpha-blended shader),
// backdrops, and textured materials. Everything returns null / falls back when an image is missing.
using System.Collections.Generic;
using UnityEngine;

namespace MoeGames
{
    public static class Art
    {
        static readonly Dictionary<(Texture2D, int, int), Sprite> sprites = new Dictionary<(Texture2D, int, int), Sprite>();

        public static Texture2D Tex(string name, string scope = null)
        {
            var reg = MoeRegistry.I;
            return reg ? reg.Texture(name, scope ?? Sfx.Scope) : null;
        }

        /// <summary>A sprite for a texture; frames &gt; 1 splits a vertical strip and takes frame index.</summary>
        public static Sprite Sprite(Texture2D tex, int frames = 1, int frame = 0)
        {
            if (!tex) return null;
            if (sprites.TryGetValue((tex, frames, frame), out var s) && s) return s;
            float fh = tex.height / (float)frames;
            s = UnityEngine.Sprite.Create(tex, new Rect(0, tex.height - fh * (frame + 1), tex.width, fh), new Vector2(0.5f, 0f), fh);
            sprites[(tex, frames, frame)] = s;
            return s;
        }

        /// <summary>A sprite standing on its bottom edge, <paramref name="height"/> world units tall.</summary>
        public static SpriteRenderer Standee(Transform parent, string name, Vector3 pos, float height, bool billboard = true, string scope = null, int frames = 1, int frame = 0)
        {
            var sp = Sprite(Tex(name, scope), frames, frame);
            if (!sp) return null;
            var go = new GameObject("art_" + name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            go.transform.localScale = Vector3.one * height;
            if (billboard) go.AddComponent<Billboard>();
            return sr;
        }

        /// <summary>A flat sprite lying on the ground (top-down games), size = world width.</summary>
        public static SpriteRenderer Decal(Transform parent, string name, Vector3 center, float width, float yaw = 0, string scope = null, int frames = 1, int frame = 0)
        {
            var tex = Tex(name, scope);
            var sp = Sprite(tex, frames, frame);
            if (!sp) return null;
            var go = new GameObject("decal_" + name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Centered(tex, frames, frame);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.Euler(90, yaw, 0);
            float s = width / (sr.sprite.rect.width / sr.sprite.pixelsPerUnit);
            go.transform.localScale = Vector3.one * s;
            return sr;
        }

        static readonly Dictionary<(Texture2D, int, int), Sprite> centered = new Dictionary<(Texture2D, int, int), Sprite>();
        static Sprite Centered(Texture2D tex, int frames, int frame)
        {
            if (centered.TryGetValue((tex, frames, frame), out var s) && s) return s;
            float fh = tex.height / (float)frames;
            s = UnityEngine.Sprite.Create(tex, new Rect(0, tex.height - fh * (frame + 1), tex.width, fh), new Vector2(0.5f, 0.5f), fh);
            centered[(tex, frames, frame)] = s;
            return s;
        }

        /// <summary>A large upright backdrop image centred at <paramref name="center"/>, <paramref name="height"/> tall.</summary>
        public static SpriteRenderer Backdrop(Transform parent, string name, Vector3 center, float height, string scope = null)
        {
            var tex = Tex(name, scope);
            if (!tex) return null;
            var go = new GameObject("backdrop_" + name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Centered(tex, 1, 0);
            go.transform.localPosition = center;
            go.transform.localScale = Vector3.one * height;
            sr.sortingOrder = -100;
            return sr;
        }

        /// <summary>A lit material using an original texture (for tiles/crates), or null.</summary>
        public static Material Material(string name, Vector2 tiling, string scope = null)
        {
            var tex = Tex(name, scope);
            if (!tex) return null;
            var m = new Material(Mats.Base) { mainTexture = tex, mainTextureScale = tiling, color = Color.white };
            return m;
        }

        public static bool Apply(GameObject go, string name, Vector2 tiling, string scope = null)
        {
            var m = Material(name, tiling, scope);
            if (!m || !go) return false;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = m;
            return true;
        }
    }

    /// <summary>Keeps a sprite facing the camera (rotating around the vertical axis only).</summary>
    public class Billboard : MonoBehaviour
    {
        public bool Upright = true;
        void LateUpdate()
        {
            var cam = Camera.main;
            if (!cam) return;
            var fwd = cam.transform.forward;
            if (Upright) { fwd.y = 0; if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward; }
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }
    }
}
