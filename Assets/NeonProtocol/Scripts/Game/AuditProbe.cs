using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace NeonProtocol
{
    // Audit-only measurements (no gameplay change): camera clipping statistics over many positions/headings.
    public class AuditProbe : MonoBehaviour
    {
        public static string Result = "";
        public static void CameraClip(int samples) { var go = new GameObject("_Audit"); go.AddComponent<AuditProbe>().StartCoroutine(go.GetComponent<AuditProbe>().Clip(samples)); }

        public static string Mark = "";
        public static void QATake() { var go = new GameObject("_AuditTake"); go.AddComponent<AuditProbe>().StartCoroutine(go.GetComponent<AuditProbe>().Take()); }

        // Scripted proof take: turning while running, ADS + firing, stairs, crouch at cover, tag-out (death) + respawn.
        IEnumerator Take()
        {
            var h = HeistDirector.Instance; var rig = FindFirstObjectByType<ShoulderRig>();
            foreach (var ai in CompetitorAIRegistry.All.ToArray()) ai.gameObject.SetActive(false);
            foreach (var d in CompetitorAIRegistry.Drones.ToArray()) d.gameObject.SetActive(false);
            NPInput.UseOverride = true; h.BeginRun(); yield return null;
            h.player.Teleport(new Vector3(0, 0.05f, -16f), 0f); rig.SetAim(0, 8);
            NPInput.State S(Vector2 mv, bool aim = false, bool fire = false, bool sprint = false) => new NPInput.State { move = mv, aim = aim, fire = fire, sprint = sprint };
            IEnumerator Hold(NPInput.State st, float t, float yawRate = 0, string m = null) { if (m != null) Mark = m; float e = 0; while (e < t) { NPInput.Override = st; if (yawRate != 0) rig.SetAim(rig.Yaw + yawRate * Time.deltaTime, rig.Pitch); e += Time.deltaTime; yield return null; } }
            yield return Hold(S(new Vector2(0, 1)), 2.0f, 0, "01 run forward");
            yield return Hold(S(new Vector2(0, 1)), 2.5f, 70f, "02 run while turning camera (relaxed)");
            yield return Hold(S(Vector2.zero), 1.0f, 90f, "03 stand, swing camera 90 deg/s");
            yield return Hold(S(new Vector2(1, 0)), 1.5f, 0, "04 strafe right");
            yield return Hold(S(Vector2.zero, aim: true), 1.0f, 0, "05 ADS");
            yield return Hold(S(Vector2.zero, aim: true, fire: true), 2.0f, 15f, "06 ADS fire while tracking");
            yield return Hold(S(new Vector2(0, 0.6f), aim: true, fire: true), 1.5f, 0, "07 walk + fire");
            h.player.Teleport(new Vector3(19.5f, 0.05f, -23f), 0f); rig.SetAim(0, 10);
            yield return Hold(S(new Vector2(0, 1)), 3.5f, 0, "08 climb stairs");
            h.player.Teleport(new Vector3(-7f, 0.05f, -13.5f), 0f); rig.SetAim(0, 5);
            NPInput.Override = new NPInput.State { crouchPressed = true }; yield return null;
            yield return Hold(S(Vector2.zero), 1.2f, 0, "09 crouch behind cover");
            yield return Hold(S(Vector2.zero, aim: true), 1.0f, 0, "10 crouch ADS");
            NPInput.Override = new NPInput.State { crouchPressed = true }; yield return null;
            Mark = "11 tag-out (forced 4 hits)";
            var th = h.player.GetComponent<TagHealth>();
            for (int i = 0; i < 4; i++) { th.OnPaintHit(new PaintHit { team = 1, point = h.player.transform.position + Vector3.up, direction = Vector3.back }); yield return new WaitForSeconds(0.15f); }
            yield return Hold(S(Vector2.zero), 4.5f, 0, "12 down + respawn");
            yield return Hold(S(new Vector2(0, 1)), 2.0f, 0, "13 move after respawn");
            Mark = "done"; NPInput.UseOverride = false;
        }

        IEnumerator Clip(int samples)
        {
            Result = "running";
            var h = HeistDirector.Instance; var rig = FindFirstObjectByType<ShoulderRig>(); var cam = rig.cam;
            foreach (var ai in CompetitorAIRegistry.All.ToArray()) ai.enabled = false;
            foreach (var d in CompetitorAIRegistry.Drones.ToArray()) d.enabled = false;
            NPInput.UseOverride = true;
            var tri = NavMesh.CalculateTriangulation();
            var rng = new System.Random(3);
            int inside = 0, occluded = 0, total = 0; var sb = new StringBuilder();
            int mask = ~((1 << 8) | (1 << 9) | (1 << 10) | (1 << 11) | (1 << 12));
            for (int i = 0; i < samples; i++)
            {
                Vector3 p = tri.vertices[rng.Next(tri.vertices.Length)];
                float yaw = rng.Next(0, 8) * 45f;
                h.player.Teleport(p + Vector3.up * 0.05f, yaw);
                rig.SetAim(yaw + 180f, rng.Next(-20, 40));   // look back toward walls behind/around the spawn
                for (int f = 0; f < 6; f++) yield return null;
                Vector3 cp = cam.transform.position;
                Vector3 pivot = h.player.transform.position + Vector3.up * 1.55f;
                bool isInside = Physics.CheckSphere(cp, cam.nearClipPlane + 0.02f, mask, QueryTriggerInteraction.Ignore);
                bool blocked = Physics.Linecast(pivot, cp, mask, QueryTriggerInteraction.Ignore);
                total++; if (isInside) inside++; if (blocked) occluded++;
                if ((isInside || blocked) && sb.Length < 600) sb.Append($"[{p.x:F0},{p.y:F0},{p.z:F0} y{rig.Yaw:F0} {(isInside ? "IN" : "")}{(blocked ? "BLK" : "")}]");
            }
            Result = $"samples={total} cameraInsideGeometry={inside} wallBetweenPlayerAndCamera={occluded} | {sb}";
            NPInput.UseOverride = false;
        }
    }
}
