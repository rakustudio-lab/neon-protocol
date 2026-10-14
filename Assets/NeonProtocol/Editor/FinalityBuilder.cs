using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Builds ACT II — THE FINALITY RUN inside THE FRACTURE scene, far from the facility (origin offset O).
    // Track = Catmull-Rom spline baked at 1 m with banking; beats: LAUNCH TUNNEL -> ENERGY BRIDGE (lane choice:
    // safe left / shutter right with more shards) -> DRONE CHASE TUNNEL -> FINALITY GATE.
    // Rebuildable: everything lives under "FinalityRun" (replaced on each run). Meshes saved under Generated/FinalityRun.
    public static class FinalityBuilder
    {
        static readonly Vector3 O = new Vector3(2000f, 0f, 0f);
        static readonly Vector3[] CP = {
            new Vector3(0, 0, -30), new Vector3(0, 0, 0), new Vector3(0, 0, 120), new Vector3(22, 0, 240), new Vector3(0, 2, 360), new Vector3(-18, 4, 460),
            new Vector3(0, 8, 560), new Vector3(55, 10, 670), new Vector3(135, 10, 750), new Vector3(225, 8, 790), new Vector3(315, 4, 780),
            new Vector3(395, 0, 730), new Vector3(425, -8, 640), new Vector3(395, -16, 560), new Vector3(325, -22, 530), new Vector3(255, -26, 550),
            new Vector3(210, -28, 590), new Vector3(190, -29, 630) };
        const float BridgeA = 470f, BridgeB = 980f, DivA = 600f, DivB = 830f;
        const string GenDir = "Assets/NeonProtocol/Generated/FinalityRun";
        const string BikeFbx = "Assets/NeonProtocol/Art/Models/Lightcycle/lightcycle.fbx";
        const string KitFbx = "Assets/NeonProtocol/Art/Models/VaultKit/vault_kit.fbx";
        const string CryptoFbx = "Assets/NeonProtocol/Art/Characters/Crypto/Ch45_nonPBR.fbx";

        static Dictionary<string, Material> M;
        static Dictionary<string, GameObject> kit;
        static FinalityTrack track;
        static Transform stage;

        [MenuItem("Neon Protocol/Art/Build Finality Run")]
        public static string Build()
        {
            System.IO.Directory.CreateDirectory(GenDir);
            M = LoadMats();
            kit = new Dictionary<string, GameObject>();
            foreach (var t in AssetDatabase.LoadAssetAtPath<GameObject>(KitFbx).GetComponentsInChildren<MeshFilter>(true)) kit[t.name] = t.gameObject;

            var old = GameObject.Find("FinalityRun"); if (old) Object.DestroyImmediate(old);
            var root = new GameObject("FinalityRun");
            stage = new GameObject("Stage").transform; stage.SetParent(root.transform);
            var trackGo = new GameObject("Track"); trackGo.transform.SetParent(stage);
            track = trackGo.AddComponent<FinalityTrack>();
            Bake(track);
            float L = track.Length;

            // ---------------- geometry
            MeshObj("Road", RoadMesh(0f, L), M["M_Road"], true);
            MeshObj("RoadUnderside", SlabMesh(BridgeA - 10f, BridgeB + 10f), M["K_Plate"], false);
            MeshObj("Tunnel_A", TunnelMesh(0f, BridgeA), M["K_Plate2S"], true);
            MeshObj("Tunnel_B", TunnelMesh(BridgeB, L), M["K_Plate2S"], true);
            MeshObj("TunnelStrips_A", TunnelStrips(0f, BridgeA), M["G_Violet"], false);
            MeshObj("TunnelStrips_B", TunnelStrips(BridgeB, L), M["G_Magenta"], false);
            MeshObj("Ribs_A", Ribs(6f, BridgeA, 12f, 0.35f, 0.6f), M["K_Trim"], false);
            MeshObj("Ribs_B", Ribs(BridgeB + 6f, L, 12f, 0.35f, 0.6f), M["K_Trim"], false);
            var gatesA = MeshObj("LightGates_A", Ribs(30f, BridgeA, 60f, 0.12f, 0.25f, -0.4f), M["G_Cyan"], false);
            var gatesB = MeshObj("LightGates_B", Ribs(BridgeB + 30f, L, 60f, 0.12f, 0.25f, -0.4f), M["G_Cyan"], false);
            var pulse = trackGo.AddComponent<GatePulse>(); pulse.gates = new[] { gatesA.GetComponent<Renderer>(), gatesB.GetComponent<Renderer>() };
            MeshObj("BridgeRails", Rails(BridgeA - 4f, BridgeB + 4f), M["K_Dark"], true);
            MeshObj("BridgeRailGlow", RailGlow(BridgeA - 4f, BridgeB + 4f), M["G_Cyan"], false);
            MeshObj("Divider", DividerMesh(DivA, DivB), M["K_Plate"], false);
            MeshObj("DividerGlow", DividerGlow(DivA, DivB), M["G_Amber"], false);
            Vista(L);

            // ---------------- gameplay elements
            var el = new GameObject("Elements").transform; el.SetParent(stage);
            Element(el, TrackElement.Kind.Checkpoint, 0f, -6, 6);
            Shard(el, 120f, -3f); Barrier(el, 180f, -6f, -1f); Barrier(el, 245f, 1f, 6f); Shard(el, 300f, 3.5f); Barrier(el, 365f, -2.2f, 2.2f);
            Element(el, TrackElement.Kind.Checkpoint, 480f, -9, 9);
            Barrier(el, 690f, -9f, -5.5f); Shard(el, 760f, -4f);                                   // left lane: safe, 1 shard
            var sh1 = Shutter(el, 655f, 0.8f, 9f); Switch(el, 655f - 1f, 7.4f, sh1); Shard(el, 705f, 5f);   // right lane: paint the switches, 2 shards
            var sh2 = Shutter(el, 790f, 0.8f, 9f); Switch(el, 790f - 1f, 7.4f, sh2); Shard(el, 812f, 4f);
            Shard(el, 905f, 0f);
            Element(el, TrackElement.Kind.Checkpoint, 990f, -6, 6);
            Barrier(el, 1100f, -6f, 0f); Shard(el, 1200f, -3f); Barrier(el, 1260f, 0f, 6f);
            Barrier(el, 1385f, -6f, -2f); Barrier(el, 1385f, 2f, 6f);
            FinalityGate(el, L - 25f);
            Drone(el, 1010f, L - 70f, 26f); Drone(el, 1160f, L - 70f, 34f);

            // ---------------- bike + rider, director
            var bike = BuildBike(root.transform);
            var run = root.AddComponent<FinalityRun>(); run.bike = bike; run.stageRoot = stage.gameObject;
            run.beatStarts = new[] { 0f, BridgeA, BridgeB, L - 120f };
            var music = root.AddComponent<AudioSource>(); music.playOnAwake = false; music.loop = true; music.spatialBlend = 0f; music.volume = 0.55f;
            music.clip = AssetDatabase.FindAssets("pursuit_loop t:AudioClip").Select(g => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            run.music = music;

            EditorSceneManager.MarkSceneDirty(root.scene); EditorSceneManager.SaveScene(root.scene);
            int tris = root.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh ? f.sharedMesh.triangles.Length / 3 : 0);
            return $"finality run: length {L:F0} m, {stage.GetComponentsInChildren<TrackElement>(true).Length} elements, {tris} tris";
        }

        // =============================================================== spline
        static void Bake(FinalityTrack t)
        {
            var dense = new List<Vector3>();
            for (int i = 1; i < CP.Length - 2; i++)
                for (int k = 0; k < 200; k++) dense.Add(O + CatmullRom(CP[i - 1], CP[i], CP[i + 1], CP[i + 2], k / 200f));
            dense.Add(O + CP[CP.Length - 2]);
            var pos = new List<Vector3> { dense[0] }; float acc = 0f;
            for (int i = 1; i < dense.Count; i++)
            {
                float d = Vector3.Distance(dense[i - 1], dense[i]); acc += d;
                while (acc >= 1f) { acc -= 1f; pos.Add(Vector3.Lerp(dense[i - 1], dense[i], 1f - acc / Mathf.Max(d, 1e-5f))); }
            }
            int n = pos.Count;
            var fwd = new Vector3[n]; var yaw = new float[n];
            for (int i = 0; i < n; i++) fwd[i] = (pos[Mathf.Min(n - 1, i + 2)] - pos[Mathf.Max(0, i - 2)]).normalized;
            for (int i = 0; i < n; i++) { var a = fwd[Mathf.Max(0, i - 4)]; var b = fwd[Mathf.Min(n - 1, i + 4)]; a.y = 0; b.y = 0; yaw[i] = Vector3.SignedAngle(a, b, Vector3.up) / 8f; }
            var up = new Vector3[n]; var right = new Vector3[n]; var width = new float[n];
            for (int i = 0; i < n; i++)
            {
                float avg = 0f; int c = 0; for (int j = Mathf.Max(0, i - 15); j <= Mathf.Min(n - 1, i + 15); j++) { avg += yaw[j]; c++; }
                float bank = Mathf.Clamp(-avg / c * 16f, -13f, 13f);
                var r0 = Vector3.Cross(Vector3.up, fwd[i]).normalized; var u0 = Vector3.Cross(fwd[i], r0);
                up[i] = Quaternion.AngleAxis(bank, fwd[i]) * u0; right[i] = Vector3.Cross(up[i], fwd[i]).normalized;
                float s = i;
                width[i] = Mathf.Lerp(12f, 18f, Mathf.Clamp01((s - (BridgeA - 30f)) / 30f) * Mathf.Clamp01(((BridgeB + 30f) - s) / 30f));
            }
            t.pos = pos.ToArray(); t.fwd = fwd; t.up = up; t.right = right; t.width = width; t.step = 1f;
            EditorUtility.SetDirty(t);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // =============================================================== mesh helpers
        class MB { public List<Vector3> v = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int> t = new List<int>();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud); t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
            public Mesh Make(string name)
            {
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
                string p = $"{GenDir}/{name}.asset"; var ex = AssetDatabase.LoadAssetAtPath<Mesh>(p);
                if (ex) { ex.Clear(); EditorUtility.CopySerialized(m, ex); return ex; }
                AssetDatabase.CreateAsset(m, p); return m;
            } }

        static Vector3 W(float s, float x, float h) => track.World(s, x, h);

        static Mesh RoadMesh(float a, float b)
        {
            var mb = new MB();
            for (float s = a; s < b - 0.01f; s += 2f)
            {
                float s1 = Mathf.Min(b, s + 2f), h0 = track.HalfWidth(s), h1 = track.HalfWidth(s1);
                mb.Quad(W(s, -h0, 0), W(s1, -h1, 0), W(s1, h1, 0), W(s, h0, 0), new Vector2(0, s / 8f), new Vector2(0, s1 / 8f), new Vector2(1, s1 / 8f), new Vector2(1, s / 8f));
            }
            return mb.Make("Road");
        }

        static Mesh SlabMesh(float a, float b)
        {
            var mb = new MB();
            for (float s = a; s < b - 0.01f; s += 4f)
            {
                float s1 = Mathf.Min(b, s + 4f), h0 = track.HalfWidth(s), h1 = track.HalfWidth(s1);
                mb.Quad(W(s, h0, -1.6f), W(s1, h1, -1.6f), W(s1, -h1, -1.6f), W(s, -h0, -1.6f), new Vector2(0, s / 2), new Vector2(0, s1 / 2), new Vector2(h1, s1 / 2), new Vector2(h0, s / 2));
                foreach (float sg in new[] { -1f, 1f })
                {
                    Vector3 a0 = W(s, sg * h0, 0), a1 = W(s1, sg * h1, 0), b0 = W(s, sg * (h0 - 1.2f), -1.6f), b1 = W(s1, sg * (h1 - 1.2f), -1.6f);
                    if (sg < 0) mb.Quad(a0, a1, b1, b0, new Vector2(0, s / 2), new Vector2(0, s1 / 2), new Vector2(1, s1 / 2), new Vector2(1, s / 2));
                    else mb.Quad(a1, a0, b0, b1, new Vector2(0, s1 / 2), new Vector2(0, s / 2), new Vector2(1, s / 2), new Vector2(1, s1 / 2));
                }
            }
            return mb.Make("RoadUnderside");
        }

        static Vector2[] Section(float half) => new[] {
            new Vector2(-half, 0), new Vector2(-half - 0.6f, 3.6f), new Vector2(-half + 0.8f, 6.8f), new Vector2(-2.6f, 8f),
            new Vector2(2.6f, 8f), new Vector2(half - 0.8f, 6.8f), new Vector2(half + 0.6f, 3.6f), new Vector2(half, 0) };

        static Mesh TunnelMesh(float a, float b)
        {
            var mb = new MB();
            for (float s = a; s < b - 0.01f; s += 3f)
            {
                float s1 = Mathf.Min(b, s + 3f);
                var p0 = Section(track.HalfWidth(s)); var p1 = Section(track.HalfWidth(s1));
                float per = 0f;
                for (int i = 0; i < p0.Length - 1; i++)
                {
                    float seg = Vector2.Distance(p0[i], p0[i + 1]);
                    // inward-facing: vertices ordered so normals point into the tunnel
                    mb.Quad(W(s, p0[i].x, p0[i].y), W(s1, p1[i].x, p1[i].y), W(s1, p1[i + 1].x, p1[i + 1].y), W(s, p0[i + 1].x, p0[i + 1].y),
                        new Vector2(per / 2f, s / 2f), new Vector2(per / 2f, s1 / 2f), new Vector2((per + seg) / 2f, s1 / 2f), new Vector2((per + seg) / 2f, s / 2f));
                    per += seg;
                }
            }
            return mb.Make("Tunnel_" + a.ToString("F0"));
        }

        static Mesh TunnelStrips(float a, float b)
        {
            var mb = new MB();
            for (float s = a; s < b - 0.01f; s += 3f)
            {
                float s1 = Mathf.Min(b, s + 3f);
                foreach (var (x, y, w) in new[] { (-1f, 3.5f, 0.12f), (1f, 3.5f, 0.12f) })
                {
                    float hx0 = x * (track.HalfWidth(s) + 0.55f), hx1 = x * (track.HalfWidth(s1) + 0.55f);
                    mb.Quad(W(s, hx0, y - w), W(s1, hx1, y - w), W(s1, hx1, y + w), W(s, hx0, y + w), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                    mb.Quad(W(s, hx0, y + w), W(s1, hx1, y + w), W(s1, hx1, y - w), W(s, hx0, y - w), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                }
                mb.Quad(W(s, -0.08f, 7.95f), W(s1, -0.08f, 7.95f), W(s1, 0.08f, 7.95f), W(s, 0.08f, 7.95f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                mb.Quad(W(s, 0.08f, 7.95f), W(s1, 0.08f, 7.95f), W(s1, -0.08f, 7.95f), W(s, -0.08f, 7.95f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            }
            return mb.Make("TunnelStrips_" + a.ToString("F0"));
        }

        // Arched frames following the tunnel section every `every` metres (ribs, or glowing light gates with inset < 0).
        static Mesh Ribs(float a, float b, float every, float depth, float thick, float inset = 0.25f)
        {
            var mb = new MB();
            for (float s = a; s < b; s += every)
            {
                var sec = Section(track.HalfWidth(s));
                for (int i = 0; i < sec.Length - 1; i++)
                {
                    Vector2 p = sec[i], q = sec[i + 1], n = new Vector2(q.y - p.y, p.x - q.x).normalized;   // inward normal of the section edge
                    Vector2 pi = p - n * inset, qi = q - n * inset, po = pi - n * thick, qo = qi - n * thick;
                    float s0 = s - depth, s1 = s + depth;
                    void F(Vector2 A, Vector2 B, float sa, float sb) => mb.Quad(W(sa, A.x, A.y), W(sa, B.x, B.y), W(sb, B.x, B.y), W(sb, A.x, A.y), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    F(po, qo, s0, s1); F(qi, pi, s0, s1);
                    mb.Quad(W(s0, pi.x, pi.y), W(s0, qi.x, qi.y), W(s0, qo.x, qo.y), W(s0, po.x, po.y), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    mb.Quad(W(s1, po.x, po.y), W(s1, qo.x, qo.y), W(s1, qi.x, qi.y), W(s1, pi.x, pi.y), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                }
            }
            return mb.Make($"Ribs_{a:F0}_{every:F0}_{inset:F1}");
        }

        static Mesh Rails(float a, float b)
        {
            var mb = new MB();
            for (float s = a; s < b - 0.01f; s += 4f)
            {
                float s1 = Mathf.Min(b, s + 4f);
                foreach (float sg in new[] { -1f, 1f })
                {
                    float x0 = sg * track.HalfWidth(s), x1 = sg * track.HalfWidth(s1);
                    for (int face = 0; face < 2; face++)
                    {
                        Vector3 A = W(s, x0, 0), B = W(s1, x1, 0), C = W(s1, x1, 1.0f), D = W(s, x0, 1.0f);
                        if ((sg < 0) == (face == 0)) mb.Quad(A, B, C, D, Vector2.zero, Vector2.right, Vector2.one, Vector2.up); else mb.Quad(B, A, D, C, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    }
                }
            }
            return mb.Make("BridgeRails");
        }

        static Mesh RailGlow(float a, float b)
        {
            var mb = new MB();
            for (float s = a; s < b - 0.01f; s += 4f)
            {
                float s1 = Mathf.Min(b, s + 4f);
                foreach (float sg in new[] { -1f, 1f })
                {
                    float x0 = sg * (track.HalfWidth(s) - 0.02f), x1 = sg * (track.HalfWidth(s1) - 0.02f);
                    mb.Quad(W(s, x0, 0.95f), W(s1, x1, 0.95f), W(s1, x1, 1.05f), W(s, x0, 1.05f), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    mb.Quad(W(s1, x1, 0.95f), W(s, x0, 0.95f), W(s, x0, 1.05f), W(s1, x1, 1.05f), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                }
            }
            return mb.Make("BridgeRailGlow");
        }

        static Mesh DividerMesh(float a, float b)
        {
            var mb = new MB();
            for (float s = a; s < b - 0.01f; s += 4f)
            {
                float s1 = Mathf.Min(b, s + 4f);
                foreach (float x in new[] { -0.5f, 0.5f })
                    if (x < 0) mb.Quad(W(s, x, 0), W(s1, x, 0), W(s1, x, 1.4f), W(s, x, 1.4f), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    else mb.Quad(W(s1, x, 0), W(s, x, 0), W(s, x, 1.4f), W(s1, x, 1.4f), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                mb.Quad(W(s, -0.5f, 1.4f), W(s1, -0.5f, 1.4f), W(s1, 0.5f, 1.4f), W(s, 0.5f, 1.4f), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
            mb.Quad(W(a, 0.5f, 0), W(a, -0.5f, 0), W(a, -0.5f, 1.4f), W(a, 0.5f, 1.4f), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            return mb.Make("Divider");
        }

        static Mesh DividerGlow(float a, float b)
        {
            var mb = new MB();
            for (float s = a; s < b - 0.01f; s += 4f)
            {
                float s1 = Mathf.Min(b, s + 4f);
                mb.Quad(W(s, -0.51f, 1.2f), W(s1, -0.51f, 1.2f), W(s1, -0.51f, 1.3f), W(s, -0.51f, 1.3f), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                mb.Quad(W(s1, 0.51f, 1.2f), W(s, 0.51f, 1.2f), W(s, 0.51f, 1.3f), W(s1, 0.51f, 1.3f), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
            return mb.Make("DividerGlow");
        }

        static GameObject MeshObj(string name, Mesh m, Material mat, bool shadows)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(stage); go.GetComponent<MeshFilter>().sharedMesh = m;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        // Kit piece instance with project materials.
        static GameObject Kit(string piece, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var src = kit[piece];
            var go = new GameObject(piece, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent); go.transform.SetPositionAndRotation(pos, rot * src.transform.localRotation);
            go.transform.localScale = Vector3.Scale(src.transform.localScale, scale);
            go.GetComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials.Select(m => M.TryGetValue(m.name, out var k) ? k : m).ToArray();
            return go;
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent); go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = m; return go;
        }

        static void Vista(float L)
        {
            var v = new GameObject("Vista").transform; v.SetParent(stage);
            var rng = new System.Random(7);
            for (float s = BridgeA - 20f; s < BridgeB + 40f; s += 22f)
            {
                track.Sample(s, out var p, out var f, out var r, out _, out _);
                foreach (float sg in new[] { -1f, 1f })
                {
                    float d = 55f + (float)rng.NextDouble() * 150f, hgt = 4f + (float)rng.NextDouble() * 8f;
                    Vector3 at = p + r * sg * d + Vector3.down * (60f + (float)rng.NextDouble() * 40f);
                    if (!Clear(at, 30f + hgt * 2f)) continue;   // the track loops back near the bridge: never pierce a tunnel
                    if (rng.NextDouble() < 0.7) Kit("Pillar", v, at, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), new Vector3(hgt * 1.2f, hgt * 1.4f, hgt * 1.2f));
                    else Kit("Capacitor", v, at + Vector3.up * 30f, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), Vector3.one * hgt * 3f);
                }
            }
            // megastructure rings hanging in the sky and the distant Finality spire the bridge points at
            for (int i = 0; i < 4; i++)
            {
                float s = BridgeA + 80f + i * 120f; track.Sample(s, out var p, out var f, out var r, out _, out _);
                Kit("Crown_Outer", v, p + Vector3.up * (55f + i * 12f) + r * (i % 2 == 0 ? 40f : -50f), Quaternion.LookRotation(f) * Quaternion.Euler(90f, 0, 0), Vector3.one * (5f + i));
            }
            var spire = GameObject.CreatePrimitive(PrimitiveType.Cylinder); spire.name = "FinalitySpire"; Object.DestroyImmediate(spire.GetComponent<Collider>());
            spire.transform.SetParent(v); spire.transform.position = O + new Vector3(520f, 120f, 1180f); spire.transform.localScale = new Vector3(14f, 260f, 14f);
            spire.GetComponent<MeshRenderer>().sharedMaterial = M["Beam_Spire"];
            // pylons under the bridge
            for (float s = BridgeA; s < BridgeB; s += 40f)
            {
                track.Sample(s, out var p, out var f, out _, out var u, out _);
                Box(v, "Pylon", p + Vector3.down * 40f, Quaternion.LookRotation(f), new Vector3(3f, 78f, 3f), M["K_Plate"]);
                Box(v, "PylonGlow", p + Vector3.down * 40f + Vector3.Cross(Vector3.up, f) * 1.52f, Quaternion.LookRotation(f), new Vector3(0.05f, 76f, 0.25f), M["G_Violet"]);
            }
        }

        static bool Clear(Vector3 at, float radius)
        {
            for (float s = 0f; s < track.Length; s += 8f)
            {
                var q = track.World(s, 0f, 0f) - at; q.y = 0f;
                if (q.magnitude < radius) return false;
            }
            return true;
        }

        // =============================================================== elements
        static Quaternion Rot(float s) { track.Sample(s, out _, out var f, out _, out var u, out _); return Quaternion.LookRotation(f, u); }

        static TrackElement Element(Transform parent, TrackElement.Kind k, float s, float x0, float x1)
        {
            var go = new GameObject($"{k}_{s:F0}"); go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(W(s, (x0 + x1) * 0.5f, 0f), Rot(s));
            var e = go.AddComponent<TrackElement>(); e.kind = k; e.s = s; e.x0 = x0; e.x1 = x1; return e;
        }

        static void Shard(Transform p, float s, float x)
        {
            var e = Element(p, TrackElement.Kind.Shard, s, x - 1.1f, x + 1.1f);
            var g = Kit("CoreGem", e.transform, W(s, x, 1.4f), Rot(s), Vector3.one * 0.9f); e.moving = g.transform;
            var halo = GameObject.CreatePrimitive(PrimitiveType.Cylinder); halo.name = "Halo"; Object.DestroyImmediate(halo.GetComponent<Collider>());
            halo.transform.SetParent(g.transform, false); halo.transform.localScale = new Vector3(2.2f, 1.2f, 2.2f); halo.GetComponent<MeshRenderer>().sharedMaterial = M["Beam_Shard"];
        }

        static void Barrier(Transform p, float s, float x0, float x1)
        {
            var e = Element(p, TrackElement.Kind.Barrier, s, x0, x1);
            var q = Rot(s);
            Box(e.transform, "Block", W(s, (x0 + x1) / 2, 0.8f), q, new Vector3(x1 - x0, 1.6f, 1.2f), M["K_Plate"]);
            Box(e.transform, "Glow", W(s, (x0 + x1) / 2, 1.62f), q, new Vector3(x1 - x0, 0.06f, 1.24f), M["G_Magenta"]);
            Box(e.transform, "Stripe", W(s, (x0 + x1) / 2, 0.8f) - (q * Vector3.forward) * 0.61f, q, new Vector3(x1 - x0 - 0.2f, 0.18f, 0.02f), M["G_Amber"]);
        }

        static TrackElement Shutter(Transform p, float s, float x0, float x1)
        {
            var e = Element(p, TrackElement.Kind.Shutter, s, x0, x1);
            var q = Rot(s);
            Box(e.transform, "PostL", W(s, x0 - 0.2f, 3.5f), q, new Vector3(0.4f, 7f, 0.8f), M["K_Trim"]);
            Box(e.transform, "PostR", W(s, x1 + 0.2f, 3.5f), q, new Vector3(0.4f, 7f, 0.8f), M["K_Trim"]);
            Box(e.transform, "Header", W(s, (x0 + x1) / 2, 7.2f), q, new Vector3(x1 - x0 + 0.8f, 0.5f, 0.9f), M["K_Plate"]);
            var door = new GameObject("Door").transform; door.SetParent(e.transform); door.SetPositionAndRotation(W(s, (x0 + x1) / 2, 0f), q);
            Box(door, "Slab", W(s, (x0 + x1) / 2, 2.5f), q, new Vector3(x1 - x0, 5f, 0.3f), M["K_Plate"]);
            for (int i = 0; i < 3; i++) Box(door, "Hazard", W(s, (x0 + x1) / 2, 1f + i * 1.5f) - (q * Vector3.forward) * 0.16f, q, new Vector3(x1 - x0 - 0.3f, 0.12f, 0.02f), M["G_Amber"]);
            e.moving = door;
            e.lamp = Box(e.transform, "Lamp", W(s, (x0 + x1) / 2, 6.6f) - (q * Vector3.forward) * 0.5f, q, new Vector3(1.2f, 0.3f, 0.05f), M["G_Lamp"]).GetComponent<Renderer>();
            return e;
        }

        static void Switch(Transform p, float s, float x, TrackElement opens)
        {
            float h = 5.8f;
            var e = Element(p, TrackElement.Kind.Switch, s, x, x); e.opens = opens;
            var q = Rot(s);
            Box(e.transform, "Arm", W(s, x, h + 0.9f), q, new Vector3(0.18f, 1.8f, 0.18f), M["K_Trim"]);
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere); lamp.name = "Target";
            lamp.transform.SetParent(e.transform); lamp.transform.position = W(s, x, h); lamp.transform.localScale = Vector3.one * 1.4f;
            lamp.GetComponent<MeshRenderer>().sharedMaterial = M["G_Lamp"]; e.lamp = lamp.GetComponent<Renderer>();
            ((SphereCollider)lamp.GetComponent<Collider>()).radius = 0.75f;      // generous: it's hit from a moving bike
            var ring = Kit("NodeHousing", e.transform, W(s, x, h), q * Quaternion.Euler(90f, 0, 0), Vector3.one * 1.8f);
        }

        static void FinalityGate(Transform p, float s)
        {
            var e = Element(p, TrackElement.Kind.Gate, s, -20f, 20f);
            // three arch frames following the tunnel section + an energy field filling the arch (fits the tunnel by construction)
            var frames = new[] { (0f, "G_Amber", 0.55f), (3.5f, "G_Cyan", 0.35f), (7f, "G_Magenta", 0.35f) };
            foreach (var (ds, mat, thick) in frames)
            {
                var go = new GameObject("GateArch_" + mat, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(e.transform);
                go.GetComponent<MeshFilter>().sharedMesh = Ribs(s + ds, s + ds + 0.5f, 10f, 0.25f, thick, -0.05f);
                go.GetComponent<MeshRenderer>().sharedMaterial = M[mat];
            }
            var mb = new MB(); var sec = Section(track.HalfWidth(s)); Vector3 c = W(s + 1f, 0f, 3.6f);
            for (int i = 0; i < sec.Length - 1; i++)
            {
                Vector3 a = W(s + 1f, sec[i].x * 0.97f, sec[i].y * 0.97f), b = W(s + 1f, sec[i + 1].x * 0.97f, sec[i + 1].y * 0.97f);
                mb.Quad(c, a, b, c, Vector2.zero, Vector2.right, Vector2.one, Vector2.up); mb.Quad(c, b, a, c, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
            { Vector3 a = W(s + 1f, sec[sec.Length - 1].x * 0.97f, 0f), b = W(s + 1f, sec[0].x * 0.97f, 0f); mb.Quad(c, a, b, c, Vector2.zero, Vector2.right, Vector2.one, Vector2.up); mb.Quad(c, b, a, c, Vector2.zero, Vector2.right, Vector2.one, Vector2.up); }
            var field = new GameObject("GateField", typeof(MeshFilter), typeof(MeshRenderer)); field.transform.SetParent(e.transform);
            field.GetComponent<MeshFilter>().sharedMesh = mb.Make("GateField");
            var fr = field.GetComponent<MeshRenderer>(); fr.sharedMaterial = M["Beam_Gate"]; fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var l = new GameObject("GateLight").AddComponent<Light>(); l.transform.SetParent(e.transform); l.transform.position = W(s - 6f, 0, 4f);
            l.type = LightType.Point; l.range = 26f; l.intensity = 6f; l.color = new Color(1f, 0.6f, 0.9f);
        }

        static void Drone(Transform p, float wake, float sleep, float lead)
        {
            var go = new GameObject($"Drone_{wake:F0}"); go.transform.SetParent(p); go.transform.position = W(wake, 0, 6f);
            var d = go.AddComponent<BikeDrone>(); d.wakeS = wake; d.sleepS = sleep; d.lead = lead;
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere); body.name = "Body"; body.transform.SetParent(go.transform, false); body.transform.localScale = new Vector3(1.3f, 0.7f, 1.3f);
            body.GetComponent<MeshRenderer>().sharedMaterial = M["K_Trim"];
            ((SphereCollider)body.GetComponent<Collider>()).radius = 0.8f;
            Kit("LockRing_C", go.transform, go.transform.position, Quaternion.identity, Vector3.one * 0.32f).transform.SetParent(go.transform, true);
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere); eye.name = "Eye"; Object.DestroyImmediate(eye.GetComponent<Collider>());
            eye.transform.SetParent(go.transform, false); eye.transform.localPosition = new Vector3(0, 0, 0.62f); eye.transform.localScale = Vector3.one * 0.38f;
            eye.GetComponent<MeshRenderer>().sharedMaterial = M["G_Lamp"]; d.eye = eye.GetComponent<Renderer>();
            var lr = go.AddComponent<LineRenderer>(); lr.positionCount = 10; lr.useWorldSpace = true; lr.sharedMaterial = M["G_Warn"]; lr.widthMultiplier = 0.3f; lr.enabled = false;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; d.warnLine = lr;
            var a = go.AddComponent<AudioSource>(); a.spatialBlend = 0.6f; a.playOnAwake = false; d.audioSource = a;
            d.warnClip = Clip("forceField_001"); d.fireClip = Clip("rifle_shot_3"); d.downClip = Clip("slime_000");
        }

        static AudioClip Clip(string name) => AssetDatabase.FindAssets(name + " t:AudioClip").Select(g => AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();

        // =============================================================== bike + rider
        static Lightcycle BuildBike(Transform root)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(BikeFbx);
            var go = new GameObject("Lightcycle"); go.transform.SetParent(root); go.layer = 8;
            var lean = new GameObject("LeanPivot").transform; lean.SetParent(go.transform, false);
            // the FBX maps Blender +Y (nose) to Unity -Z: turn the model so the nose points along +Z (travel direction)
            var model = new GameObject("Model").transform; model.SetParent(lean, false); model.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Transform Piece(string n)
            {
                var s = src.transform.Find(n); var o = Object.Instantiate(s.gameObject, model); o.name = n;
                o.transform.localPosition = s.localPosition; o.transform.localRotation = s.localRotation; o.transform.localScale = s.localScale;
                var mr = o.GetComponent<MeshRenderer>(); mr.sharedMaterials = mr.sharedMaterials.Select(m => M.TryGetValue(m.name, out var k) ? k : m).ToArray();
                foreach (var c in o.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                return o.transform;
            }
            var body = Piece("Bike_Body"); var wf = Piece("Wheel_Front"); var wr = Piece("Wheel_Rear"); var launcher = Piece("Launcher");
            // handlebar where CRYPTO's hands land (rider pose puts them ~1.2 m up, 0.2-0.35 m ahead of the hips)
            Box(lean, "Handlebar", go.transform.position + new Vector3(0f, 1.2f, 0.28f), Quaternion.identity, new Vector3(0.62f, 0.05f, 0.06f), M["K_Trim"]);
            Box(lean, "Grip_L", go.transform.position + new Vector3(-0.3f, 1.2f, 0.28f), Quaternion.identity, new Vector3(0.1f, 0.07f, 0.08f), M["K_Dark"]);
            Box(lean, "Grip_R", go.transform.position + new Vector3(0.3f, 1.2f, 0.28f), Quaternion.identity, new Vector3(0.1f, 0.07f, 0.08f), M["K_Dark"]);
            Box(lean, "Stem", go.transform.position + new Vector3(0f, 1.08f, 0.36f), Quaternion.Euler(-25f, 0f, 0f), new Vector3(0.06f, 0.26f, 0.06f), M["K_Trim"]);
            // light trail from the rear wheel (the concept's ribbon behind the bike) + speed streaks around the camera path
            var trailGo = new GameObject("Trail"); trailGo.transform.SetParent(lean, false); trailGo.transform.localPosition = new Vector3(0f, 0.25f, -1.25f);
            var tr = trailGo.AddComponent<TrailRenderer>(); tr.time = 0.22f; tr.minVertexDistance = 0.6f; tr.widthMultiplier = 0.14f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0)); tr.sharedMaterial = M["FX_Trail"];
            var tg = new Gradient(); tg.SetKeys(new[] { new GradientColorKey(new Color(0.3f, 0.95f, 1f), 0), new GradientColorKey(new Color(0.8f, 0.3f, 1f), 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            tr.colorGradient = tg; tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var streakGo = new GameObject("SpeedStreaks"); streakGo.transform.SetParent(go.transform, false); streakGo.transform.localPosition = new Vector3(0f, 2f, 30f);
            var ps = streakGo.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = 0.5f; main.startSpeed = 0f; main.startSize = 0.06f; main.maxParticles = 220; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.5f, 0.9f, 1f, 0.8f), new Color(1f, 0.5f, 1f, 0.8f));
            var em = ps.emission; em.rateOverTime = 160f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(14f, 7f, 6f);
            var pr = streakGo.GetComponent<ParticleSystemRenderer>(); pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0f; pr.cameraVelocityScale = 0.06f; pr.lengthScale = 2f;
            pr.sharedMaterial = M["FX_Trail"]; pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(lean, false); muzzle.localPosition = new Vector3(0f, 0.55f, 1.45f);
            var under = new GameObject("UnderGlow").AddComponent<Light>(); under.transform.SetParent(lean, false); under.transform.localPosition = new Vector3(0, 0.3f, 0);
            under.type = LightType.Point; under.range = 6f; under.intensity = 3f; under.color = new Color(0.3f, 0.9f, 1f);
            // CRYPTO, seated
            var crypto = AssetDatabase.LoadAssetAtPath<GameObject>(CryptoFbx);
            var rider = (GameObject)PrefabUtility.InstantiatePrefab(crypto); PrefabUtility.UnpackPrefabInstance(rider, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            rider.name = "Rider_CRYPTO"; rider.transform.SetParent(lean, false); rider.transform.localPosition = new Vector3(0f, 0.06f, -0.42f);
            foreach (var t in rider.GetComponentsInChildren<Transform>()) t.gameObject.layer = 8;
            var an = rider.GetComponent<Animator>() ?? rider.AddComponent<Animator>(); an.applyRootMotion = false;
            var playerMats = GameObject.Find("Player")?.GetComponentInChildren<SkinnedMeshRenderer>()?.sharedMaterials;
            if (playerMats != null) foreach (var smr in rider.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.sharedMaterials = playerMats;
            rider.AddComponent<RiderPose>();
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer = 8;

            var bike = go.AddComponent<Lightcycle>();
            bike.track = track; bike.body = lean; bike.wheelFront = wf; bike.wheelRear = wr; bike.muzzle = muzzle; bike.riderRoot = rider.transform;
            var eng = go.AddComponent<AudioSource>(); eng.loop = true; eng.playOnAwake = true; eng.spatialBlend = 0f; eng.volume = 0.4f; eng.clip = Clip("engineCircular_000"); bike.engine = eng;
            var sfx = go.AddComponent<AudioSource>(); sfx.playOnAwake = false; sfx.spatialBlend = 0f; bike.sfx = sfx;
            bike.hitClip = Clip("tag_out"); bike.shardClip = Clip("hit_confirm_1"); bike.fireClip = Clip("rifle_shot_0"); bike.dodgeClip = Clip("forceField_000");
            return bike;
        }

        // =============================================================== materials
        static Dictionary<string, Material> LoadMats()
        {
            var d = new Dictionary<string, Material>();
            foreach (var n in new[] { "K_Plate", "K_Dark", "K_Trim", "G_Cyan", "G_Magenta", "G_Amber", "G_Violet", "K_Glass" })
                d[n] = AssetDatabase.LoadAssetAtPath<Material>($"Assets/NeonProtocol/Art/Materials/Kit/{n}.mat");
            Material Get(string n, Shader s) { string p = $"Assets/NeonProtocol/Art/Materials/Kit/{n}.mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(p); if (!m) { m = new Material(s) { name = n }; AssetDatabase.CreateAsset(m, p); } m.shader = s; d[n] = m; return m; }
            var lit = Shader.Find("Universal Render Pipeline/Lit"); var unlit = Shader.Find("Universal Render Pipeline/Unlit"); var beam = Shader.Find("NeonProtocol/Beam");
            foreach (var (suffix, normal, srgb) in new[] { ("_A", false, true), ("_N", true, false), ("_MS", false, false), ("_E", false, true) })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath($"Assets/NeonProtocol/Art/Textures/T_Road{suffix}.png");
                if (ti && (ti.textureType != (normal ? TextureImporterType.NormalMap : TextureImporterType.Default) || ti.sRGBTexture != srgb))
                { ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default; ti.sRGBTexture = srgb; ti.maxTextureSize = 1024; ti.anisoLevel = 8; ti.SaveAndReimport(); }
            }
            var road = Get("M_Road", lit);
            road.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/NeonProtocol/Art/Textures/T_Road_A.png"));
            road.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/NeonProtocol/Art/Textures/T_Road_N.png"));
            road.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/NeonProtocol/Art/Textures/T_Road_MS.png"));
            road.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/NeonProtocol/Art/Textures/T_Road_E.png"));
            road.EnableKeyword("_NORMALMAP"); road.EnableKeyword("_METALLICSPECGLOSSMAP"); road.EnableKeyword("_EMISSION");
            road.SetColor("_EmissionColor", new Color(3f, 3f, 3f)); road.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; road.SetFloat("_Smoothness", 1f);
            var two = AssetDatabase.LoadAssetAtPath<Material>("Assets/NeonProtocol/Art/Materials/Kit/K_Plate2S.mat");
            if (!two) { two = new Material(d["K_Plate"]) { name = "K_Plate2S" }; AssetDatabase.CreateAsset(two, "Assets/NeonProtocol/Art/Materials/Kit/K_Plate2S.mat"); }
            two.CopyPropertiesFromMaterial(d["K_Plate"]); two.SetFloat("_Cull", 0f); two.doubleSidedGI = true; two.SetColor("_BaseColor", new Color(1.3f, 1.2f, 1.5f)); two.EnableKeyword("_EMISSION"); two.SetTexture("_EmissionMap", two.GetTexture("_BaseMap")); two.SetColor("_EmissionColor", new Color(1.6f, 1.1f, 3.2f)); two.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; d["K_Plate2S"] = two;
            var trail = Get("FX_Trail", Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            trail.SetFloat("_Surface", 1f); trail.SetFloat("_Blend", 2f); trail.SetColor("_BaseColor", new Color(2.5f, 2.5f, 2.5f, 1f));
            trail.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); trail.EnableKeyword("_BLENDMODE_ADD"); trail.renderQueue = 3000;
            trail.SetOverrideTag("RenderType", "Transparent"); trail.SetFloat("_ZWrite", 0f); trail.SetFloat("_SrcBlend", 1f); trail.SetFloat("_DstBlend", 1f);
            var lamp = Get("G_Lamp", unlit); lamp.SetColor("_BaseColor", new Color(3.5f, 1.6f, 0.3f));
            var warn = Get("G_Warn", unlit); warn.SetColor("_BaseColor", new Color(4f, 0.25f, 0.3f));
            void B(string n, Color c, float core, float a, float b, float soft) { var m = Get(n, beam); m.SetColor("_Color", c); m.SetFloat("_Core", core); m.SetFloat("_FadeBottom", a); m.SetFloat("_FadeTop", b); m.SetFloat("_FadeSoft", soft); m.SetFloat("_BandAmount", 0.3f); }
            B("Beam_Spire", new Color(0.9f, 0.35f, 1.6f), 1.5f, -1f, 1f, 0.4f);
            B("Beam_Shard", new Color(0.15f, 0.9f, 1.1f), 2.5f, -1f, 1f, 0.6f);
            B("Beam_Gate", new Color(0.35f, 0.12f, 0.8f), 0.3f, -100000f, 100000f, 0.01f);
            foreach (var m in d.Values) EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return d;
        }
    }
}
