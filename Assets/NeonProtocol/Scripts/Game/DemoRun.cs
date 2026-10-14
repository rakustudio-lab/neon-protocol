using System.Collections;
using System.Linq;
using UnityEngine;

namespace NeonProtocol
{
    // A full heist played through the real controls by PlayerAutopilot (walks the actual routes, aims, paints).
    // Doubles as end-to-end QA (routing + objectives) and as the take for demo footage.
    public class DemoRun : MonoBehaviour
    {
        public static string Status = "";
        public static bool Running;
        public bool aiAggression = false;   // for recording: rivals still patrol and react, but don't overwhelm the take

        public static DemoRun Begin(bool peacefulRivals = true)
        {
            var go = new GameObject("_DemoRun"); var d = go.AddComponent<DemoRun>();
            d.aiAggression = !peacefulRivals;
            d.StartCoroutine(d.Play());
            return d;
        }

        IEnumerator Play()
        {
            Running = true; Status = "start";
            NPInput.UseOverride = true;   // claim input first: otherwise the "menu open" pause stops time
            var ap = PlayerAutopilot.Ensure();
            var h = HeistDirector.Instance;
            if (!aiAggression) foreach (var ai in CompetitorAIRegistry.All) { ai.fireRate = 1.2f; ai.aimErrorDeg = 6f; }
            h.BeginRun();
            yield return new WaitForSeconds(0.6f);
            var relays = h.relays;
            Relay A = relays.First(r => r.name.EndsWith("A")), B = relays.First(r => r.name.EndsWith("B")), C = relays.First(r => r.name.EndsWith("C"));

            // ARRIVAL: take in the view, then down into the hall painting the reactor as we pass
            yield return ap.Run(ap.Look(0f, 2f, 1.2f));
            yield return ap.Run(ap.GoTo(new Vector3(0, 0, -16f), sprint: true));
            yield return ap.Run(ap.AimFire(() => new Vector3(-1.5f, 2.2f, -7f), 1.2f));
            Status = "hall";
            // tag the closest visible rival if one is near
            var near = CompetitorAIRegistry.All.Where(r => r && !r.TaggedOut).OrderBy(r => (r.transform.position - h.player.transform.position).sqrMagnitude).FirstOrDefault();
            if (near && Vector3.Distance(near.transform.position, h.player.transform.position) < 22f)
                yield return ap.Run(ap.AimFire(() => near.transform.position + Vector3.up * 1.2f, 1.6f));

            // RELAY A: east stairs to the catwalk
            yield return ap.Run(ap.GoTo(new Vector3(19.5f, 0, -22.5f), sprint: true));
            yield return ap.Run(ap.GoTo(new Vector3(19.5f, 4.5f, -8f)));
            float t0 = Time.time;
            while (!A.Online && Time.time - t0 < 6f) yield return ap.Run(ap.AimFire(() => A.transform.position, 0.8f));
            Status = $"relayA={A.Online}";

            // RELAY B: across the bridge, down the west stairs, into the trench (stealth: crouch)
            yield return ap.Run(ap.GoTo(new Vector3(-20.5f, 4.5f, 6f), sprint: true));
            yield return ap.Run(ap.GoTo(new Vector3(-20.5f, 0, -23.5f)));
            yield return ap.Run(ap.GoTo(new Vector3(-14f, 0, -23.2f)));
            yield return ap.Run(ap.GoTo(new Vector3(-14f, -3.5f, 3f), crouch: true));
            t0 = Time.time;
            while (!B.Online && Time.time - t0 < 6f) yield return ap.Run(ap.AimFire(() => B.transform.position, 0.8f));
            yield return ap.Run(ap.Crouch(false));
            Status += $" relayB={B.Online}";

            // RELAY C: trench north into the vault, up to the balcony
            yield return ap.Run(ap.GoTo(new Vector3(-13.8f, 0, 22f), sprint: true));
            yield return ap.Run(ap.GoTo(new Vector3(12.5f, 0, 18f), sprint: true));
            yield return ap.Run(ap.GoTo(new Vector3(6f, 5f, 31f)));
            t0 = Time.time;
            while (!C.Online && Time.time - t0 < 6f) yield return ap.Run(ap.AimFire(() => C.transform.position, 0.8f));
            Status += $" relayC={C.Online}";

            // CORE
            yield return ap.Run(ap.Look(180f, 20f, 1.0f));
            yield return ap.Run(ap.GoTo(new Vector3(12.5f, 0, 18.5f)));
            yield return ap.Run(ap.GoTo(new Vector3(0f, 0f, 20.4f), sprint: true));
            float w = 0; while (!h.core.Carried && w < 4f) { w += Time.deltaTime; yield return null; }
            Status += $" core={h.core.Carried}";

            // PURSUIT: east door -> corridor -> gate (re-take the Core if a tag-out drops it)
            var legs = new[] { new Vector3(20f, 0, 16f), new Vector3(27f, 0, 2f), new Vector3(27f, 0, -14f) };
            for (int attempt = 0; attempt < 4 && h.Current != HeistDirector.Phase.Extracted; attempt++)
            {
                if (!h.core.Carried)
                {
                    while (h.player.TaggedOut) yield return null;
                    yield return ap.Run(ap.GoTo(h.core.coreVisual.position, sprint: true));
                    w = 0; while (!h.core.Carried && w < 3f) { w += Time.deltaTime; yield return null; }
                    if (!h.core.Carried) continue;
                }
                foreach (var leg in legs)
                {
                    if (!h.core.Carried) break;
                    yield return ap.Run(ap.GoTo(leg, sprint: true));
                }
                w = 0; while (h.Current != HeistDirector.Phase.Extracted && w < 2f) { w += Time.deltaTime; yield return null; }
            }
            Status += $" {RuntimeErrorMonitor.Summary()} phase={h.Current} time={(h.Elapsed + h.Penalty):F1}s tags={h.PlayerTags}";
            Running = false;
        }
    }
}
