using UnityEngine;

namespace NeonProtocol
{
    // QA / demo driver for the Finality Run: feeds NPInput like a player would (steer, boost, fire, dodge).
    // Prefers the right lane at the split (shutters + switches) to exercise the paint mechanic. Not used in normal play.
    public class BikeAutopilot : MonoBehaviour
    {
        public bool preferRight = true;
        float dodgeCd;

        void OnDisable() { NPInput.UseOverride = false; }

        void Update()
        {
            var run = FinalityRun.Instance; if (!run || !run.Running || !run.bike) return;
            var b = run.bike; var st = new NPInput.State();
            float target = 0f, fireFor = 0f;
            bool targetSet = false;
            foreach (var e in run.Elements)
            {
                float d = e.s - b.S; if (d < 0f || d > 80f) continue;
                if (e.kind == TrackElement.Kind.Shard && !e.done && !targetSet && d < 70f) { target = (e.x0 + e.x1) * 0.5f; targetSet = true; }
                if (e.kind == TrackElement.Kind.Switch && !e.done && d < 75f && d > 8f) fireFor = 1f;
            }
            if (!targetSet && b.S > 560f && b.S < 840f) target = preferRight ? 4.5f : -4.5f;   // lane choice on the bridge
            // avoid blockers in the next 35 m
            foreach (var e in run.Elements)
            {
                float d = e.s - b.S; if (d < 0f || d > 35f || !e.Blocks) continue;
                if (target + 0.9f > e.x0 && target - 0.9f < e.x1)
                {
                    float half = b.track.HalfWidth(e.s) - 1.2f;
                    float left = e.x0 - 1.4f, right = e.x1 + 1.4f;
                    target = (Mathf.Abs(target - left) < Mathf.Abs(target - right) && left > -half) || right > half ? left : right;
                }
            }
            // drone telegraph: the red line on the road shows the sweep lane -> steer out of it (as a player would)
            if (run.Drones != null) foreach (var dr in run.Drones)
                if (dr && dr.Warning && Mathf.Abs(target - dr.LaneX) < 2.4f)
                {
                    float half = b.track.HalfWidth(b.S) - 1.2f, l = dr.LaneX - 2.8f, r = dr.LaneX + 2.8f;
                    target = (Mathf.Abs(b.X - l) < Mathf.Abs(b.X - r) && l > -half) || r > half ? l : r;
                }
            st.move = new Vector2(Mathf.Clamp((target - b.X) * 0.45f, -1f, 1f), 0f);
            st.sprint = Mathf.Abs(b.track.Turn(b.S + 20f)) < 0.2f && b.Boost > 0.3f;
            st.fire = fireFor > 0f || (b.S > 990f && Time.time % 1.6f < 0.6f);   // spray at drones in the chase
            dodgeCd -= Time.deltaTime;
            NPInput.Override = st; NPInput.UseOverride = true;
        }
    }
}
