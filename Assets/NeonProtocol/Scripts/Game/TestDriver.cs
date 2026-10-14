using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NeonProtocol
{
    // Repeatable scripted input for QA (editor play mode and web builds via ?test=...).
    // Each step holds an input state for a duration; optional absolute aim.
    public class TestDriver : MonoBehaviour
    {
        public struct Step { public float duration; public NPInput.State input; public float? yaw, pitch; public string mark; }

        public static TestDriver Instance { get; private set; }
        public static readonly List<string> Log = new List<string>();
        public bool Running { get; private set; }

        public static TestDriver Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("_TestDriver");
            Instance = go.AddComponent<TestDriver>();
            return Instance;
        }

        public void Run(IEnumerable<Step> steps) { StopAllCoroutines(); StartCoroutine(Play(new List<Step>(steps))); }

        IEnumerator Play(List<Step> steps)
        {
            Running = true;
            NPInput.UseOverride = true;
            var rig = FindFirstObjectByType<ShoulderRig>();
            foreach (var s in steps)
            {
                if (!string.IsNullOrEmpty(s.mark)) Log.Add($"{Time.time:F2} {s.mark}");
                if (rig && (s.yaw.HasValue || s.pitch.HasValue)) rig.SetAim(s.yaw ?? rig.Yaw, s.pitch ?? rig.Pitch);
                float t = 0f; bool first = true;
                while (t < s.duration)
                {
                    var st = s.input;
                    if (!first) { st.crouchPressed = st.jumpPressed = st.reloadPressed = st.zoomPressed = st.shoulderPressed = st.interactPressed = false; }
                    NPInput.Override = st;
                    first = false;
                    yield return null;
                    t += Time.deltaTime;
                }
            }
            NPInput.Override = default;
            Running = false;
        }

        // Full-loop QA: relays -> core -> extraction. Teleports between objectives (tests systems, not routing).
        public void RunHeistSmoke() { StopAllCoroutines(); StartCoroutine(HeistSmoke()); }
        public static string SmokeResult = "";

        IEnumerator HeistSmoke()
        {
            SmokeResult = "running";
            var d = HeistDirector.Instance; var rig = FindFirstObjectByType<ShoulderRig>();
            NPInput.UseOverride = true;
            // Deterministic: the systems test runs without hostile AI (AI is covered by its own checks).
            foreach (var ai in CompetitorAIRegistry.All.ToArray()) { ai.GetComponent<UnityEngine.AI.NavMeshAgent>().isStopped = true; ai.enabled = false; }
            foreach (var dr in CompetitorAIRegistry.Drones.ToArray()) dr.enabled = false;
            d.BeginRun();
            yield return new WaitForSeconds(0.5f);
            var spots = new[] { new Vector3(18.5f, 4.55f, -8f), new Vector3(-13.6f, -3.45f, 0.5f), new Vector3(-7.5f, 0.05f, 17.5f) };   // vault: shoot the lock rings from the floor (balcony rail blocks low shots)
            for (int i = 0; i < d.relays.Length; i++)
            {
                var r = d.relays[i];
                d.player.Teleport(spots[i], 0f);
                yield return null;
                Vector3 to = r.transform.position - (d.player.transform.position + Vector3.up * 1.6f);
                float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                float t = 0;
                var lockRings = r.acceptsPaint ? null : FindFirstObjectByType<VaultRings>();
                if (lockRings)
                {
                    // vault lock: track each moving ring node with lead until it locks
                    foreach (var ring in lockRings.rings)
                    {
                        float tn = 0;
                        while (ring.node && !ring.node.Locked && tn < 8f)
                        {
                            Vector3 np = ring.node.transform.position;
                            np += ring.node.Velocity * ((np - rig.cam.transform.position).magnitude / 78f);
                            Vector3 fc = np - rig.cam.transform.position;
                            rig.SetAim(Mathf.Atan2(fc.x, fc.z) * Mathf.Rad2Deg, -Mathf.Atan2(fc.y, new Vector2(fc.x, fc.z).magnitude) * Mathf.Rad2Deg);
                            NPInput.Override = new NPInput.State { aim = true, fire = true };
                            tn += Time.deltaTime; t += Time.deltaTime; yield return null;
                        }
                        NPInput.Override = default;
                        Log.Add($"node {ring.node?.name} locked={ring.node?.Locked} {tn:F1}s");
                    }
                }
                while (!r.Online && t < 6f && !lockRings)
                {
                    // aim from the real lens position (camera sits off the right shoulder)
                    Vector3 fromCam = r.transform.position - rig.cam.transform.position;
                    yaw = Mathf.Atan2(fromCam.x, fromCam.z) * Mathf.Rad2Deg; pitch = -Mathf.Atan2(fromCam.y, new Vector2(fromCam.x, fromCam.z).magnitude) * Mathf.Rad2Deg;
                    rig.SetAim(yaw, pitch);
                    NPInput.Override = new NPInput.State { aim = true, fire = true };
                    t += Time.deltaTime; yield return null;
                }
                NPInput.Override = default;
                Log.Add($"relay {r.name} online={r.Online} after {t:F1}s");
                yield return new WaitForSeconds(0.3f);
            }
            Log.Add($"phase after relays: {d.Current}");
            yield return new WaitForSeconds(2.5f);   // cage lowers
            d.player.Teleport(d.core.coreVisual.position + Vector3.back * 0.6f + Vector3.down * 1.4f, 0f);
            float w = 0; while (!d.core.Carried && w < 3f) { w += Time.deltaTime; yield return null; }
            Log.Add($"core carried={d.core.Carried} phase={d.Current}");
            yield return new WaitForSeconds(2.6f);   // gate opens
            d.player.Teleport(d.gate.transform.position + new Vector3(0, 0.1f, 2.5f), 180f);
            yield return null;
            d.player.Teleport(d.gate.extractTrigger.transform.position + Vector3.down * 1.4f, 180f);
            w = 0; while (d.Current != HeistDirector.Phase.Extracted && d.Current != HeistDirector.Phase.FinalityRun && w < 3f) { w += Time.deltaTime; yield return null; }
            if (d.Current == HeistDirector.Phase.FinalityRun && FinalityRun.Instance)   // ACT II: ride it with the QA driver
            {
                Log.Add("finality run started");
                var ap = FinalityRun.Instance.gameObject.AddComponent<BikeAutopilot>();
                w = 0; while (d.Current == HeistDirector.Phase.FinalityRun && w < 60f) { w += Time.deltaTime; yield return null; }
                Destroy(ap);
                Log.Add($"finality: shards {FinalityRun.Instance.Shards}/{FinalityRun.Instance.ShardsTotal} crashes {FinalityRun.Instance.Crashes}");
            }
            var sig = d.LastSignature;
            SmokeResult = $"{RuntimeErrorMonitor.Summary()} | phase={d.Current} sigBytes={(sig != null ? sig.Length : 0)} raw={d.Recorder.RawCount} time={(d.Elapsed + d.Penalty):F1}s | " + string.Join(" ; ", Log);
            NPInput.UseOverride = false;
        }

        public void StopTest() { StopAllCoroutines(); NPInput.Override = default; NPInput.UseOverride = false; Running = false; }

        // Built-in sequences
        public static IEnumerable<Step> FireAt(float yaw, float pitch, float seconds, bool aim = true)
        {
            yield return new Step { duration = 0.4f, yaw = yaw, pitch = pitch, input = new NPInput.State { aim = aim }, mark = "aim" };
            yield return new Step { duration = seconds, input = new NPInput.State { aim = aim, fire = true }, mark = "fire" };
            yield return new Step { duration = 0.6f, input = new NPInput.State { aim = aim }, mark = "hold" };
        }

        public static IEnumerable<Step> Walk(Vector2 move, float seconds, bool sprint = false)
        {
            yield return new Step { duration = seconds, input = new NPInput.State { move = move, sprint = sprint }, mark = "walk" };
        }
    }
}
