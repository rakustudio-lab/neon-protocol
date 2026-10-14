using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;

namespace NeonProtocol
{
    // Feeds the HTML HUD (web template): compact state at 15 Hz, crosshair every frame. No Unity UI in the web build.
    public class WebHud : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void NP_Crosshair(float gapPx, int aimMode, float hitmark, int visible);
#else
        static void NP_Crosshair(float gapPx, int aimMode, float hitmark, int visible) { }
#endif
        public PaintRifle rifle;
        public PlayerTagController tags;
        float sendT, hitmark;
        VaultRings vault; IrisRelay iris; RoomExit roomExit;
        string lastAnnounce = "";

        void Start()
        {
            if (PaintProjectiles.Instance) PaintProjectiles.Instance.Impact += (h, counted) => { if (counted && h.team == 0) hitmark = 1f; };
            if (HeistDirector.Instance) HeistDirector.Instance.Announce += a => lastAnnounce = a;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            hitmark = Mathf.Max(0f, hitmark - Time.deltaTime * 5f);
            if (rifle && cam)
            {
                float cy = Screen.height * 0.5f;
                float gap = 4f + Mathf.Tan(rifle.CurrentSpread * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cy;
                float dpr = Screen.height > 0 ? 1f : 1f;
                try { NP_Crosshair(gap * dpr, rifle.AimMode, hitmark, Cursor.lockState == CursorLockMode.Locked ? 1 : 0); } catch { }
            }
            sendT -= Time.unscaledDeltaTime;
            if (sendT > 0f) return;
            sendT = 1f / 15f;
            var d = HeistDirector.Instance;
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder(256);
            sb.Append("{\"type\":\"hud\"");
            if (d)
            {
                sb.Append(",\"phase\":\"").Append(d.Current).Append('"');
                sb.Append(",\"route\":\"").Append(HeistDirector.FullRoute ? "full" : "featured").Append('"');
                var encounter = PaintballEncounter.Instance;
                if (encounter && encounter.Required) sb.Append(",\"defenders\":").Append(encounter.Cleared).Append(",\"defenderTotal\":").Append(encounter.Total);
                if (!iris) iris = FindFirstObjectByType<IrisRelay>();
                if (iris) sb.Append(",\"iris\":").Append(iris.Next);
                sb.Append(",\"objective\":\"").Append(d.Objective).Append('"');
                sb.Append(",\"timeMs\":").Append(Mathf.RoundToInt((d.Elapsed + d.Penalty) * 1000f));
                sb.Append(",\"limitMs\":").Append(Mathf.RoundToInt(d.timeLimit * 1000f));
                sb.Append(",\"relays\":").Append(d.RelaysOnline).Append(",\"relayTotal\":").Append(d.relays != null ? d.relays.Length : 0);
                sb.Append(",\"core\":").Append(d.core && d.core.Carried ? "true" : "false");
                sb.Append(",\"unlocked\":").Append(d.core && d.core.Unlocked ? "true" : "false");
                // per-relay checklist (A/B/C) and the vault lock progress for the HUD objective card
                sb.Append(",\"rs\":[");
                if (d.relays != null) for (int i = 0; i < d.relays.Length; i++) { if (i > 0) sb.Append(','); sb.Append(d.relays[i] && d.relays[i].Online ? 1 : 0); }
                sb.Append(']');
                if (!vault) vault = FindFirstObjectByType<VaultRings>();
                if (vault) sb.Append(",\"lock\":").Append(vault.Locked).Append(",\"lockTotal\":").Append(vault.rings.Count).Append(",\"inVault\":").Append(d.player && vault.vaultArea.Contains(d.player.transform.position) ? 1 : 0);
            }
            if (rifle) sb.Append(",\"ammo\":").Append(rifle.Ammo).Append(",\"mag\":").Append(rifle.magazine).Append(",\"reload\":").Append(rifle.Reloading ? rifle.ReloadProgress.ToString("F2", inv) : "-1")
                    .Append(",\"wstate\":\"").Append(rifle.StateLabel).Append("\",\"pressure\":").Append(rifle.Pressure.ToString("F2", inv)).Append(",\"low\":").Append(rifle.Ammo <= rifle.lowAmmo ? 1 : 0);
            if (tags)
            {
                sb.Append(",\"hits\":").Append(tags.Hits).Append(",\"hitsToTag\":").Append(tags.HitsToTag);
                sb.Append(",\"tagged\":").Append(HeistDirector.Instance && HeistDirector.Instance.player && HeistDirector.Instance.player.TaggedOut ? "true" : "false");
                float since = Time.time - tags.LastHitTime;
                if (since < 1.2f && cam)
                {
                    Vector3 dir = -tags.LastHitDirection; dir.y = 0;
                    Vector3 fwd = cam.transform.forward; fwd.y = 0;
                    float ang = Vector3.SignedAngle(fwd, dir, Vector3.up);
                    sb.Append(",\"hitDir\":").Append(ang.ToString("F0", inv)).Append(",\"hitAge\":").Append(since.ToString("F2", inv));
                }
            }
            bool detected = false;
            foreach (var ai in CompetitorAIRegistry.All) if (ai && ai.EngagingPlayer) { detected = true; break; }
            foreach (var dr in CompetitorAIRegistry.Drones) if (dr && dr.Current == SentinelDrone.State.Alert) { detected = true; break; }
            sb.Append(",\"detected\":").Append(detected ? "true" : "false");
            var vs = VaultSecurity.Instance;
            if (vs && vs.Locked) sb.Append(",\"lockdown\":").Append(vs.LockRemaining.ToString("F1", inv));
            if (FinalityElevator.Active) sb.Append(",\"ascend\":").Append(FinalityElevator.Progress.ToString("F2", inv)).Append(",\"ascBlocks\":").Append(FinalityElevator.Blocks);
            // Objective markers (screen-space, normalised 0..1; z<0 = behind the camera)
            if (d && cam)
            {
                sb.Append(",\"markers\":[");
                bool first = true;
                void M(Vector3 w, string kind)
                {
                    Vector3 v = cam.WorldToViewportPoint(w);
                    float dist = d.player ? Vector3.Distance(d.player.transform.position, w) : 0f;
                    if (!first) sb.Append(','); first = false;
                    sb.Append("{\"k\":\"").Append(kind).Append("\",\"x\":").Append(v.x.ToString("F3", inv)).Append(",\"y\":").Append(v.y.ToString("F3", inv))
                      .Append(",\"z\":").Append(v.z.ToString("F1", inv)).Append(",\"d\":").Append(Mathf.RoundToInt(dist)).Append('}');
                }
                if (d.Current == HeistDirector.Phase.Infiltrate && d.relays != null) foreach (var r in d.relays) if (r && !r.Online) M(r.transform.position, "relay");
                if (d.Current == HeistDirector.Phase.CoreUnlocked && d.core)
                {   // still in the Relay Room: guide to its open exit (the Core is in the vault beyond it), not 300 m through walls
                    if (!roomExit) roomExit = FindFirstObjectByType<RoomExit>();
                    bool inRoom = roomExit && d.player && (d.player.transform.position - roomExit.transform.position).sqrMagnitude < 60f * 60f;
                    M(inRoom ? roomExit.transform.position + Vector3.up * 2.2f : d.core.coreVisual.position, "core");
                }
                if (d.Current == HeistDirector.Phase.Pursuit && d.gate) M(d.gate.transform.position + Vector3.up * 3f, "gate");
                sb.Append(']');
                // Rival nameplates: only when actually visible (line of sight from the camera), always labelled AI.
                sb.Append(",\"bots\":[");
                bool fb = true;
                foreach (var ai in CompetitorAIRegistry.All)
                {
                    if (!ai || ai.TaggedOut) continue;
                    Vector3 head = ai.transform.position + Vector3.up * 2.05f;
                    Vector3 vp = cam.WorldToViewportPoint(head);
                    if (vp.z <= 0f || vp.z > 45f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;
                    Vector3 from = cam.transform.position, to = head + Vector3.down * 0.5f;
                    if (Physics.Linecast(from, to, out var hit, ~((1 << 8) | (1 << 11) | (1 << 12)), QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(ai.transform)) continue;
                    if (!fb) sb.Append(','); fb = false;
                    bool painted = ai.GetComponent<PaintDynamic>() && Time.time - ai.GetComponent<PaintDynamic>().LastPaintTime < 8f;
                    sb.Append("{\"n\":\"").Append(ai.displayName).Append("\",\"x\":").Append(vp.x.ToString("F3", inv)).Append(",\"y\":").Append(vp.y.ToString("F3", inv))
                      .Append(",\"s\":\"").Append(ai.EngagingPlayer ? "!" : ai.Current == CompetitorAI.State.Investigate || ai.Current == CompetitorAI.State.Search ? "?" : "").Append("\",\"p\":").Append(painted ? 1 : 0).Append('}');
                }
                sb.Append(']');
            }
            if (lastAnnounce.Length > 0) { sb.Append(",\"announce\":\"").Append(lastAnnounce).Append('"'); lastAnnounce = ""; }
            sb.Append('}');
            WebBridge.Send(sb.ToString());
        }
    }

    // Tiny registry so HUD/director can query AI without FindObjects every frame.
    public static class CompetitorAIRegistry
    {
        public static readonly System.Collections.Generic.List<CompetitorAI> All = new System.Collections.Generic.List<CompetitorAI>();
        public static readonly System.Collections.Generic.List<SentinelDrone> Drones = new System.Collections.Generic.List<SentinelDrone>();
    }
}
