using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Keeps NeonBlock meshes saved as assets (so scenes stay small and editable) and bakes the paint atlas UVs.
    [InitializeOnLoad]
    public static class NeonBlockTools
    {
        public const string GeneratedRoot = "Assets/NeonProtocol/Generated";
        public const int AtlasLogicalSize = 4096;
        public const int Padding = 2;          // texels around every island (at the logical size)
        public const float MaxDensity = 64f;   // texels per metre
        public static string LastAreaReport = "";

        static NeonBlockTools()
        {
            EditorApplication.hierarchyChanged += () => { if (!Application.isPlaying) EditorApplication.delayCall += RebuildDirtyBlocks; };
            Undo.postprocessModifications += mods => { EditorApplication.delayCall += RebuildDirtyBlocks; return mods; };
        }

        public static void RebuildDirtyBlocks()
        {
            if (Application.isPlaying) return;
            foreach (var b in Object.FindObjectsByType<NeonBlock>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b.NeedsRebuild) Rebuild(b);
        }

        [MenuItem("Neon Protocol/Rebuild All Blocks")]
        public static void RebuildAll()
        {
            foreach (var b in Object.FindObjectsByType<NeonBlock>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Rebuild(b);
            BakePaintAtlas();
        }

        public static void Rebuild(NeonBlock b)
        {
            var scene = b.gameObject.scene;
            string dir = $"{GeneratedRoot}/{(string.IsNullOrEmpty(scene.name) ? "Shared" : scene.name)}";
            Directory.CreateDirectory(dir);
            string id = GlobalObjectId.GetGlobalObjectIdSlow(b).targetObjectId.ToString();
            if (id == "0") id = b.GetInstanceID().ToString().Replace("-", "n");

            var mf = b.GetComponent<MeshFilter>();
            var mesh = b.Generate();
            string path = $"{dir}/{b.name}_{id}.asset";
            mesh.name = Path.GetFileNameWithoutExtension(path);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, path);
            mf.sharedMesh = mesh;

            if (b.addCollider)
            {
                var walk = b.GenerateWalkCollider();
                if (walk)
                {
                    foreach (var bc in b.GetComponents<BoxCollider>()) Object.DestroyImmediate(bc);
                    string cpath = $"{dir}/{b.name}_{id}_col.asset";
                    walk.name = Path.GetFileNameWithoutExtension(cpath);
                    var ce = AssetDatabase.LoadAssetAtPath<Mesh>(cpath);
                    if (ce) { EditorUtility.CopySerialized(walk, ce); walk = ce; } else AssetDatabase.CreateAsset(walk, cpath);
                    var mc = b.GetComponent<MeshCollider>(); if (!mc) mc = b.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = walk; mc.convex = false;
                }
                else if (b.shape == NeonBlock.Shape.Ramp)
                {
                    foreach (var bc in b.GetComponents<BoxCollider>()) Object.DestroyImmediate(bc);
                    var mc = b.GetComponent<MeshCollider>(); if (!mc) mc = b.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                }
                else
                {
                    foreach (var mc in b.GetComponents<MeshCollider>()) Object.DestroyImmediate(mc);
                    var bc = b.GetComponent<BoxCollider>(); if (!bc) bc = b.gameObject.AddComponent<BoxCollider>();
                    bc.center = Vector3.zero; bc.size = b.size;
                }
            }
            if (b.paintable && !b.GetComponent<PaintSurface>()) b.gameObject.AddComponent<PaintSurface>();
            if (!b.paintable) { var ps = b.GetComponent<PaintSurface>(); if (ps) Object.DestroyImmediate(ps); }
            b.MarkBuilt();
            EditorUtility.SetDirty(b); EditorUtility.SetDirty(mesh);
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }

        /// Splits tall paintable boxes at a height so the reachable band gets full paint resolution.
        [MenuItem("Neon Protocol/Split Tall Walls (6.5 m)")]
        public static string SplitTallBlocks() => SplitTallBlocks(6.5f);

        public static string SplitTallBlocks(float splitY)
        {
            int n = 0;
            foreach (var b in Object.FindObjectsByType<NeonBlock>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b.shape != NeonBlock.Shape.Box || !b.paintable || b.transform.rotation != Quaternion.identity) continue;
                float bottom = b.transform.position.y - b.size.y * 0.5f, top = bottom + b.size.y;
                if (b.size.y < 7.5f || bottom > splitY - 0.5f || top < splitY + 1f) continue;
                var upper = Object.Instantiate(b.gameObject, b.transform.parent);
                upper.name = b.name + "_Upper";
                var ub = upper.GetComponent<NeonBlock>();
                ub.size = new Vector3(b.size.x, top - splitY, b.size.z);
                upper.transform.position = new Vector3(b.transform.position.x, splitY + ub.size.y * 0.5f, b.transform.position.z);
                b.size = new Vector3(b.size.x, splitY - bottom, b.size.z);
                b.transform.position = new Vector3(b.transform.position.x, bottom + b.size.y * 0.5f, b.transform.position.z);
                upper.GetComponent<MeshFilter>().sharedMesh = null;   // force its own mesh asset
                Rebuild(b); Rebuild(ub); n++;
            }
            return $"split {n} tall blocks at {splitY} m";
        }

        /// Splits big boxes into pieces of at most maxSize (X/Z) so Forward rendering's per-object light limit
        /// picks nearby lights for each piece instead of the "most important" six for a 35 m slab.
        [MenuItem("Neon Protocol/Tile Large Blocks (9 m)")]
        public static string TileLargeBlocks() => TileLargeBlocks(9f);

        public static string TileLargeBlocks(float maxSize)
        {
            int made = 0, split = 0;
            foreach (var b in Object.FindObjectsByType<NeonBlock>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b.shape != NeonBlock.Shape.Box || b.transform.rotation != Quaternion.identity || b.transform.lossyScale != Vector3.one) continue;
                int nx = Mathf.CeilToInt(b.size.x / maxSize), nz = Mathf.CeilToInt(b.size.z / maxSize);
                if (nx * nz <= 1) continue;
                Vector3 c = b.transform.position, s0 = b.size, piece = new Vector3(s0.x / nx, s0.y, s0.z / nz);
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                    {
                        Vector3 pc = c + new Vector3(-s0.x * 0.5f + piece.x * (ix + 0.5f), 0, -s0.z * 0.5f + piece.z * (iz + 0.5f));
                        GameObject go;
                        if (ix == 0 && iz == 0) go = b.gameObject;
                        else { go = Object.Instantiate(b.gameObject, b.transform.parent); go.name = $"{b.name}_{ix}_{iz}"; go.GetComponent<MeshFilter>().sharedMesh = null; made++; }
                        var nb = go.GetComponent<NeonBlock>();
                        go.transform.position = pc; nb.size = piece;
                        Rebuild(nb);
                    }
                split++;
            }
            return $"tiled {split} blocks into {made} extra pieces";
        }

        // ---------------- paint atlas ----------------

        struct Chart { public Mesh mesh; public List<int> verts; public Vector3 o, u, w, n, center; public Vector2 min, size; public RectInt px; public bool dead; public float weight; }

        [MenuItem("Neon Protocol/Bake Paint Atlas (open scenes)")]
        public static string BakePaintAtlas()
        {
            var surfaces = Object.FindObjectsByType<PaintSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var charts = new List<Chart>();
            var meshes = new List<(Mesh mesh, Transform tr)>();
            foreach (var s in surfaces)
            {
                var mf = s.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                if (meshes.Any(m => m.mesh == mf.sharedMesh)) { Debug.LogWarning($"[PaintAtlas] {s.name} shares a mesh; paint would mirror. Skipped.", s); continue; }
                meshes.Add((mf.sharedMesh, s.transform));
                charts.AddRange(ExtractCharts(mf.sharedMesh, s.transform));
            }
            if (charts.Count == 0) return "no paintable surfaces";

            // Faces nobody can see don't deserve atlas space: buried faces, and exterior faces looking into the void
            // (up-facing faces are always kept so open-air floors stay paintable).
            Physics.SyncTransforms();
            int dead = 0;
            for (int i = 0; i < charts.Count; i++)
            {
                var c = charts[i];
                // Sample the whole face (centre, vertices and edge midpoints pulled 15% inward): a face is hidden only
                // if EVERY sample is buried, or (for non-upward faces) every sample looks into the void.
                var pts = new List<Vector3> { c.center };
                var vs = c.verts.Select(v => c.mesh.vertices[v]).ToList();
                var tr = meshes.First(m => m.mesh == c.mesh).tr;
                for (int k = 0; k < vs.Count; k++)
                {
                    Vector3 a = tr.TransformPoint(vs[k]), b = tr.TransformPoint(vs[(k + 1) % vs.Count]);
                    pts.Add(Vector3.Lerp(c.center, a, 0.85f));
                    pts.Add(Vector3.Lerp(c.center, (a + b) * 0.5f, 0.85f));
                }
                bool allBuried = true, allExterior = c.n.y < 0.5f;
                foreach (var pnt in pts)
                {
                    Vector3 start = pnt + c.n * 0.01f;
                    if (allBuried && !Physics.CheckSphere(start + c.n * 0.02f, 0.005f, ~0, QueryTriggerInteraction.Ignore)) allBuried = false;
                    if (allExterior && Physics.Raycast(start, c.n, 150f, ~0, QueryTriggerInteraction.Ignore)) allExterior = false;
                    if (!allBuried && !allExterior) break;
                }
                bool sliver = Mathf.Min(c.size.x, c.size.y) < 0.08f;   // bevel strips: not worth atlas space
                if (allBuried || allExterior || sliver) { c.dead = true; charts[i] = c; dead++; }
            }
            var live = charts.Where(c => !c.dead).ToList();
            LastAreaReport = string.Join(" ", live.GroupBy(c => c.mesh.name).Select(g => (g.Key, g.Sum(c => c.size.x * c.size.y * (c.weight > 0 ? c.weight * c.weight : 1f))))
                .OrderByDescending(x => x.Item2).Take(18).Select(x => $"{x.Key.Split('_')[0]}{(x.Key.Contains("Upper") ? "U" : "")}:{x.Item2:F0}"))
                + $" | total m2={live.Sum(c => c.size.x * c.size.y * (c.weight > 0 ? c.weight * c.weight : 1f)):F0}";

            // Find the highest density that packs into the atlas.
            float lo = 4f, hi = MaxDensity, best = lo;
            for (int it = 0; it < 18; it++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Pack(live, mid, false)) { best = mid; lo = mid; } else hi = mid;
            }
            Pack(live, best, true);
            int li = 0;
            for (int i = 0; i < charts.Count; i++)
            {
                var c = charts[i];
                c.px = c.dead ? new RectInt(-4096, -4096, 1, 1) : live[li++].px;   // dead: off-atlas, never rasterized or sampled
                if (!c.dead) { var l = live[li - 1]; c.u = l.u; c.w = l.w; c.min = l.min; c.size = l.size; }
                charts[i] = c;
            }

            // Write UV channel 3 (atlas uv) and 4 (island rect).
            foreach (var (mesh, tr) in meshes)
            {
                var verts = mesh.vertices;
                var uv3 = new Vector2[verts.Length];
                var rect = new Vector4[verts.Length];
                foreach (var c in charts.Where(c => c.mesh == mesh))
                {
                    float S = AtlasLogicalSize;
                    Vector4 r = new Vector4(c.px.xMin / S, c.px.yMin / S, c.px.xMax / S, c.px.yMax / S);
                    foreach (int vi in c.verts)
                    {
                        Vector3 p = tr.TransformPoint(verts[vi]) - c.o;
                        Vector2 q = new Vector2(Vector3.Dot(p, c.u), Vector3.Dot(p, c.w)) - c.min;
                        Vector2 t01 = new Vector2(c.size.x > 1e-5f ? q.x / c.size.x : 0, c.size.y > 1e-5f ? q.y / c.size.y : 0);
                        uv3[vi] = new Vector2(Mathf.Lerp(r.x, r.z, t01.x), Mathf.Lerp(r.y, r.w, t01.y));
                        rect[vi] = r;
                    }
                }
                mesh.SetUVs(3, uv3);
                mesh.SetUVs(4, rect.ToList());
                EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets();
            string msg = $"[PaintAtlas] {meshes.Count} surfaces, {charts.Count - dead} live islands ({dead} hidden skipped), density {best:F1} texels/m at {AtlasLogicalSize}px";
            Debug.Log(msg);
            return msg;
        }

        static IEnumerable<Chart> ExtractCharts(Mesh mesh, Transform tr)
        {
            var verts = mesh.vertices; var tris = mesh.triangles;
            // union-find over shared vertex indices -> polygons
            int[] parent = Enumerable.Range(0, verts.Length).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = Find(tris[i]), b = Find(tris[i + 1]), c = Find(tris[i + 2]);
                parent[b] = a; parent[Find(c)] = a;
            }
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < tris.Length; i++)
            {
                int r = Find(tris[i]);
                if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
                if (!g.Contains(tris[i])) g.Add(tris[i]);
            }
            foreach (var g in groups.Values)
            {
                Vector3 p0 = tr.TransformPoint(verts[g[0]]);
                Vector3 p1 = tr.TransformPoint(verts[g[1 % g.Count]]);
                Vector3 p2 = tr.TransformPoint(verts[g[2 % g.Count]]);
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                if (n.sqrMagnitude < 0.5f) n = Vector3.up;
                Vector3 u = (p1 - p0).normalized, w = Vector3.Cross(n, u);
                Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = -mn;
                foreach (int vi in g)
                {
                    Vector3 p = tr.TransformPoint(verts[vi]) - p0;
                    var q = new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, w));
                    mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q);
                }
                Vector3 ctr = Vector3.zero; float minY = float.MaxValue; foreach (int vi in g) { var wp = tr.TransformPoint(verts[vi]); ctr += wp; minY = Mathf.Min(minY, wp.y); } ctr /= g.Count;
                // Paint is mostly seen up close below ~6 m; faces entirely above that get half resolution.
                float weight = minY > 6.5f ? 0.45f : 1f;
                yield return new Chart { mesh = mesh, verts = g, o = p0, u = u, w = w, n = n, center = ctr, min = mn, size = mx - mn, weight = weight };
            }
        }

        // Shelf packing, tallest first.
        static bool Pack(List<Chart> charts, float density, bool write)
        {
            int S = AtlasLogicalSize;
            // Shelf packing: lay every island flat (long side horizontal), then sort by height so shelves fill evenly.
            var order = Enumerable.Range(0, charts.Count).OrderByDescending(i => Mathf.Min(charts[i].size.x, charts[i].size.y) * (charts[i].weight > 0 ? charts[i].weight : 1f))
                .ThenByDescending(i => Mathf.Max(charts[i].size.x, charts[i].size.y)).ToList();
            int x = 0, y = 0, shelfH = 0;
            foreach (int i in order)
            {
                var c = charts[i];
                bool rot = c.size.y > c.size.x;   // lay islands flat to keep shelves tight
                float dd = density * (c.weight > 0f ? c.weight : 1f);
                int w = Mathf.Max(1, Mathf.CeilToInt((rot ? c.size.y : c.size.x) * dd)) + Padding * 2;
                int h = Mathf.Max(1, Mathf.CeilToInt((rot ? c.size.x : c.size.y) * dd)) + Padding * 2;
                if (w > S) return false;
                if (x + w > S) { x = 0; y += shelfH; shelfH = 0; }
                if (y + h > S) return false;
                if (write)
                {
                    if (rot) { var t = c.u; c.u = c.w; c.w = t; c.min = new Vector2(c.min.y, c.min.x); c.size = new Vector2(c.size.y, c.size.x); }
                    c.px = new RectInt(x + Padding, y + Padding, w - Padding * 2, h - Padding * 2);
                    charts[i] = c;
                }
                x += w; shelfH = Mathf.Max(shelfH, h);
            }
            return true;
        }
    }

    [CustomEditor(typeof(NeonBlock)), CanEditMultipleObjects]
    public class NeonBlockEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed) foreach (var t in targets) NeonBlockTools.Rebuild((NeonBlock)t);
            if (GUILayout.Button("Rebuild Mesh")) foreach (var t in targets) NeonBlockTools.Rebuild((NeonBlock)t);
            EditorGUILayout.HelpBox("After layout changes, run Neon Protocol > Bake Paint Atlas before building.", MessageType.Info);
        }
    }
}
