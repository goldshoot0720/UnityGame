// Procedural jump shot for a humanoid Chibi, applied in LateUpdate on top of whatever clip is playing.
// Reference: Highlight NBA, 完美的投籃有多少細節？3步全面解析投籃姿勢 (https://www.youtube.com/watch?v=KEe1P7aJvGk):
//   1. legs — knees point at the rim, load the hips and drive up through the arches;
//   2. set point — ball above the shooting-side brow, elbow under the ball, guide hand on its side;
//   3. release — the arm extends up and out, the wrist snaps fully down and the follow-through is held.
using UnityEngine;

namespace MoeGames.Game2
{
    public class Shooter : MonoBehaviour
    {
        public const float SetPoint = 0.6f, ReleasePoint = 0.72f;

        /// <summary>Shot progress: 0 holding, 0.3 dip, 0.6 set point, 0.72 release, 1 follow-through held.</summary>
        public float Shot;
        /// <summary>Blend of the shot pose over the animated pose (0 = pure clip).</summary>
        public float Weight;
        /// <summary>The rim: eyes stay on it through the shot.</summary>
        public Vector3 LookAt;
        /// <summary>Off the floor: legs hang extended with the toes pointed instead of planting.</summary>
        public bool Airborne;
        /// <summary>The ball; while <see cref="CarryBall"/> it sits between the hands.</summary>
        public Transform Ball;
        public bool CarryBall;
        public float BallRadius = 0.13f;

        /// <summary>Where the ball sits in the hands this frame (world).</summary>
        public Vector3 BallPoint { get; private set; }
        public bool Rigged { get; private set; }

        Chibi chibi;
        Transform hips, spine, chest, head, lUp, lLow, lHand, rUp, rLow, rHand, lFinger, rFinger;
        Transform lThigh, lShin, lFoot, rThigh, rShin, rFoot;
        Transform[] blended;
        Quaternion[] saved;
        bool carried;
        float handoff = -1;
        Vector3 handoffFrom;

        // Key poses in the shooter's frame (x = shooting side, y = up, z = toward the rim). Ball centre
        // relative to the shoulder centre in arm lengths; crouch = hip drop as a fraction of the height.
        static readonly float[] KeyS = { 0f, 0.3f, SetPoint, ReleasePoint, 1f };
        static readonly Vector3[] KeyBall =
        {
            new Vector3(0.10f, -0.55f, 0.55f),  // holding at the chest
            new Vector3(0.08f, -0.85f, 0.50f),  // dip to the shooting pocket at the waist
            new Vector3(0.45f, 0.85f, 0.30f),   // set point above the shooting-side brow, elbow under it
            new Vector3(0.30f, 1.40f, 0.60f),   // release, arm locked out up and toward the rim
            new Vector3(0.26f, 1.25f, 0.80f),   // follow-through (where the ball would be), held high
        };
        static readonly float[] KeyCrouch = { 0.015f, 0.085f, 0.045f, 0f, 0f };
        static readonly float[] KeyHinge = { 3f, 12f, 4f, 0f, 0f };
        // Shooting wrist: cocked back under the ball, then snapped fully down ("goose neck").
        static readonly float[] KeyWrist = { 0f, 0f, -35f, 10f, 80f };

        public static Shooter Attach(Chibi c)
        {
            var s = c.gameObject.AddComponent<Shooter>();
            s.chibi = c;
            s.hips = c.Bone(HumanBodyBones.Hips);
            s.spine = c.Bone(HumanBodyBones.Spine);
            s.chest = c.Bone(HumanBodyBones.Chest) ?? s.spine;
            s.head = c.Bone(HumanBodyBones.Head);
            s.lUp = c.Bone(HumanBodyBones.LeftUpperArm); s.lLow = c.Bone(HumanBodyBones.LeftLowerArm); s.lHand = c.Bone(HumanBodyBones.LeftHand);
            s.rUp = c.Bone(HumanBodyBones.RightUpperArm); s.rLow = c.Bone(HumanBodyBones.RightLowerArm); s.rHand = c.Bone(HumanBodyBones.RightHand);
            s.lThigh = c.Bone(HumanBodyBones.LeftUpperLeg); s.lShin = c.Bone(HumanBodyBones.LeftLowerLeg); s.lFoot = c.Bone(HumanBodyBones.LeftFoot);
            s.rThigh = c.Bone(HumanBodyBones.RightUpperLeg); s.rShin = c.Bone(HumanBodyBones.RightLowerLeg); s.rFoot = c.Bone(HumanBodyBones.RightFoot);
            s.lFinger = c.Bone(HumanBodyBones.LeftMiddleProximal); s.rFinger = c.Bone(HumanBodyBones.RightMiddleProximal);
            s.Rigged = s.hips && s.spine && s.head && s.lUp && s.lLow && s.lHand && s.rUp && s.rLow && s.rHand;
            if (s.Rigged)
            {
                s.blended = new[] { s.hips, s.spine, s.chest, s.head, s.lUp, s.lLow, s.lHand, s.rUp, s.rLow, s.rHand, s.lThigh, s.lShin, s.lFoot, s.rThigh, s.rShin, s.rFoot };
                s.saved = new Quaternion[s.blended.Length];
            }
            return s;
        }

