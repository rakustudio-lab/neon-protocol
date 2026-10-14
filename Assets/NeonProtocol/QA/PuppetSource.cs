using UnityEngine;

namespace NeonProtocol
{
    // QA puppet for the rig diagnosis: drives a CharacterAnimator with scripted values (no motor, no physics, in place).
    public class PuppetSource : MonoBehaviour, CharacterAnimator.ISource
    {
        public Vector3 velocityLocal; public bool crouched, sprinting, combat; public int aimMode; public float turnRate; public bool turning;
        public Vector3 aimOffset = new Vector3(0f, 1.4f, 10f);
        public Vector3 Velocity => transform.TransformDirection(velocityLocal);
        public bool Grounded => true;
        public bool Crouched => crouched;
        public bool Sprinting => sprinting;
        public int AimMode => aimMode;
        public bool CombatStance => combat;
        public Vector3 AimPoint => transform.TransformPoint(aimOffset);
        public bool TaggedOut => false;
        public float TurnRate => turnRate;
        public bool TurningInPlace => turning;
        public void AddRootYaw(float d) { transform.Rotate(0f, d, 0f, Space.World); }
    }

    // Copies the driven Animator parameters from one puppet to the control rig every frame (same clips, same values).
    [DefaultExecutionOrder(150)]
    public class AnimatorParamMirror : MonoBehaviour
    {
        public Animator from, to;
        void LateUpdate()
        {
            if (!from || !to) return;
            foreach (var p in from.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Float) to.SetFloat(p.nameHash, from.GetFloat(p.nameHash));
                else if (p.type == AnimatorControllerParameterType.Bool) to.SetBool(p.nameHash, from.GetBool(p.nameHash));
            }
        }
    }
}
