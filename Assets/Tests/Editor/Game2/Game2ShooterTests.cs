using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using MoeGames.Game2;

namespace MoeGames.Tests
{
    public class Game2ShooterTests
    {
        /// <summary>Local rotations/positions of every transform under a model, to restore the
        /// animated pose between samples (edit mode has no animator update to do it).</summary>
        static List<(Transform t, Vector3 p, Quaternion r)> Snapshot(Transform root)
        {
            var l = new List<(Transform, Vector3, Quaternion)>();
            foreach (var t in root.GetComponentsInChildren<Transform>()) l.Add((t, t.localPosition, t.localRotation));
            return l;
        }

        static void Restore(List<(Transform t, Vector3 p, Quaternion r)> snap)
        {
            foreach (var (t, p, r) in snap) { t.localPosition = p; t.localRotation = r; }
        }

        [TestCaseSource(typeof(Cast), nameof(Cast.Ids))]
        public void ShotKeepsHandsOnBallAndFeetPlanted(string id)
        {
            var root = new GameObject("ShooterTest");
            try
            {
                var c = Chibi.Spawn(id, root.transform, Vector3.zero, 1.7f, "Game2");
                var s = Shooter.Attach(c);
                Assert.IsTrue(s.Rigged, id + " humanoid rig");
                s.LookAt = new Vector3(0, 3.05f, 6f);
                s.Weight = 1;
                var snap = Snapshot(c.transform);
                var lFoot = c.Bone(HumanBodyBones.LeftFoot);
                var rFoot = c.Bone(HumanBodyBones.RightFoot);
                Vector3 l0 = lFoot.position, r0 = rFoot.position;
                var head = c.Bone(HumanBodyBones.Head);
                float worstGrip = 0, worstFoot = 0, lowestSet = 99, worstAt = 0;
                for (int i = 0; i <= 100; i++)
                {
                    Restore(snap);
                    s.Shot = i / 100f;
                    s.ApplyPose();
                    var ball = s.BallPoint;
                    if (s.Shot <= Shooter.ReleasePoint)
                    {
                        // Wrist → ball centre should be about ball radius + half a palm for both hands.
                        foreach (var h in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                        {
                            var hand = c.Bone(h);
                            var finger = c.Bone(h == HumanBodyBones.LeftHand ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                            float palm = finger ? Vector3.Distance(hand.position, finger.position) : 0.05f;
                            float err = Mathf.Abs(Vector3.Distance(hand.position, ball) - (s.BallRadius + palm * 0.55f));
                            if (err > worstGrip) { worstGrip = err; worstAt = s.Shot; }
                        }
                    }
                    if (Mathf.Abs(s.Shot - Shooter.SetPoint) < 0.005f) lowestSet = ball.y - head.position.y;
                    worstFoot = Mathf.Max(worstFoot, Vector3.Distance(l0, lFoot.position), Vector3.Distance(r0, rFoot.position));
                }
                Debug.Log($"SHOOTER_CHECK {id}: grip={worstGrip:F4}@{worstAt:F2} foot={worstFoot:F4} setAboveHead={lowestSet:F3}");
                Assert.Less(worstGrip, 0.03f, id + " both hands stay on the ball until the release");
                Assert.Less(worstFoot, 0.02f, id + " feet stay planted while grounded");
                Assert.Greater(lowestSet, -0.05f, id + " set point is up by the head, not at the chest");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RenderShotReferencePoses()
        {
            var root = new GameObject("ShotPreview");
            var rt = new RenderTexture(1600, 1350, 24);
            var tex = new Texture2D(1600, 1350, TextureFormat.RGB24, false);
            var oldRT = RenderTexture.active;
            try
            {
                var camera = new GameObject("PreviewCamera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform);
                camera.transform.position = new Vector3(0, 0.9f, 12);
                camera.transform.LookAt(new Vector3(0, 0.9f, 0));
                camera.orthographic = true;
                camera.orthographicSize = 4.5f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.14f, 0.19f, 0.25f);
                camera.targetTexture = rt;
                var light = new GameObject("PreviewLight").AddComponent<Light>();
                light.transform.SetParent(root.transform);
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                light.transform.rotation = Quaternion.Euler(35, 160, 0);
                // Rows: tshirt side view, tshirt from behind, penguin (chibi proportions) side view.
                float[] phases = { 0f, 0.3f, Shooter.SetPoint, Shooter.ReleasePoint, 1f };
                for (int row = 0; row < 3; row++)
                    for (int i = 0; i < phases.Length; i++)
                    {
                        var pos = new Vector3((2 - i) * -1.6f, 2.6f - row * 3f, 0);
                        var c = Chibi.Spawn(row == 2 ? "penguin" : "tshirt", root.transform, pos, 1.7f, "Game2");
                        c.Face(row == 1 ? Vector3.back : Vector3.left);
                        var s = Shooter.Attach(c);
                        s.Weight = 1;
                        s.Shot = phases[i];
                        s.LookAt = c.transform.position + c.transform.forward * 6 + Vector3.up * 3.05f;
                        s.ApplyPose();
                        if (phases[i] <= Shooter.ReleasePoint)
                        {
                            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                            Object.DestroyImmediate(ball.GetComponent<Collider>());
                            ball.transform.SetParent(root.transform);
                            ball.transform.position = s.BallPoint;
                            ball.transform.localScale = Vector3.one * s.BallRadius * 2;
                            ball.GetComponent<Renderer>().sharedMaterial = Mats.Get(new Color(0.94f, 0.48f, 0.12f));
                        }
                    }
                camera.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                Directory.CreateDirectory("Builds/Validation");
                File.WriteAllBytes("Builds/Validation/shooting-poses.png", tex.EncodeToPNG());
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
