using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace NeonProtocol
{
    // Drives the real player controller through NPInput (same code path as a human): follows NavMesh paths,
    // turns the camera smoothly, aims and fires at targets. Used for QA runs and for recording demo footage.
    public class PlayerAutopilot : MonoBehaviour
    {
        public static PlayerAutopilot Instance { get; private set; }
        public float turnSpeed = 220f;   // deg/s camera yaw while walking
        public float aimSpeed = 160f;
        public bool fightBack = true;

        CompetitorAI Threat()
        {
            CompetitorAI best = null; float bd = 22f;
            foreach (var ai in CompetitorAIRegistry.All)
            {
                if (!ai || ai.TaggedOut || !ai.EngagingPlayer) continue;
                Vector3 head = ai.transform.position + Vector3.up * 1.3f;
                float d = Vector3.Distance(head, rig.transform.position);
                if (d > bd) continue;
                if (Physics.Linecast(rig.transform.position + rig.transform.forward * rig.BoomLength, head, out var hit, ~((1 << 8) | (1 << 11) | (1 << 12)), QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(ai.transform)) continue;
                best = ai; bd = d;
            }
            return best;
        }

        ShoulderRig rig; PlayerMotor motor;
        NPInput.State st;
        public bool Busy { get; private set; }

        public static PlayerAutopilot Ensure()
        {
            if (Instance) return Instance;
            Instance = new GameObject("_Autopilot").AddComponent<PlayerAutopilot>();
            return Instance;
        }

        void Awake() { rig = FindFirstObjectByType<ShoulderRig>(); motor = FindFirstObjectByType<PlayerMotor>(); }

        void LateUpdate() { if (Busy) { NPInput.UseOverride = true; NPInput.Override = st; st.crouchPressed = st.jumpPressed = st.reloadPressed = st.zoomPressed = false; } }

        public Coroutine Run(IEnumerator script) => StartCoroutine(Wrap(script));
        public void Cancel() { StopAllCoroutines(); st = default; Busy = false; NPInput.Override = default; }   // QA: stop a walk when the phase changes
        IEnumerator Wrap(IEnumerator s) { Busy = true; yield return s; st = default; Busy = false; NPInput.Override = default; }

        float YawTo(Vector3 p) { Vector3 d = p - motor.transform.position; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

        /// Walk (or sprint) along the NavMesh to a point.
        public IEnumerator GoTo(Vector3 target, bool sprint = false, bool crouch = false, float timeout = 25f, float lookPitch = 6f)
        {
            if (crouch != motor.Crouched) { st.crouchPressed = true; yield return null; }
            var path = new NavMeshPath();
            NavMesh.CalculatePath(motor.transform.position, target, NavMesh.AllAreas, path);
            var corners = path.corners.Length > 0 ? path.corners : new[] { target };
            int i = 0; float t = 0, scan = 0, replan = 1.5f;
            while (i < corners.Length && t < timeout)
            {
                // re-plan regularly: shutters/doors carve the NavMesh only while closed (a plan made during a lockdown
                // would otherwise send the runner the long way round forever)
                replan -= Time.deltaTime;
                if (replan <= 0f) { replan = 1.5f; if (NavMesh.CalculatePath(motor.transform.position, target, NavMesh.AllAreas, path) && path.corners.Length > 1) { corners = path.corners; i = 1; } }
                // fight back like a player would: paint out a rival that is engaging us
                scan -= Time.deltaTime;
                if (fightBack && scan <= 0f)
                {
                    scan = 0.3f;
                    var threat = Threat();
                    if (threat)
                    {
                        float ft = 0;
                        while (threat && !threat.TaggedOut && ft < 3.5f && !motor.TaggedOut)
                        {
                            Vector3 p = threat.transform.position + Vector3.up * 1.2f, d2 = p - rig.transform.position;
                            float y = Mathf.Atan2(d2.x, d2.z) * Mathf.Rad2Deg, pt = -Mathf.Atan2(d2.y, new Vector2(d2.x, d2.z).magnitude) * Mathf.Rad2Deg;
                            rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, y, aimSpeed * 1.5f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, pt, aimSpeed * Time.deltaTime));
                            st.move = Vector2.zero; st.aim = true; st.fire = Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, y)) < 3f;
                            ft += Time.deltaTime; yield return null;
                        }
                        st.aim = st.fire = false;
                        // path may have changed after a tag-out respawn
                        if (motor.TaggedOut) { while (motor.TaggedOut) yield return null; NavMesh.CalculatePath(motor.transform.position, target, NavMesh.AllAreas, path); corners = path.corners.Length > 0 ? path.corners : new[] { target }; i = 0; }
                    }
                }
                Vector3 c = corners[i]; Vector3 d = c - motor.transform.position; d.y = 0;
                if (d.magnitude < (i == corners.Length - 1 ? 0.4f : 0.8f)) { i++; continue; }
                float want = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float yaw = Mathf.MoveTowardsAngle(rig.Yaw, want, turnSpeed * Time.deltaTime);
                rig.SetAim(yaw, Mathf.MoveTowards(rig.Pitch, lookPitch, 30f * Time.deltaTime));
                float off = Mathf.DeltaAngle(yaw, want);
                Vector3 local = Quaternion.Euler(0, -yaw, 0) * d.normalized;   // move relative to camera
                st.move = new Vector2(local.x, local.z) * (Mathf.Abs(off) > 70f ? 0.4f : 1f);
                st.sprint = sprint && Mathf.Abs(off) < 25f;
                st.aim = false; st.fire = false;
                t += Time.deltaTime; yield return null;
            }
            st.move = Vector2.zero; st.sprint = false;
        }

        /// Turn to a target, aim, and fire for a duration (re-aims every frame, so moving targets are tracked).
        public IEnumerator AimFire(System.Func<Vector3> target, float seconds, bool ads = true, float settle = 0.35f)
        {
            float t = 0; st.move = Vector2.zero;
            while (t < seconds + settle)
            {
                Vector3 p = target(); Vector3 eye = rig.transform.position;
                Vector3 d = p - eye;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, yaw, aimSpeed * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, pitch, aimSpeed * Time.deltaTime));
                st.aim = ads; st.fire = t > settle && Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, yaw)) < 2.5f;
                t += Time.deltaTime; yield return null;
            }
            st.fire = false; st.aim = false;
        }

        public IEnumerator Look(float yaw, float pitch, float seconds)
        {
            float t = 0; st.move = Vector2.zero;
            while (t < seconds) { rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, yaw, 90f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, pitch, 40f * Time.deltaTime)); t += Time.deltaTime; yield return null; }
        }

        public IEnumerator Wait(float s) { st.move = Vector2.zero; st.fire = false; yield return new WaitForSeconds(s); }
        public IEnumerator Crouch(bool on) { if (on != motor.Crouched) { st.crouchPressed = true; yield return null; } }
    }
}
