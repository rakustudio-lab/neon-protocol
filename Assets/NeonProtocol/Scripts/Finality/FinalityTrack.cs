using UnityEngine;

namespace NeonProtocol
{
    // The Finality Run track: a baked centre line (1 m samples) with frames and widths.
    // Gameplay runs in track space (s = distance, x = lateral offset), which keeps the bike controllable,
    // deterministic and cheap: no wheel physics, no tunnelling through walls at 60 m/s.
    public class FinalityTrack : MonoBehaviour
    {
        [HideInInspector] public Vector3[] pos, fwd, right, up;
        [HideInInspector] public float[] width;
        public float step = 1f;
        [Tooltip("Lane-choice divider in track space (audited from the stage mesh): the bike may not cross it.")]
        public float dividerS0 = 600f, dividerS1 = 830f, dividerHalf = 0.5f;
        public float Length => pos == null ? 0f : (pos.Length - 1) * step;

        public void Sample(float s, out Vector3 p, out Vector3 f, out Vector3 r, out Vector3 u, out float w)
        {
            float t = Mathf.Clamp(s / step, 0f, pos.Length - 1.001f);
            int i = (int)t; float k = t - i;
            p = Vector3.Lerp(pos[i], pos[i + 1], k);
            f = Vector3.Slerp(fwd[i], fwd[i + 1], k).normalized;
            u = Vector3.Slerp(up[i], up[i + 1], k).normalized;
            r = Vector3.Cross(u, f).normalized;
            w = Mathf.Lerp(width[i], width[i + 1], k);
        }

        public Vector3 World(float s, float x, float h = 0f)
        {
            Sample(s, out var p, out _, out var r, out var u, out _);
            return p + r * x + u * h;
        }

        public float HalfWidth(float s) { Sample(s, out _, out _, out _, out _, out var w); return w * 0.5f; }

        // Centre-line curvature (deg per metre, signed: + = turning right) used for banking and the HUD.
        public float Turn(float s)
        {
            Sample(s, out _, out var f0, out _, out var u, out _);
            Sample(s + 6f, out _, out var f1, out _, out _, out _);
            return Vector3.SignedAngle(f0, f1, u) / 6f;
        }
    }
}
