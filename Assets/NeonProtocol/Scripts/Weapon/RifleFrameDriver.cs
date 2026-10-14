using UnityEngine;

namespace NeonProtocol
{
    // Input stays in PaintRifle.Update. Resolve only after this frame's camera/weapon pose.
    [DefaultExecutionOrder(1100)]
    [DisallowMultipleComponent]
    public sealed class RifleFrameDriver : MonoBehaviour
    {
        public PaintRifle rifle;
        void LateUpdate() { if (rifle && rifle.isActiveAndEnabled) rifle.ResolvePendingFire(); }
    }
}
