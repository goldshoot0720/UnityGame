// Procedural batting pose for a humanoid Chibi: both hands grip the bat handle (two-bone arm IK),
// and the swing runs stance → load → contact → follow-through with hip/torso rotation and the head
// tracking the ball. Applied in LateUpdate on top of whatever clip the Chibi is playing (idle).
using UnityEngine;

namespace MoeGames.Game1
{
    public class Batter : MonoBehaviour
    {
        /// <summary>Swing progress: 0 stance, 0.25 loaded, 0.5 contact, 1 follow-through finished.</summary>
        public float Swing;
        /// <summary>World point the head looks at (the ball / the pitcher).</summary>
        public Vector3 LookAt;
        /// <summary>The bat (handle at local origin, barrel along local +z); positioned every frame.</summary>
        public Transform Bat;

        Chibi chibi;
        Transform hips, spine, chest, head, lUp, lLow, lHand, rUp, rLow, rHand, lFinger, rFinger;
        Transform lThigh, lShin, lFoot, rThigh, rShin, rFoot;
        public bool Rigged { get; private set; }

        // Key poses in the batter's frame (x = toward the catcher, y = up, z = toward the plate), with
        // hand positions relative to the shoulder centre in arm lengths. Right-handed batter.
        // Reference: JOE是棒球, https://www.youtube.com/watch?v=sZ4nUqtBlFo
        // 1:37: inward hip hinge; 2:10: avoid excessive shoulder rotation during the load.
        // 引棒: the hands load back to the rear shoulder, then lead knob-first down toward the ball with
        // the barrel still above them, and only then does the barrel flatten into the zone.
        // The launch key separates the pelvis opening from the hands/barrel releasing.
        static readonly float[] KeyS = { 0f, 0.25f, 0.34f, 0.5f, 0.7f, 1f };
        static readonly Vector3[] KeyHands =
        {
            new Vector3(0.40f, -0.08f, 0.14f),   // stance, hands at the rear shoulder
            new Vector3(0.50f, -0.04f, 0.06f),   // load: hands ease back (引棒), still compact
            new Vector3(0.36f, -0.30f, 0.22f),   // launch: knob leads down toward the ball, hands stay back
            new Vector3(-0.02f, -0.60f, 0.55f),  // contact
            new Vector3(-0.43f, -0.46f, 0.42f),  // extension toward pitcher
            new Vector3(-0.45f, 0.08f, 0.12f),   // finish high over the lead shoulder
        };
        static readonly Vector3[] KeyBat =
        {
            new Vector3(0.30f, 0.90f, -0.25f),  // bat up, tipped back over the rear shoulder
            new Vector3(0.42f, 0.84f, -0.28f),
            new Vector3(0.55f, 0.72f, -0.40f),  // barrel lags above the hands (knob to the ball)
            new Vector3(-0.20f, -0.05f, 1f),
            new Vector3(-1f, 0.12f, 0.30f),
            new Vector3(0.70f, 0.35f, -0.60f),
        };
        // Absolute turns in the batter frame. Torso stays quiet while the hips load/launch.
        static readonly float[] KeyHips = { 0f, -10f, 22f, 48f, 70f, 85f };
        static readonly float[] KeyTorso = { 0f, -8f, -5f, 55f, 92f, 112f };

        public static Batter Attach(Chibi c, Transform bat)
        {
            var b = c.gameObject.AddComponent<Batter>();
            b.chibi = c;
            b.Bat = bat;
            b.hips = c.Bone(HumanBodyBones.Hips);
            b.spine = c.Bone(HumanBodyBones.Spine);
            b.chest = c.Bone(HumanBodyBones.Chest) ?? b.spine;
            b.head = c.Bone(HumanBodyBones.Head);
            b.lUp = c.Bone(HumanBodyBones.LeftUpperArm); b.lLow = c.Bone(HumanBodyBones.LeftLowerArm); b.lHand = c.Bone(HumanBodyBones.LeftHand);
            b.rUp = c.Bone(HumanBodyBones.RightUpperArm); b.rLow = c.Bone(HumanBodyBones.RightLowerArm); b.rHand = c.Bone(HumanBodyBones.RightHand);
            b.lThigh = c.Bone(HumanBodyBones.LeftUpperLeg); b.lShin = c.Bone(HumanBodyBones.LeftLowerLeg); b.lFoot = c.Bone(HumanBodyBones.LeftFoot);
            b.rThigh = c.Bone(HumanBodyBones.RightUpperLeg); b.rShin = c.Bone(HumanBodyBones.RightLowerLeg); b.rFoot = c.Bone(HumanBodyBones.RightFoot);
            b.lFinger = c.Bone(HumanBodyBones.LeftMiddleProximal); b.rFinger = c.Bone(HumanBodyBones.RightMiddleProximal);
            b.Rigged = b.hips && b.spine && b.head && b.lUp && b.lLow && b.lHand && b.rUp && b.rLow && b.rHand;
            return b;
        }