        static void Pose(float s, out Vector3 ball, out float crouch, out float hinge, out float wrist)
        {
            s = Mathf.Clamp01(s);
            int i = 0;
            while (i < KeyS.Length - 2 && s > KeyS[i + 1]) i++;
            float u = Mathf.InverseLerp(KeyS[i], KeyS[i + 1], s);
            u = u * u * (3 - 2 * u);
            ball = Vector3.Lerp(KeyBall[i], KeyBall[i + 1], u);
            crouch = Mathf.Lerp(KeyCrouch[i], KeyCrouch[i + 1], u);
            hinge = Mathf.Lerp(KeyHinge[i], KeyHinge[i + 1], u);
            wrist = Mathf.Lerp(KeyWrist[i], KeyWrist[i + 1], u);
        }

        void LateUpdate()
        {
            if (Weight > 0.001f) ApplyPose();
            if (!Ball) return;
            if (CarryBall && Weight > 0.001f) { Ball.position = BallPoint; carried = true; }
            else if (carried) { carried = false; handoff = 0; handoffFrom = BallPoint; }
            // Ease the ball from the fingertips onto its flight path instead of popping.
            if (handoff >= 0)
            {
                handoff += Time.deltaTime / 0.12f;
                if (handoff >= 1) handoff = -1;
                else Ball.position = Vector3.Lerp(handoffFrom, Ball.position, handoff * handoff * (3 - 2 * handoff));
            }
        }

