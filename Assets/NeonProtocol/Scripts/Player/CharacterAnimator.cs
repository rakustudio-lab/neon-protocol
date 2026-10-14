using UnityEngine;

namespace NeonProtocol
{
    // Drives the humanoid from a motion source (player or AI). Rebuilt after the audit:
    //  * The rifle is PARENTED to the right-hand bone (the Mixamo rifle clips already hold a rifle), so it follows
    //    every animation, including the tag-out fall. No more code-placed floating gun.
    //  * After animation (LateUpdate): the spine pitches/yaws around WORLD axes toward the aim (no bone-axis twist).
    //  * While aiming, the RIFLE POSE is authoritative: butt at the right shoulder, barrel on the aim point.
    //    The right arm is solved (two-bone IK) so the hand holds the grip, then the left hand goes to the foregrip.
    //    Retargeted rigs (CRYPTO) don't carry a trustworthy hand rotation, so we never rely on it while aiming.
    //    Sprint / tag-out blend back to the animated hand (the rifle stays parented to it, so it falls with the body).
    [RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(100)]
    public class CharacterAnimator : MonoBehaviour
    {
        public interface ISource
        {
            Vector3 Velocity { get; }
            bool Grounded { get; }
            bool Crouched { get; }
            bool Sprinting { get; }
            int AimMode { get; }          // 0 relaxed/hip, 1 ADS, 2 zoom
            bool CombatStance { get; }
            Vector3 AimPoint { get; }
            bool TaggedOut { get; }
            // Rescue 2026-10-11: turn-in-place, airborne-for-animation (coyote) and vertical speed. Defaults keep AI simple.
            float TurnRate => 0f;          // -1..1, signed yaw rate while turning in place
            bool TurningInPlace => false;
            void AddRootYaw(float degrees) { }   // root-motion yaw from the turn-in-place clips
            bool AnimGrounded => Grounded;
            float VerticalSpeed => 0f;
        }

        public Transform weapon;            // parented to the right hand at calibration
        public Transform muzzle;
        public Transform rightGrip, leftGrip;
        [Range(0, 1)] public float spinePitchShare = 0.85f;
        public float maxSpineYaw = 35f, maxHandCorrection = 14f;
        // Driven by RifleReloadVisual: support-hand palm target during the reload, and the canted "reload" rifle pose.
        [System.NonSerialized] public float leftOverrideW, reloadPose;
        [System.NonSerialized] public Vector3 leftOverridePos;
        public Vector3 reloadGripOffset = new Vector3(-0.04f, -0.16f, 0.48f);   // rifle held further from the chest while reloading
        public Vector3 handguardPoint = new Vector3(0f, -0.035f, 0.29f);   // where the support palm sits, in rifle space
        [Tooltip("Grip position in the aim frame, relative to the right shoulder (x right, y up, z along the aim)")]
        public Vector3 adsGripOffset = new Vector3(-0.12f, 0.04f, 0.47f), hipGripOffset = new Vector3(-0.06f, -0.12f, 0.42f);

        Animator anim;
        ISource src;
        Transform spine, chest, upperChest, lUpper, lLower, lHand, rUpper, rLower, rHand;
        Vector3 weaponLocalPos; Quaternion weaponLocalRot; bool weaponInHand;
        float adsBlend, adsBlendVel;
        HumanPoseHandler poseHandler; HumanPose pose; int[] fingerMuscles; bool[] fingerIsThumb;
        // Perf (R1 profile: 7 ms/frame for 5 characters + ~14k allocations/frame): the closed-hand finger pose is computed
        // ONCE through the muscle system, then applied per frame as cached local bone rotations.
        Transform[] fingerBones; Quaternion[] fingerClosed; bool fingersCached;
        Transform[] spineBones;
        static Camera mainCam;
        [System.NonSerialized] public int maxLayer = 9;
        // true first person: the rifle grip is placed relative to the CAMERA (hip lower-right, ADS under the sightline)
        public Vector3 fpHipGrip = new Vector3(0.15f, -0.26f, 0.34f), fpAdsGrip = new Vector3(0.0f, -0.215f, 0.29f);
        bool FP => FirstPersonBody.Active && FirstPersonBody.OwnerAnim == anim;   // QA rig diagnosis: 0 clips only, 1 +fingers, 2 +spine aim, 3 +right-arm IK, 4 +support hand
        Transform lIndex, lPinky, lMiddle;
        Vector2 move, moveVel;
        float aimW, aimWVel, alive = 1f, recoil, recoilVel;