        /// <summary>Interpolated key pose at swing progress s.</summary>
        static void Pose(float s, out Vector3 hands, out Vector3 dir, out float hipTurn, out float torsoTurn)
        {
            s = Mathf.Clamp01(s);
            int i = 0;
            while (i < KeyS.Length - 2 && s > KeyS[i + 1]) i++;
            float u = Mathf.InverseLerp(KeyS[i], KeyS[i + 1], s);
            // Ease into the load and the held finish; keep moving through contact.
            if (i == 0 || i == KeyS.Length - 2) u = u * u * (3 - 2 * u);
            hands = Vector3.Lerp(KeyHands[i], KeyHands[i + 1], u);
            dir = Vector3.Slerp(KeyBat[i].normalized, KeyBat[i + 1].normalized, u);
            hipTurn = Mathf.Lerp(KeyHips[i], KeyHips[i + 1], u);
            torsoTurn = Mathf.Lerp(KeyTorso[i], KeyTorso[i + 1], u);
        }

        void LateUpdate() => ApplyPose();

        /// <summary>Pose the (freshly animated) body for the current <see cref="Swing"/>.</summary>
        public void ApplyPose()
        {
            if (!Rigged || !Bat) return;
            // Reset before adding offsets, including manual previews / repeated samples.
            chibi.Resample();
            var root = transform;
            Vector3 up = root.up;
            // Measure the (animated) body before posing it.
            Vector3 shoulders = (lUp.position + rUp.position) * 0.5f;
            float arm = Vector3.Distance(rUp.position, rLow.position) + Vector3.Distance(rLow.position, rHand.position);
            float s = Swing;
            if (s <= 0.001f) s = Mathf.Sin(Time.time * 2.2f) * 0.03f + 0.03f; // bat waggle in the stance
            Pose(s, out var handsLocal, out var dirLocal, out float hipTurn, out float torsoTurn);
            float load = Mathf.SmoothStep(0, 1, s / 0.25f) * (1 - Mathf.SmoothStep(0, 1, (s - 0.25f) / 0.25f));
            float release = Mathf.SmoothStep(0, 1, (s - 0.28f) / 0.42f);

            // Athletic stance: feet wider apart, knees bent, then the body drops so the feet stay on the ground.
            bool legs = lThigh && lShin && lFoot && rThigh && rShin && rFoot;
            float footY = legs ? Mathf.Min(lFoot.position.y, rFoot.position.y) : 0;
            if (legs)
            {
                Vector3 fwd = root.forward, right = root.right;
                const float Spread = 8f, Bend = 16f;
                rThigh.rotation = Quaternion.AngleAxis(-Bend, right) * Quaternion.AngleAxis(Spread, fwd) * rThigh.rotation;
                lThigh.rotation = Quaternion.AngleAxis(-Bend, right) * Quaternion.AngleAxis(-Spread, fwd) * lThigh.rotation;
                rShin.rotation = Quaternion.AngleAxis(Bend * 2, right) * rShin.rotation;
                lShin.rotation = Quaternion.AngleAxis(Bend * 2, right) * lShin.rotation;
                rFoot.rotation = Quaternion.AngleAxis(-Bend, right) * rFoot.rotation;
                lFoot.rotation = Quaternion.AngleAxis(-Bend, right) * lFoot.rotation;
            }

            // First establish a bent-knee stance and capture its feet. Re-solve the legs
            // after the pelvis moves, rather than letting hip rotation drag both feet around.
            if (legs) hips.position += up * (footY - Mathf.Min(lFoot.position.y, rFoot.position.y));
            Vector3 leftPlant = legs ? lFoot.position : Vector3.zero;
            Vector3 rightPlant = legs ? rFoot.position : Vector3.zero;
            Quaternion leftSole = legs ? lFoot.rotation : Quaternion.identity;
            Quaternion rightSole = legs ? rFoot.rotation : Quaternion.identity;
            float height = chibi.Height;
            // Sit into the rear hip with a small inward coil, keeping the head inside the base.
            hips.position += root.TransformDirection(new Vector3(0.035f * load - 0.025f * release,
                -0.018f * load - 0.03f * release, -0.025f * load)) * height;
            float hinge = 10f + 8f * load - 6f * release;
            hips.rotation = Quaternion.AngleAxis(-hipTurn, up) * Quaternion.AngleAxis(hinge, root.right) * hips.rotation;
            float separation = torsoTurn - hipTurn;
            spine.rotation = Quaternion.AngleAxis(-separation * (chest != spine ? 0.5f : 1f), up) * spine.rotation;
            if (chest != spine) chest.rotation = Quaternion.AngleAxis(-separation * 0.5f, up) * chest.rotation;
            if (legs)
            {
                SolveArm(lThigh, lShin, lFoot, leftPlant, leftPlant + root.forward * height);
                // Rear heel releases after launch; the toe stays close to its planted location.
                float heel = 24f * release;
                var pivot = Quaternion.AngleAxis(-65f * release, up) * Quaternion.AngleAxis(heel, root.right);
                Vector3 toeOffset = root.forward * (height * 0.075f);
                // The rear foot drags a little toward the front one so the rear knee can fold in and down.
                Vector3 rearTarget = rightPlant + toeOffset - pivot * toeOffset - root.right * (height * 0.06f * release);
                SolveArm(rThigh, rShin, rFoot, rearTarget, rightPlant + root.forward * height - root.right * height * 0.3f * release);
                lFoot.rotation = leftSole;
                rFoot.rotation = pivot * rightSole;
            }
            shoulders = (lUp.position + rUp.position) * 0.5f;

            // Bat: handle at the hands, barrel along the key direction (fixed in the batter's frame).
            var handsWorld = shoulders + root.TransformDirection(handsLocal) * arm;
            var dirWorld = root.TransformDirection(dirLocal).normalized;
            float hand = arm * 0.12f;
            float leftReach = (Vector3.Distance(lUp.position, lLow.position) + Vector3.Distance(lLow.position, lHand.position)) * 0.98f;
            float rightReach = arm * 0.98f;
            float leftMin = Mathf.Abs(Vector3.Distance(lUp.position, lLow.position) - Vector3.Distance(lLow.position, lHand.position)) + 0.002f;
            float rightMin = Mathf.Abs(Vector3.Distance(rUp.position, rLow.position) - Vector3.Distance(rLow.position, rHand.position)) + 0.002f;
            // Short-armed characters need a closer handle, especially at full extension.
            // Project the shared handle into both reach spheres; never detach one hand.
            for (int i = 0; i < 16; i++)
            {
                handsWorld = ClampReach(handsWorld, lUp.position, leftMin, leftReach);
                Vector3 rightOrigin = rUp.position - dirWorld * hand;
                handsWorld = ClampReach(handsWorld, rightOrigin, rightMin, rightReach);
            }
            Bat.position = handsWorld;
            Bat.rotation = Quaternion.LookRotation(dirWorld, Mathf.Abs(Vector3.Dot(dirWorld, up)) > 0.99f ? root.forward : up);

            // Both hands on the handle: left (bottom) at the knob end, right hand just above it.
            Vector3 rightElbowHint = rUp.position + root.TransformDirection(new Vector3(0.5f, -1f, -0.3f));
            Vector3 leftElbowHint = lUp.position + root.TransformDirection(new Vector3(-0.3f, -1f, 0.4f));
            SolveArm(lUp, lLow, lHand, handsWorld, leftElbowHint);
            SolveArm(rUp, rLow, rHand, handsWorld + dirWorld * hand, rightElbowHint);
            AimHand(lHand, lFinger, dirWorld);
            AimHand(rHand, rFinger, dirWorld);

            // Head: turn toward the ball / pitcher (most of the way).
            // Counter the hip hinge so the head does not nod down into the load.
            head.rotation = Quaternion.AngleAxis(-hinge, root.right) * head.rotation;
            var facing = Quaternion.AngleAxis(-torsoTurn, up) * root.forward;
            var want = Vector3.ProjectOnPlane(LookAt - head.position, up);
            if (want.sqrMagnitude > 0.0001f)
            {
                var delta = Quaternion.FromToRotation(Vector3.ProjectOnPlane(facing, up), want);
                head.rotation = Quaternion.Slerp(Quaternion.identity, delta, 0.8f) * head.rotation;
            }
        }