        /// <summary>Pose the (freshly animated) body for the current <see cref="Shot"/> and <see cref="Weight"/>.</summary>
        public void ApplyPose()
        {
            if (!Rigged) return;
            float w = Mathf.Clamp01(Weight);
            for (int i = 0; i < blended.Length; i++) if (blended[i]) saved[i] = blended[i].localRotation;
            Vector3 savedHips = hips.localPosition;

            var root = transform;
            Vector3 up = root.up, fwd = root.forward, right = root.right;
            float height = chibi.Height;
            float upR = Vector3.Distance(rUp.position, rLow.position), foreR = Vector3.Distance(rLow.position, rHand.position);
            float upL = Vector3.Distance(lUp.position, lLow.position), foreL = Vector3.Distance(lLow.position, lHand.position);
            float armR = upR + foreR, armL = upL + foreL;
            // Nearest a hand can come to its shoulder (fully folded elbow).
            float minR = Mathf.Max(Mathf.Abs(upR - foreR), armR * 0.2f) + 0.005f, minL = Mathf.Max(Mathf.Abs(upL - foreL), armL * 0.2f) + 0.005f;
            float palmR = rFinger ? Vector3.Distance(rHand.position, rFinger.position) : armR * 0.15f;
            float palmL = lFinger ? Vector3.Distance(lHand.position, lFinger.position) : armL * 0.15f;
            Pose(Shot, out var ballLocal, out float crouch, out float hinge, out float wrist);

            // Legs: sit into the hips with the knees driving toward the rim, feet planted; once off the
            // floor the legs hang long with the toes pointed.
            bool legs = lThigh && lShin && lFoot && rThigh && rShin && rFoot;
            if (legs && !Airborne)
            {
                Vector3 lPlant = lFoot.position, rPlant = rFoot.position;
                Quaternion lSole = lFoot.rotation, rSole = rFoot.rotation;
                hips.position += (-up * crouch - fwd * crouch * 0.35f) * height;
                hips.rotation = Quaternion.AngleAxis(hinge, right) * hips.rotation;
                SolveLimb(lThigh, lShin, lFoot, lPlant, lThigh.position + fwd * height - right * height * 0.05f);
                SolveLimb(rThigh, rShin, rFoot, rPlant, rThigh.position + fwd * height + right * height * 0.05f);
                lFoot.rotation = lSole;
                rFoot.rotation = rSole;
            }
            else
            {
                hips.rotation = Quaternion.AngleAxis(hinge, right) * hips.rotation;
                if (legs)
                {
                    lFoot.rotation = Quaternion.AngleAxis(25f, right) * lFoot.rotation;
                    rFoot.rotation = Quaternion.AngleAxis(25f, right) * rFoot.rotation;
                }
            }
            // Chest stays tall over the hips as the ball rises.
            spine.rotation = Quaternion.AngleAxis(-hinge * 0.4f, right) * spine.rotation;

            // Ball and hands. The shooting palm sits behind and under the ball, the guide hand on its side.
            Vector3 shoulders = (lUp.position + rUp.position) * 0.5f;
            Vector3 ball = shoulders + root.TransformDirection(ballLocal) * armR;
            float release = Mathf.InverseLerp(ReleasePoint, 1f, Shot);
            Vector3 rOff = (-fwd * 0.8f - up * 0.6f + right * 0.2f).normalized * (BallRadius + palmR * 0.55f);
            Vector3 lOff = (-right - fwd * 0.2f - up * 0.1f).normalized * (BallRadius + palmL * 0.55f);
            // After the release the guide hand peels off; it never pushes the ball.
            Vector3 lDrift = (-right * 0.10f - up * 0.12f - fwd * 0.04f) * armL * release;
            // Keep the ball where both hands can actually reach it (short chibi arms).
            for (int i = 0; i < 8; i++)
            {
                ball = ClampReach(ball + lOff + lDrift, lUp.position, minL, armL * 0.985f) - lOff - lDrift;
                ball = ClampReach(ball + rOff, rUp.position, minR, armR * 0.985f) - rOff;
            }
            // Elbow under the ball, pointing at the rim; guide elbow out to the side.
            SolveLimb(rUp, rLow, rHand, ball + rOff, rUp.position + (fwd * 0.7f - up + right * 0.15f) * armR);
            SolveLimb(lUp, lLow, lHand, ball + lOff + lDrift, lUp.position + (-right - up * 0.7f) * armL);
            AimFingers(rHand, rFinger, ball - rHand.position);
            AimFingers(lHand, lFinger, ball - lHand.position - lDrift);
            // Wrist: cocked back at the set point, snapped down through the follow-through.
            rHand.rotation = Quaternion.AngleAxis(wrist, right) * rHand.rotation;

            // Eyes on the rim.
            var facing = Quaternion.AngleAxis(hinge * 0.6f, right) * fwd;
            var want = LookAt - head.position;
            if (want.sqrMagnitude > 1e-4f)
            {
                var delta = Quaternion.FromToRotation(facing, want.normalized);
                head.rotation = Quaternion.Slerp(Quaternion.identity, delta, 0.6f) * head.rotation;
            }

            if (w < 0.999f)
            {
                for (int i = 0; i < blended.Length; i++)
                    if (blended[i]) blended[i].localRotation = Quaternion.Slerp(saved[i], blended[i].localRotation, w);
                hips.localPosition = Vector3.Lerp(savedHips, hips.localPosition, w);
            }
            // The ball follows the hands actually reached (after blending).
            BallPoint = (rHand.position - rOff + lHand.position - lOff - lDrift) * 0.5f;
        }

        static Vector3 ClampReach(Vector3 target, Vector3 origin, float min, float max)
        {
            Vector3 d = target - origin;
            return origin + (d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward) * Mathf.Clamp(d.magnitude, min, max);
        }

        /// <summary>Two-bone IK (arm or leg): elbow/knee in the plane of root, target and hint.</summary>
        static void SolveLimb(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint)
        {
            Vector3 a = upper.position;
            float lab = Vector3.Distance(a, lower.position), lbc = Vector3.Distance(lower.position, end.position);
            Vector3 at = target - a;
            float lat = Mathf.Clamp(at.magnitude, Mathf.Abs(lab - lbc) + 0.001f, (lab + lbc) * 0.999f);
            Vector3 dir = at.normalized;
            float x = (lab * lab - lbc * lbc + lat * lat) / (2 * lat);
            float h = Mathf.Sqrt(Mathf.Max(0, lab * lab - x * x));
            Vector3 bend = Vector3.ProjectOnPlane(hint - a, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            Vector3 joint = a + dir * x + bend.normalized * h;
            upper.rotation = Quaternion.FromToRotation(lower.position - a, joint - a) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, a + dir * lat - lower.position) * lower.rotation;
        }

        /// <summary>Point the fingers at the ball.</summary>
        static void AimFingers(Transform hand, Transform finger, Vector3 toward)
        {
            if (!finger) return;
            var fingers = finger.position - hand.position;
            if (fingers.sqrMagnitude < 1e-8f || toward.sqrMagnitude < 1e-8f) return;
            hand.rotation = Quaternion.FromToRotation(fingers, toward) * hand.rotation;
        }
    }
}
