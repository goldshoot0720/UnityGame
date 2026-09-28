// Draws many small coloured meshes (bullets, particles) per frame with GPU instancing instead of
// one GameObject each. Call Add(...) during Update, then the batches render in LateUpdate.
using System.Collections.Generic;
using UnityEngine;

namespace MoeGames
{
    public class Instanced : MonoBehaviour
    {
        static Instanced inst;
        static Mesh sphere, cube;
        readonly Dictionary<(Color32, bool, bool), List<Matrix4x4>> batches = new Dictionary<(Color32, bool, bool), List<Matrix4x4>>();
        readonly Dictionary<(Color32, bool), Material> mats = new Dictionary<(Color32, bool), Material>();
        readonly Matrix4x4[] buf = new Matrix4x4[1023];

        static Instanced I
        {
            get
            {
                if (!inst)
                {
                    var go = new GameObject("MoeInstanced");
                    DontDestroyOnLoad(go);
                    inst = go.AddComponent<Instanced>();
                    var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    sphere = s.GetComponent<MeshFilter>().sharedMesh;
                    Destroy(s);
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube = c.GetComponent<MeshFilter>().sharedMesh;
                    Destroy(c);
                }
                return inst;
            }
        }

        /// <summary>Queue a sphere (or cube) for this frame.</summary>
        public static void Add(Vector3 pos, Vector3 scale, Color color, bool glow = true, bool isCube = false, Quaternion? rot = null)
        {
            var key = ((Color32)color, glow, isCube);
            var b = I.batches;
            if (!b.TryGetValue(key, out var list)) b[key] = list = new List<Matrix4x4>();
            list.Add(Matrix4x4.TRS(pos, rot ?? Quaternion.identity, scale));
        }

        Material Mat(Color32 c, bool glow)
        {
            if (mats.TryGetValue((c, glow), out var m) && m) return m;
            m = new Material(glow ? Mats.Glow(c) : Mats.Get(c)) { enableInstancing = true };
            mats[(c, glow)] = m;
            return m;
        }

        void LateUpdate()
        {
            foreach (var kv in batches)
            {
                var list = kv.Value;
                if (list.Count == 0) continue;
                var mat = Mat(kv.Key.Item1, kv.Key.Item2);
                var mesh = kv.Key.Item3 ? cube : sphere;
                for (int i = 0; i < list.Count; i += buf.Length)
                {
                    int n = Mathf.Min(buf.Length, list.Count - i);
                    list.CopyTo(i, buf, 0, n);
                    Graphics.DrawMeshInstanced(mesh, 0, mat, buf, n, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
                }
                list.Clear();
            }
        }
    }
}
