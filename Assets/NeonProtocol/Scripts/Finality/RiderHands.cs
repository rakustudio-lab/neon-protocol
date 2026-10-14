using UnityEngine;

namespace NeonProtocol
{
    // R4: the rider's hands ON the grips (two-bone arm IK after RiderPose), and the cockpit camera at the rider's eyes
    // (head hidden, like FirstPersonBody on foot). Runs after RiderPose and Lightcycle so the camera and arms use this
    // frame's pose (a one-frame lag is a metre at 60 m/s).
    [DefaultExecutionOrder(500)]
    public class RiderHands : MonoBehaviour
    {
        public Lightcycle bike;
        public Transform gripL, gripR;
        public Vector3 eyeOffset = new Vector3(0f, 0.16f, -0.06f);   // bike frame, from the head bone
        Transform[] fingersL, fingersR;
        Animator an; Transform uaL, faL, hL, uaR, faR, hR, head, mL, mR; Vector3 headScale;

        void OnEnable()
        {
            an = GetComponentInChildren<Animator>(true); if (!an) { enabled = false; return; }
            uaL = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); faL = an.GetBoneTransform(HumanBodyBones.LeftLowerArm); hL = an.GetBoneTransform(HumanBodyBones.LeftHand);
            uaR = an.GetBoneTransform(HumanBodyBones.RightUpperArm); faR = an.GetBoneTransform(HumanBodyBones.RightLowerArm); hR = an.GetBoneTransform(HumanBodyBones.RightHand);
            head = an.GetBoneTransform(HumanBodyBones.Head); if (head) headScale = head.localScale;
            mL = an.GetBoneTransform(HumanBodyBones.LeftMiddleProximal); mR = an.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            fingersL = Fingers(true); fingersR = Fingers(false);
        }
        void OnDisable() { if (head) head.localScale = headScale; }

        void LateUpdate()
        {
            if (!bike || !gripL || !gripR) return;
            Vector3 up = bike.transform.up, right = bike.transform.right;
            Solve(uaL, faL, hL, gripL.position + up * 0.04f, -right * 0.35f - up * 0.25f);
            Solve(uaR, faR, hR, gripR.position + up * 0.04f, right * 0.35f - up * 0.25f);
            // wrists: fingers point forward-down over the bar (the knuckles lead), so the curled fingers wrap it
            Vector3 f = bike.transform.forward;
            Aim(hL, mL, (f * 0.9f - up * 0.35f - right * 0.2f).normalized); Aim(hR, mR, (f * 0.9f - up * 0.35f + right * 0.2f).normalized);
            Curl(fingersL, hL, mL, up); Curl(fingersR, hR, mR, up);
            bool cockpit = Lightcycle.Cockpit && (bike.Active || bike.Finale);
            if (head) head.localScale = cockpit ? Vector3.one * 0.001f : headScale;
            // The cockpit lens stays in the bike frame; head animation must not move it.
        }

        // fingers wrap the bar: each joint turns down about the knuckle line (independent of the avatar muscle setup)
        Transform[] Fingers(bool left)
        {
            var l = new System.Collections.Generic.List<Transform>();
            var b = left ? new[] { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal, HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal }
                            : new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal, HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal };
            foreach (var x in b) l.Add(an.GetBoneTransform(x));
            return l.ToArray();
        }
        static readonly float[] curl = { 55f, 70f, 50f };
        static void Curl(Transform[] f, Transform hand, Transform mid, Vector3 up)
        {
            if (f == null || !hand || !mid) return;
            Vector3 dir = (mid.position - hand.position).normalized, axis = Vector3.Cross(up, dir).normalized;
            for (int i = 0; i < f.Length; i++) if (f[i]) f[i].rotation = Quaternion.AngleAxis(curl[i % 3], axis) * f[i].rotation;
        }

        static void Aim(Transform hand, Transform finger, Vector3 want)
        {
            if (!hand || !finger) return;
            hand.rotation = Quaternion.FromToRotation(finger.position - hand.position, want) * hand.rotation;
        }

        static void Solve(Transform a, Transform b, Transform c, Vector3 target, Vector3 hintDir)
        {
            if (!a || !b || !c) return;
            float la = (b.position - a.position).magnitude, lb = (c.position - b.position).magnitude;
            Vector3 at = target - a.position; float d = Mathf.Clamp(at.magnitude, 0.05f, la + lb - 0.002f);
            Vector3 dir = at.normalized;
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            Vector3 n = Vector3.Cross(dir, hintDir); if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(dir, Vector3.up);
            n.Normalize();
            Vector3 elbow = a.position + Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, n) * dir * la;
            a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, target - b.position) * b.rotation;
        }
    }
}
