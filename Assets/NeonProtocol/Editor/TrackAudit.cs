using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Escape audit (rescue order §4): measures the REAL free space either side of the road centre line against the
    // visible stage geometry (temporary mesh colliders on every stage renderer), every metre, at bike height.
    // A defect = the lane the bike may use (HalfWidth - 0.75 m, plus the bike's 0.45 m half-width) reaches into geometry.
    // Fix(): narrows the baked track width where needed so the lane never enters a wall. Report: Builds/track_audit.txt
    public static class TrackAudit
    {
        const float BikeHalf = 0.45f, LaneInset = 0.75f, Margin = 0.15f;

        static List<(float s, float half, float freeL, float freeR)> Measure(FinalityTrack t, out int tested)
        {
            var run = Object.FindFirstObjectByType<FinalityRun>(FindObjectsInactive.Include);
            var stage = run && run.stageRoot ? run.stageRoot : t.gameObject;
            bool wasActive = stage.activeSelf; stage.SetActive(true);
            var temp = new List<Collider>();
            foreach (var mf in stage.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || mf.GetComponent<Collider>()) continue;
                var r = mf.GetComponent<Renderer>(); if (!r || !r.enabled) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; temp.Add(mc);
            }
            Physics.SyncTransforms();
            var res = new List<(float, float, float, float)>(); tested = 0;
            for (float s = 2f; s < t.Length - 2f; s += 1f)
            {
                tested++;
                t.Sample(s, out var p, out _, out var rgt, out var up, out _);
                float half = t.HalfWidth(s);
                Vector3 o = p + up * 0.9f;
                float far = half + 6f;
                float fl = Physics.Raycast(o, -rgt, out var hl, far, ~0, QueryTriggerInteraction.Ignore) && !Mine(hl.collider) ? hl.distance : far;
                float fr = Physics.Raycast(o, rgt, out var hr, far, ~0, QueryTriggerInteraction.Ignore) && !Mine(hr.collider) ? hr.distance : far;
                res.Add((s, half, fl, fr));
            }
            foreach (var c in temp) Object.DestroyImmediate(c);
            stage.SetActive(wasActive);
            return res;
        }
        static bool Mine(Collider c) => c && (c.GetComponentInParent<Lightcycle>() || c.GetComponentInParent<TrackElement>() || c.GetComponentInParent<BikeDrone>() || c.isTrigger);   // gameplay elements in the lane are intended

        // Lateral sweep: the bike's box at every 0.25 m across the drivable lane, every metre. Any overlap with stage
        // geometry (not gameplay elements) = a place where the bike visually drives through a wall/pillar/divider.
        [MenuItem("Neon Protocol/Finality/Sweep Bike Bounds")]
        public static string Sweep()
        {
            var t = Object.FindFirstObjectByType<FinalityTrack>(FindObjectsInactive.Include); if (!t) return "no track";
            var run = Object.FindFirstObjectByType<FinalityRun>(FindObjectsInactive.Include);
            var stage = run && run.stageRoot ? run.stageRoot : t.gameObject; bool was = stage.activeSelf; stage.SetActive(true);
            var temp = new List<Collider>();
            foreach (var mf in stage.GetComponentsInChildren<MeshFilter>(true))
            { if (!mf.sharedMesh || mf.GetComponent<Collider>()) continue; var r = mf.GetComponent<Renderer>(); if (!r || !r.enabled) continue; var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; temp.Add(mc); }
            Physics.SyncTransforms();
            var hits = new Collider[16]; var bad = new SortedDictionary<int, string>();
            for (float s = 2f; s < t.Length - 2f; s += 1f)
            {
                t.Sample(s, out var p, out var f, out var rgt, out var up, out _);
                float lane = t.HalfWidth(s) - LaneInset;
                var rot = Quaternion.LookRotation(f, up);
                for (float x = -lane; x <= lane + 0.01f; x += 0.25f)
                {
                    if (s >= t.dividerS0 && s <= t.dividerS1 && Mathf.Abs(x) < t.dividerHalf + 0.55f) continue;   // the bike can't be here
                    int n = Physics.OverlapBoxNonAlloc(p + rgt * x + up * 0.9f, new Vector3(BikeHalf, 0.55f, 0.9f), hits, rot, ~0, QueryTriggerInteraction.Ignore);
                    for (int k = 0; k < n; k++) if (!Mine(hits[k]) && !hits[k].GetComponentInParent<Lightcycle>())
                    { int key = Mathf.RoundToInt(s); if (!bad.ContainsKey(key)) bad[key] = $"s={s:F0} x={x:F2} {hits[k].name}"; break; }
                }
            }
            foreach (var c in temp) Object.DestroyImmediate(c);
            stage.SetActive(was);
            var sb = new StringBuilder($"bike-bounds sweep: {bad.Count} metres of track where the drivable lane overlaps stage geometry" + System.Environment.NewLine);
            foreach (var kv in bad) sb.AppendLine(kv.Value);
            System.IO.File.WriteAllText("Builds/track_sweep.txt", sb.ToString());
            return sb.ToString().Length > 3000 ? sb.ToString().Substring(0, 3000) : sb.ToString();
        }

        // Light-gate posts intrude ~0.3 m into the far-left lane edge every 60 m. Taper the baked width around each
        // reported gate sample (+-8 m, smooth) instead of moving the combined gate mesh.
        public static string TaperAt(string csvS, float targetHalf)
        {
            var t = Object.FindFirstObjectByType<FinalityTrack>(FindObjectsInactive.Include);
            int changed = 0;
            foreach (var tok in csvS.Split(','))
            {
                if (!float.TryParse(tok, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float g)) continue;
                for (float s = g - 8f; s <= g + 8f; s += t.step)
                {
                    int i = Mathf.RoundToInt(s / t.step); if (i < 0 || i >= t.width.Length) continue;
                    float k = 1f - Mathf.Clamp01((Mathf.Abs(s - g) - 2f) / 6f);       // full inside +-2 m, blend to 0 at +-8 m
                    float w = Mathf.Lerp(t.width[i], Mathf.Min(t.width[i], targetHalf * 2f), Mathf.SmoothStep(0, 1, k));
                    if (w < t.width[i] - 0.001f) { t.width[i] = w; changed++; }
                }
            }
            EditorUtility.SetDirty(t); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(t.gameObject.scene); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(t.gameObject.scene);
            return "tapered samples: " + changed;
        }

        [MenuItem("Neon Protocol/Finality/Audit Track Clearance")]
        public static string Audit() => Run(false);
        [MenuItem("Neon Protocol/Finality/Fix Track Clearance")]
        public static string Fix() => Run(true);

        static string Run(bool fix)
        {
            var t = Object.FindFirstObjectByType<FinalityTrack>(FindObjectsInactive.Include); if (!t) return "no track";
            var m = Measure(t, out int tested);
            var sb = new StringBuilder($"Finality track clearance audit ({(fix ? "FIX" : "AUDIT")}), {tested} samples at 1 m, bike half-width {BikeHalf} m, lane inset {LaneInset} m\n");
            int bad = 0; float worst = 0f; var ranges = new List<string>(); float runStart = -1f, prevS = -10f;
            foreach (var (s, half, fl, fr) in m)
            {
                float lane = half - LaneInset + BikeHalf;               // farthest the bike body reaches from the centre
                float over = Mathf.Max(lane - fl, lane - fr);
                if (over > 0.02f)
                {
                    bad++; worst = Mathf.Max(worst, over);
                    if (s - prevS > 1.5f) { if (runStart >= 0) ranges.Add($"{runStart:F0}-{prevS:F0}"); runStart = s; }
                    prevS = s;
                    if (fix)
                    {
                        int i = Mathf.RoundToInt(s / t.step);
                        float allowedHalf = Mathf.Min(fl, fr) - BikeHalf + LaneInset - Margin;
                        if (i >= 0 && i < t.width.Length) t.width[i] = Mathf.Max(3.2f, Mathf.Min(t.width[i], allowedHalf * 2f));
                    }
                }
            }
            if (runStart >= 0) ranges.Add($"{runStart:F0}-{prevS:F0}");
            sb.AppendLine($"samples where the lane enters geometry: {bad} (worst overlap {worst:F2} m)");
            sb.AppendLine("ranges (m): " + string.Join(", ", ranges));
            if (fix) { EditorUtility.SetDirty(t); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(t.gameObject.scene); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(t.gameObject.scene); }
            System.IO.File.WriteAllText("Builds/track_audit" + (fix ? "_fix" : "") + ".txt", sb.ToString());
            return sb.ToString();
        }
    }
}
