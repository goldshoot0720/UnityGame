using System.IO;
using NUnit.Framework;
using UnityEngine;
using MoeGames.Game1;

namespace MoeGames.Tests
{
    public class Game1BatterTests
    {
        static GameObject Cylinder(Transform parent, Vector3 pos, float radius, float height, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(radius * 2, height * 0.5f, radius * 2);
            go.GetComponent<Renderer>().sharedMaterial = Mats.Get(color);
            return go;
        }
        [TestCaseSource(typeof(Cast), nameof(Cast.Ids))]
        public void SwingKeepsBothHandsOnBatAndLeadFootPlanted(string id)
        {
            var root = new GameObject("BatterTest");
            try
            {
                var c = Chibi.Spawn(id, root.transform, Vector3.zero, 1.6f, "Game1");
                var bat = new GameObject("Bat").transform;
                bat.SetParent(root.transform);
                var b = Batter.Attach(c, bat);
                Assert.IsTrue(b.Rigged, id + " humanoid rig");
                b.LookAt = new Vector3(-10, 1.2f, 0);
                b.Swing = 0.0011f;
                b.ApplyPose();
                var lead = c.Bone(HumanBodyBones.LeftFoot).position;
                float worstGrip = 0, worstFoot = 0;
                for (int i = 0; i <= 100; i++)
                {
                    b.Swing = Mathf.Max(0.0011f, i / 100f);
                    b.ApplyPose();
                    var l = c.Bone(HumanBodyBones.LeftHand);
                    var r = c.Bone(HumanBodyBones.RightHand);
                    float arm = Vector3.Distance(c.Bone(HumanBodyBones.RightUpperArm).position, c.Bone(HumanBodyBones.RightLowerArm).position)
                        + Vector3.Distance(c.Bone(HumanBodyBones.RightLowerArm).position, r.position);
                    worstGrip = Mathf.Max(worstGrip, Vector3.Distance(l.position, bat.position), Vector3.Distance(r.position, bat.position + bat.forward * arm * 0.12f));
                    worstFoot = Mathf.Max(worstFoot, Vector3.Distance(lead, c.Bone(HumanBodyBones.LeftFoot).position));
                    Vector3 first = bat.position;
                    b.ApplyPose();
                    Assert.Less(Vector3.Distance(first, bat.position), 0.001f, id + " repeated pose must not accumulate");
                }
                Debug.Log($"BATTER_CHECK {id}: grip={worstGrip:F5} foot={worstFoot:F5}");
                Assert.Less(worstGrip, 0.035f, id + " both hands must reach handle throughout swing");
                Assert.Less(worstFoot, 0.025f, id + " lead foot must remain planted");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RenderSwingReferencePoses()
        {
            var root = new GameObject("SwingPreview");
            var rt = new RenderTexture(1600, 1360, 24);
            var tex = new Texture2D(1600, 1360, TextureFormat.RGB24, false);
            var oldRT = RenderTexture.active;
            try
            {
                var camera = new GameObject("PreviewCamera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform);
                camera.transform.position = new Vector3(0, 2.15f, 8);
                camera.transform.LookAt(new Vector3(0, 0f, 0));
                camera.orthographic = true;
                camera.orthographicSize = 4.1f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.14f, 0.19f, 0.25f);
                camera.targetTexture = rt;
                var light = new GameObject("PreviewLight").AddComponent<Light>();
                light.transform.SetParent(root.transform);
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                light.transform.rotation = Quaternion.Euler(35, 160, 0);
                float[] phases = { 0.0011f, 0.25f, 0.34f, 0.5f, 0.7f, 1f };
                // Top row: seen from the plate side; bottom row: from the pitcher (centre-field view).
                for (int row = 0; row < 2; row++)
                for (int i = 0; i < phases.Length; i++)
                {
                    var c = Chibi.Spawn("tshirt", root.transform, new Vector3((2.5f - i) * 1.5f, row == 0 ? 1.1f : -2.9f, 0), 1.6f, "Game1");
                    if (row == 1) c.Face(Vector3.right);
                    var bat = new GameObject("Bat").transform;
                    bat.SetParent(root.transform);
                    var handle = Cylinder(bat, new Vector3(0, 0, 0.2f), 0.018f, 0.5f, new Color(0.8f, 0.55f, 0.25f));
                    handle.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    var barrel = Cylinder(bat, new Vector3(0, 0, 0.64f), 0.033f, 0.46f, new Color(0.9f, 0.7f, 0.4f));
                    barrel.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    var b = Batter.Attach(c, bat);
                    b.Swing = phases[i];
                    b.LookAt = c.transform.position - c.transform.right * 10 + Vector3.up * 1.2f;
                    b.ApplyPose();
                }
                camera.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                Directory.CreateDirectory("Builds/Validation");
                File.WriteAllBytes("Builds/Validation/batting-poses.png", tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = oldRT;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }
    }
}
