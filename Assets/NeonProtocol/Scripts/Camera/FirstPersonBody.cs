using UnityEngine;

namespace NeonProtocol
{
    // True first person with the player's OWN rig: the camera sits at CRYPTO's eyes, only the head is hidden, and the
    // real animated arms hold the real rifle (aim IK, support hand, the existing reload with its canister). The body
    // faces exactly where the camera looks (PlayerMotor.firstPersonBody). Runs after ShoulderRig and before CharacterAnimator.s IK
    // (order 100) and ShoulderRig, before RifleFrameDriver resolves shots (1100).
    [DefaultExecutionOrder(50)]   // after ShoulderRig (-50), BEFORE CharacterAnimator IK (100): the rifle is framed from the final eye
    public class FirstPersonBody : MonoBehaviour
    {
        public static bool Active { get; private set; }
        public static Animator OwnerAnim { get; private set; }     // only the player's animator uses the first-person poses
        public ShoulderRig rig;
        public PlayerMotor motor;
        public Vector3 eyeOffset = new Vector3(0f, 0.07f, 0.11f);   // from the head bone: forward of the face, at the eyes
        public float nearClip = 0.04f;
        // Release default uses the stable capsule camera and existing weapon viewmodel.
        // Full-body animation remains available for further rig work, without driving the player camera.
        public bool fullBodyPreview = false;

        Transform head; Animator anim; float eyeY; bool hadEye; float baseNear = -1f;

        void Start()
        {
            if (!rig) rig = FindFirstObjectByType<ShoulderRig>();
            if (!motor) motor = FindFirstObjectByType<PlayerMotor>();
            anim = motor ? motor.GetComponentInChildren<Animator>() : null;
            head = anim ? anim.GetBoneTransform(HumanBodyBones.Head) : null; OwnerAnim = anim;
        }

        void LateUpdate()
        {
            bool want = fullBodyPreview && rig && rig.enabled && rig.firstPersonTrial && motor && motor.isActiveAndEnabled && head && !motor.TaggedOut;
            Active = want;
            if (motor) motor.firstPersonBody = want;
            if (!want)
            {
                if (head && head.localScale.x < 0.5f) head.localScale = Vector3.one;
                if (rig && rig.cam && baseNear > 0f) { rig.cam.nearClipPlane = baseNear; baseNear = -1f; }
                hadEye = false; return;
            }
            var cam = rig.cam;
            if (baseNear < 0f) baseNear = cam.nearClipPlane;
            cam.nearClipPlane = nearClip;
            // eye = head bone (animated) + offset in the BODY frame; vertical motion lightly smoothed (no bob, no roll)
            Vector3 eye = head.position + motor.transform.rotation * eyeOffset;
            if (!hadEye) { eyeY = eye.y; hadEye = true; }
            eyeY = Springs.Damp(eyeY, eye.y, 18f, Mathf.Max(Time.deltaTime, 1e-4f));
            eye.y = eyeY;
            rig.transform.position = eye; cam.transform.position = eye;
            head.localScale = Vector3.one * 0.001f;   // hide the head (mask/hair) from the inside view only
        }
    }
}
