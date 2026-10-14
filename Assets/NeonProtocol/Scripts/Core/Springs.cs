using UnityEngine;

namespace NeonProtocol
{
    // Critically damped springs with exact integration: frame-rate independent, never overshoot.
    // omega ~ responsiveness in rad/s (settles in roughly 4/omega seconds).
    public static class Springs
    {
        public static float Step(float x, ref float v, float target, float omega, float dt)
        {
            float d = x - target, e = Mathf.Exp(-omega * dt), k = (v + omega * d) * dt;
            v = (v - omega * k) * e;
            return target + (d + k) * e;
        }

        public static Vector3 Step(Vector3 x, ref Vector3 v, Vector3 target, float omega, float dt)
        {
            Vector3 d = x - target; float e = Mathf.Exp(-omega * dt); Vector3 k = (v + omega * d) * dt;
            v = (v - omega * k) * e;
            return target + (d + k) * e;
        }

        public static float StepAngle(float x, ref float v, float target, float omega, float dt)
        {
            float t = x + Mathf.DeltaAngle(x, target);
            return Step(x, ref v, t, omega, dt);
        }

        // Exponential smoothing toward a target (no velocity state).
        public static float Damp(float x, float target, float lambda, float dt) => Mathf.Lerp(x, target, 1f - Mathf.Exp(-lambda * dt));
        public static Vector3 Damp(Vector3 x, Vector3 target, float lambda, float dt) => Vector3.Lerp(x, target, 1f - Mathf.Exp(-lambda * dt));
    }
}