        static readonly int pX = Animator.StringToHash("MoveX"), pZ = Animator.StringToHash("MoveZ"), pCrouch = Animator.StringToHash("Crouch"),
            pGround = Animator.StringToHash("Grounded"), pAim = Animator.StringToHash("Aiming"), pTag = Animator.StringToHash("TaggedOut"), pHit = Animator.StringToHash("Hit"),
            pTurn = Animator.StringToHash("Turn"), pTurning = Animator.StringToHash("Turning"), pVelY = Animator.StringToHash("VelY"), pSpeed = Animator.StringToHash("Speed"), pTurnMul = Animator.StringToHash("TurnSpeedMul");
        bool hasTurn;
        float block, blockVel;   // 0..1: rifle raised because the barrel would be inside a wall
        float leftW, leftWVel;

        void Awake()
        {
            anim = GetComponent<Animator>();
            anim.applyRootMotion = false;
            src = GetComponentInParent<ISource>();
            spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            upperChest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            lUpper = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            lLower = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            rUpper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rLower = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            lIndex = anim.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            lMiddle = anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            lPinky = anim.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            if (anim.avatar && anim.avatar.isHuman)
            {
                poseHandler = new HumanPoseHandler(anim.avatar, transform);
                var ids = new System.Collections.Generic.List<int>();
                for (int i = 0; i < HumanTrait.MuscleCount; i++)
                {
                    string n = HumanTrait.MuscleName[i];
                    if (n.Contains("Stretched") && (n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little") || n.Contains("Thumb"))) ids.Add(i);
                }
                fingerMuscles = ids.ToArray();
                var names = HumanTrait.MuscleName;                       // one copy (the getter allocates a new array per call)
                fingerIsThumb = new bool[fingerMuscles.Length];
                for (int k = 0; k < fingerMuscles.Length; k++) fingerIsThumb[k] = names[fingerMuscles[k]].Contains("Thumb");
                var fb = new System.Collections.Generic.List<Transform>();
                for (var b = HumanBodyBones.LeftThumbProximal; b <= HumanBodyBones.RightLittleDistal; b++) { var t = anim.GetBoneTransform(b); if (t) fb.Add(t); }
                fingerBones = fb.ToArray(); fingerClosed = new Quaternion[fingerBones.Length];
            }
            if (weapon && rHand && weapon.parent == rHand) { weaponLocalPos = weapon.localPosition; weaponLocalRot = weapon.localRotation; weaponInHand = true; }
            spineBones = upperChest ? new[] { spine, chest, upperChest } : new[] { spine, chest };
            foreach (var p in anim.parameters) if (p.nameHash == pTurn) hasTurn = true;
        }

        public void SetSource(ISource s) => src = s;

        // Root motion is applied by script: only the turn-in-place yaw is used (translation is code-driven by the motor).
        void OnAnimatorMove()
        {
            if (src == null || !anim) return;
            if (src.TurningInPlace) { float y = anim.deltaRotation.eulerAngles.y; if (y > 180f) y -= 360f; src.AddRootYaw(y); }
        }
        public void Kick(float amount) => recoilVel -= amount;
        public void PlayHit() => anim.SetTrigger(pHit);

        void Update()
        {
            if (src == null) return;
            float dt = Time.deltaTime;
            Transform root = transform.parent ? transform.parent : transform;
            Vector3 lv = root.InverseTransformDirection(src.Velocity); lv.y = 0;
            move.x = Springs.Step(move.x, ref moveVel.x, lv.x, 14f, dt);
            move.y = Springs.Step(move.y, ref moveVel.y, lv.z, 14f, dt);
            anim.SetFloat(pX, move.x); anim.SetFloat(pZ, move.y);
            anim.SetBool(pCrouch, src.Crouched);
            anim.SetBool(pGround, src.AnimGrounded);
            if (hasTurn)
            {
                anim.SetFloat(pTurn, src.TurnRate, 0.08f, dt);
                anim.SetBool(pTurning, src.TurningInPlace);
                anim.SetFloat(pVelY, src.VerticalSpeed);
                anim.SetFloat(pSpeed, new Vector2(lv.x, lv.z).magnitude);
                anim.SetFloat(pTurnMul, src.CombatStance || src.AimMode > 0 ? 2.2f : 1.4f);
            }
            anim.SetBool(pAim, src.AimMode > 0);
            anim.SetBool(pTag, src.TaggedOut);
            aimW = Springs.Step(aimW, ref aimWVel, src.Sprinting ? 0f : src.AimMode > 0 || src.CombatStance || FP ? 1f : 0f, 16f, dt);
            // the support hand lets go while sprinting AND while turning in place: the turn clip moves the arms and the
            // hand target used to drag the rifle toward the face (R2 fix)
            leftW = Springs.Step(leftW, ref leftWVel, src.Sprinting || (src.TurningInPlace && src.AimMode == 0) ? 0f : 1f, 14f, dt);
            alive = Mathf.MoveTowards(alive, src.TaggedOut ? 0f : 1f, dt * (src.TaggedOut ? 6f : 3f));
            recoil = Springs.Step(recoil, ref recoilVel, 0f, 24f, dt);
            adsBlend = Springs.Step(adsBlend, ref adsBlendVel, src.AimMode > 0 ? 1f : 0f, 18f, dt);
        }

        void LateUpdate()
        {
            if (src == null || !spine) return;
            float w = Mathf.Clamp01(Mathf.Max(aimW, reloadPose)) * alive;
            float lw = Mathf.Clamp01(Mathf.Max(Mathf.Max(w, leftW), leftOverrideW)) * alive;
            if (lw <= 0.001f || maxLayer <= 0) return;
            Transform root = transform.parent ? transform.parent : transform;
            // far characters are a few pixels tall: skip the procedural layer (the rifle stays parented to the animated hand)
            if (!mainCam) mainCam = Camera.main;
            if (mainCam && (mainCam.transform.position - root.position).sqrMagnitude > 35f * 35f) return;

            // 0) Close both hands around the rifle (finger muscles; the clips' hands are relaxed for a different gun)
            if (poseHandler != null && weaponInHand && alive > 0.5f && maxLayer >= 1)
            {
                if (!fingersCached)
                {
                    poseHandler.GetHumanPose(ref pose);
                    for (int k = 0; k < fingerMuscles.Length; k++) pose.muscles[fingerMuscles[k]] = fingerIsThumb[k] ? 0.2f : -0.55f;
                    poseHandler.SetHumanPose(ref pose);
                    for (int k = 0; k < fingerBones.Length; k++) fingerClosed[k] = fingerBones[k].localRotation;
                    fingersCached = true;
                }
                else for (int k = 0; k < fingerBones.Length; k++) fingerBones[k].localRotation = Quaternion.Slerp(fingerBones[k].localRotation, fingerClosed[k], alive);
            }

            // 1) Spine toward the aim, around world axes (split over the spine chain).
            Vector3 eye = root.position + Vector3.up * (src.Crouched ? 1.05f : 1.45f);
            Vector3 aimDir = src.AimPoint - eye;
            if (aimDir.sqrMagnitude > 0.25f && w > 0.001f && maxLayer >= 2)
            {
                aimDir.Normalize();
                float pitch = -Mathf.Asin(Mathf.Clamp(aimDir.y, -0.95f, 0.95f)) * Mathf.Rad2Deg * spinePitchShare;
                float yaw = Mathf.Clamp(Vector3.SignedAngle(root.forward, Vector3.ProjectOnPlane(aimDir, Vector3.up), Vector3.up), -maxSpineYaw, maxSpineYaw);
                var bones = spineBones;
                Quaternion step = Quaternion.AngleAxis(yaw * w / bones.Length, Vector3.up) * Quaternion.AngleAxis(pitch * w / bones.Length, root.right);
                foreach (var b in bones) if (b) b.rotation = step * b.rotation;
            }

            // 2) Rifle pose from the shoulder + aim point, right arm solved to hold it.
            if (weaponInHand && rUpper && rLower && rHand && muzzle && w > 0.001f && maxLayer >= 3)
            {
                Vector3 shoulder = rUpper.position;
                Vector3 toAim = src.AimPoint - shoulder;
                if (toAim.sqrMagnitude > 1f)
                {
                    Quaternion aimRot = Quaternion.LookRotation(toAim.normalized, Vector3.up);
                    // Weapon block: if the barrel would end inside geometry, raise the rifle to high-ready instead of clipping.
                    const float reach = 1.05f;   // shoulder to muzzle tip at full extension
                    float want = 0f;
                    if (Physics.SphereCast(shoulder - toAim.normalized * 0.1f, 0.07f, toAim.normalized, out var bh, reach, ~((1 << 8) | (1 << 2) | (1 << 9) | (1 << 11) | (1 << 12)), QueryTriggerInteraction.Ignore))
                        want = Mathf.Clamp01((reach - bh.distance) / 0.55f);
                    block = Springs.Step(block, ref blockVel, want, 18f, Time.deltaTime);
                    Vector3 gripLocal = Vector3.Lerp(Vector3.Lerp(hipGripOffset, adsGripOffset, adsBlend), reloadGripOffset, reloadPose);
                    gripLocal = Vector3.Lerp(gripLocal, new Vector3(-0.08f, 0.02f, 0.2f), block);
                    Vector3 grip = shoulder + aimRot * gripLocal;
                    grip += aimRot * Vector3.forward * (recoil * 0.05f);
                    if (FP && mainCam)
                    {   // first person: frame the rifle from the eye, not the shoulder (the arms reach it with the same IK)
                        var ct = mainCam.transform;
                        Vector3 fp = Vector3.Lerp(fpHipGrip, fpAdsGrip, adsBlend) + new Vector3(-0.11f, -0.03f, -0.03f) * reloadPose + new Vector3(0f, -0.06f, -0.12f) * block;
                        grip = ct.position + ct.rotation * fp + ct.forward * (recoil * 0.035f);
                    }
                    // barrel from the muzzle's offset in weapon space toward the aim point
                    Vector3 muzzleLocal = Quaternion.Inverse(weapon.rotation) * (muzzle.position - weapon.position);
                    Quaternion wRot = Quaternion.LookRotation((src.AimPoint - (grip + aimRot * muzzleLocal)).normalized, Vector3.up);
                    if (reloadPose > 0f) wRot = wRot * Quaternion.Euler(12f * reloadPose, 0f, -38f * reloadPose);   // nose down, cant the well toward the support hand
                    if (block > 0.01f) wRot = Quaternion.AngleAxis(-60f * block, root.right) * wRot;                       // muzzle up, out of the wall
                    // the hand pose that puts the (hand-parented) rifle exactly there
                    Quaternion handRot = wRot * Quaternion.Inverse(weaponLocalRot);
                    Vector3 handPos = grip - handRot * Vector3.Scale(weaponLocalPos, rHand.lossyScale);
                    Vector3 animHand = rHand.position; Quaternion animRot = rHand.rotation;
                    Vector3 target = Vector3.Lerp(animHand, handPos, w);
                    Vector3 hint = shoulder + root.right * 0.25f - Vector3.up * 0.35f - root.forward * 0.1f;
                    SolveTwoBone(rUpper, rLower, rHand, target, hint);
                    rHand.rotation = Quaternion.Slerp(animRot, handRot, w);
                }
            }

            // 3) Support hand under the handguard: fingers wrap toward the far side, thumb along the barrel;
            //    the PALM (not the wrist) is placed on the handguard, then the arm is solved to reach it.
            if (leftGrip && lUpper && lLower && lHand && maxLayer >= 4)
            {
                Quaternion want = leftGrip.rotation; Vector3 palmOff = Vector3.zero;
                Vector3 fore = leftGrip.position;
                if (weapon && lIndex && lPinky && lMiddle)
                {
                    Vector3 fCur = (lMiddle.position - lHand.position).normalized, sCur = (lIndex.position - lPinky.position).normalized;
                    Vector3 fDes = (weapon.right * 0.8f + weapon.up * 0.35f + weapon.forward * 0.2f).normalized, sDes = weapon.forward;
                    Quaternion fix = Quaternion.LookRotation(fDes, Vector3.Cross(fDes, sDes)) * Quaternion.Inverse(Quaternion.LookRotation(fCur, Vector3.Cross(fCur, sCur)));
                    want = fix * lHand.rotation;
                    palmOff = fix * ((lMiddle.position - lHand.position) * 0.5f);
                    fore = weapon.TransformPoint(handguardPoint);
                }
                Vector3 goal = Vector3.Lerp(fore, leftOverridePos, leftOverrideW) - palmOff;
                // never let the support hand path cut through the torso: keep it in front of the chest plane above the waist
                Transform torso = upperChest ? upperChest : chest;
                if (torso && leftOverrideW > 0.01f)
                {
                    float waist = spine ? spine.position.y : root.position.y + 1f;
                    float ahead = Vector3.Dot(goal - torso.position, root.forward);
                    if (goal.y > waist && ahead < 0.24f) goal += root.forward * (0.24f - ahead) * Mathf.InverseLerp(waist, waist + 0.15f, goal.y);
                }
                Vector3 target = Vector3.Lerp(lHand.position, goal, lw);
                Vector3 lHint = lUpper.position - root.right * 0.1f - Vector3.up * 0.4f;
                Vector3 reloadHint = lUpper.position - root.right * 0.45f - Vector3.up * 0.3f + root.forward * 0.05f;   // elbow out to the side
                lHint = Vector3.Lerp(lHint, reloadHint, leftOverrideW);
                Quaternion animRot = lHand.rotation;
                SolveTwoBone(lUpper, lLower, lHand, target, Vector3.Lerp(lLower.position, lHint, lw));
                lHand.rotation = Quaternion.Slerp(animRot, want, lw * (1f - 0.5f * leftOverrideW));
            }
        }

        static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
        {
            float ab = Vector3.Distance(a.position, b.position), bc = Vector3.Distance(b.position, c.position);
            Vector3 at = target - a.position;
            float d = Mathf.Clamp(at.magnitude, 0.01f, ab + bc - 0.001f);
            Vector3 dir = at.normalized;
            Vector3 toHint = hint - a.position;
            Vector3 bendDir = Vector3.ProjectOnPlane(toHint, dir);
            if (bendDir.sqrMagnitude < 1e-6f) bendDir = Vector3.ProjectOnPlane(Vector3.down, dir);
            bendDir.Normalize();
            float cosA = Mathf.Clamp((ab * ab + d * d - bc * bc) / (2f * ab * d), -1f, 1f);
            Vector3 elbow = a.position + dir * (cosA * ab) + bendDir * (Mathf.Sqrt(1f - cosA * cosA) * ab);
            a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, target - b.position) * b.rotation;
        }
    }
}