        static Vector3 ClampReach(Vector3 target, Vector3 origin, float min, float max)
        {
            Vector3 delta = target - origin;
            return origin + (delta.sqrMagnitude > 1e-8f ? delta.normalized : Vector3.forward) * Mathf.Clamp(delta.magnitude, min, max);
        }

        /// <summary>Two-bone IK: place the elbow in the plane of shoulder, target and hint, then point
        /// the upper and lower arm at it (positional law-of-cosines solve; no sign conventions).</summary>
        static void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 hint)
        {
            Vector3 a = upper.position;
            float lab = Vector3.Distance(a, lower.position), lbc = Vector3.Distance(lower.position, hand.position);
            Vector3 at = target - a;
            float lat = Mathf.Clamp(at.magnitude, Mathf.Abs(lab - lbc) + 0.001f, (lab + lbc) * 0.999f);
            Vector3 dir = at.normalized;
            float x = (lab * lab - lbc * lbc + lat * lat) / (2 * lat);
            float h = Mathf.Sqrt(Mathf.Max(0, lab * lab - x * x));
            Vector3 bend = Vector3.ProjectOnPlane(hint - a, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            Vector3 elbow = a + dir * x + bend.normalized * h;
            upper.rotation = Quaternion.FromToRotation(lower.position - a, elbow - a) * upper.rotation;
            Vector3 reach = a + dir * lat;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, reach - lower.position) * lower.rotation;
        }

        /// <summary>Keep the hand in line with the forearm and roll it so the fingers wrap the handle.</summary>
        static void AimHand(Transform hand, Transform finger, Vector3 batDir)
        {
            if (!finger) return;
            var fingers = finger.position - hand.position;
            if (fingers.sqrMagnitude < 1e-8f) return;
            // Fingers curl perpendicular to the bat.
            var want = Vector3.ProjectOnPlane(fingers, batDir);
            if (want.sqrMagnitude > 1e-8f) hand.rotation = Quaternion.FromToRotation(fingers, want) * hand.rotation;
        }
    }
}
