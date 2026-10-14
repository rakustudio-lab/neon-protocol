using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.AI.Navigation;

namespace NeonProtocol.EditorTools
{
    // Core Vault encounter redesign (additive, idempotent: skipped if "04b_VaultEncounter" already exists).
    //   * Centerpiece: reactor spindle from the ceiling feeding the Core, three counter-rotating rings.
    //   * Lock: one paint node per ring (hit 3x within 2 s while it moves) -> Relay C comes online.
    //   * Detection: a HIGH scanner (crouch) and a LOW scanner (jump) sweep the floor. Crossing = LOCKDOWN:
    //     shutters close both exits for 8 s, hunters converge, unfinished ring progress resets.
    //   * Routes: west stairs from the trench exit to the balcony (stealth/overwatch), front door (loud),
    //     low cover on the floor, hiding space under the balcony, east door to the corridor.
    public static class VaultUpgrade
    {
        const string MatDir = "Assets/NeonProtocol/Art/Materials";
        const string Root = "04b_VaultEncounter";
        static Material floorM, obsidianM, graphiteM, metalM, glowM;

        [MenuItem("Neon Protocol/Build Core Vault Encounter (one-time)")]
        public static string Build()
        {
            if (GameObject.Find(Root)) return "exists: " + Root + " (not rebuilt)";
            floorM = Load("M_Floor"); obsidianM = Load("M_Obsidian"); graphiteM = Load("M_Graphite"); metalM = Load("M_Metal"); glowM = Load("M_Glow");
            var lamp = Glow("M_NodeLamp", new Color(4f, 0.3f, 1.6f));
            var beamM = Glow("M_ScanBeam", new Color(4.5f, 0.6f, 0.3f));
            var linkM = Load("M_CoreGlow") ?? glowM;

            var arch = GameObject.Find("Architecture").transform;
            var root = new GameObject(Root).transform; root.SetParent(arch);
            var log = new List<string>();
            Vector3 c = new Vector3(0, 0, 23f);

            // ---- centerpiece: reactor spindle + energy link to the Core ----
            B(root, "Spindle", c + new Vector3(0, 12f, 0), new Vector3(1.8f, 8f, 1.8f), obsidianM, 0.1f).paintable = false;
            B(root, "Spindle_Cap", c + new Vector3(0, 7.85f, 0), new Vector3(3f, 0.3f, 3f), metalM, 0.08f).paintable = false;
            foreach (var (n, o, s) in new[] { ("N", new Vector3(0, 0, 0.92f), new Vector3(0.12f, 7.6f, 0.06f)), ("S", new Vector3(0, 0, -0.92f), new Vector3(0.12f, 7.6f, 0.06f)),
                                              ("E", new Vector3(0.92f, 0, 0), new Vector3(0.06f, 7.6f, 0.12f)), ("W", new Vector3(-0.92f, 0, 0), new Vector3(0.06f, 7.6f, 0.12f)) })
                Deco(root, "Spindle_Seam_" + n, c + new Vector3(0, 12f, 0) + o, s, glowM);
            Deco(root, "Energy_Link", c + new Vector3(0, 5.95f, 0), new Vector3(0.18f, 3.7f, 0.18f), linkM);

            // ---- rotating rings + nodes ----
            var ringsGo = new GameObject("VaultLock"); ringsGo.transform.SetParent(root); ringsGo.transform.position = c + Vector3.up * 3f;
            var vr = ringsGo.AddComponent<VaultRings>();
            var specs = new[] { (h: 2.6f, r: 3.6f, n: 14, speed: 28f, a0: 0f), (h: 4.6f, r: 5.0f, n: 18, speed: -20f, a0: 120f), (h: 6.8f, r: 3.2f, n: 12, speed: 40f, a0: 240f) };
            for (int k = 0; k < specs.Length; k++)
            {
                var sp = specs[k];
                var ring = new GameObject("Ring" + (k + 1)).transform; ring.SetParent(ringsGo.transform); ring.position = c + Vector3.up * sp.h; ring.rotation = Quaternion.Euler(0, sp.a0, 0);
                float arc = 2f * Mathf.PI * sp.r / sp.n;
                for (int i = 1; i < sp.n; i++)   // segment 0 is the node
                {
                    var q = Quaternion.Euler(0, i * 360f / sp.n, 0);
                    var seg = Deco(ring, "Seg" + i, Vector3.zero, new Vector3(arc * 0.82f, 0.22f, 0.4f), metalM);
                    seg.transform.localPosition = q * new Vector3(0, 0, sp.r); seg.transform.localRotation = q;
                    var strip = Deco(ring, "Strip" + i, Vector3.zero, new Vector3(arc * 0.82f, 0.04f, 0.1f), glowM);
                    strip.transform.localPosition = q * new Vector3(0, 0.13f, sp.r + 0.12f); strip.transform.localRotation = q;
                }
                var nodeGo = new GameObject("Node" + (k + 1)); nodeGo.transform.SetParent(ring, false); nodeGo.transform.localPosition = new Vector3(0, 0, sp.r);
                var lampR = Deco(nodeGo.transform, "Lamp", Vector3.zero, new Vector3(0.75f, 0.75f, 0.75f), lamp);
                lampR.transform.localPosition = Vector3.zero; lampR.transform.localRotation = Quaternion.Euler(45, 0, 45);
                var col = nodeGo.AddComponent<BoxCollider>(); col.size = Vector3.one * 1.05f;
                var node = nodeGo.AddComponent<VaultNode>(); node.lamp = lampR;
                vr.rings.Add(new VaultRings.Ring { ring = ring, speed = sp.speed, node = node });
            }
            var rl = new GameObject("ReactorLight").AddComponent<Light>(); rl.transform.SetParent(ringsGo.transform); rl.transform.position = c + new Vector3(0, 5.5f, 0);
            rl.type = LightType.Point; rl.color = new Color(0.25f, 1f, 0.95f); rl.range = 14f; rl.intensity = 10f; rl.shadows = LightShadows.None;
            vr.reactorLight = rl;
            vr.audioSource = ringsGo.AddComponent<AudioSource>(); vr.audioSource.spatialBlend = 0.4f; vr.audioSource.playOnAwake = false;
            vr.lockClip = Clip("forceField_000"); vr.resetClip = Clip("computerNoise_000"); vr.openClip = Clip("doorOpen_000");
            var relayC = GameObject.Find("Relay_C")?.GetComponent<Relay>();
            if (relayC) { relayC.acceptsPaint = false; vr.vaultRelay = relayC; EditorUtility.SetDirty(relayC); log.Add("RelayC->lock"); }

            // ---- security: scanners + shutters ----
            var secGo = new GameObject("VaultSecurity"); secGo.transform.SetParent(root); secGo.transform.position = c;
            var sec = secGo.AddComponent<VaultSecurity>();
            var high = Beam(root, "Scanner_High", new Vector3(1.25f, 1.4f, 13.5f), new Vector3(27.4f, 0.06f, 0.06f), beamM, true);
            var low = Beam(root, "Scanner_Low", new Vector3(-11f, 0.45f, 20.5f), new Vector3(0.06f, 0.06f, 17f), beamM, false);
            sec.beams = new[]
            {
                new VaultSecurity.Beam { beam = high.transform, from = new Vector3(1.25f, 1.4f, 13.5f), to = new Vector3(1.25f, 1.4f, 28.2f), period = 7f, phase = 0f, halfExtents = new Vector3(13.7f, 0.08f, 0.15f) },
                new VaultSecurity.Beam { beam = low.transform, from = new Vector3(-11f, 0.45f, 20.5f), to = new Vector3(10.4f, 0.45f, 20.5f), period = 9f, phase = 3f, halfExtents = new Vector3(0.15f, 0.08f, 8.5f) },
            };
            sec.beamRenderers = new[] { high.GetComponentInChildren<Renderer>(), low.GetComponentInChildren<Renderer>() };
            var sFront = Shutter(root, "Shutter_Front", new Vector3(0, 4.5f, 11.5f), new Vector3(11.2f, 9f, 0.6f));
            var sEast = Shutter(root, "Shutter_East", new Vector3(15.5f, 3f, 16f), new Vector3(0.6f, 6f, 6.2f));
            sec.shutters = new[] { sFront, sEast };
            sec.audioSource = secGo.AddComponent<AudioSource>(); sec.audioSource.spatialBlend = 0f; sec.audioSource.playOnAwake = false;
            sec.alarmClip = Clip("computerNoise_000");

            // ---- routes: west stealth stairs to the balcony, balcony west fill, cover ----
            var ws = B(root, "Vault_Stairs_W", new Vector3(-13.75f, 0f, 20.5f), new Vector3(3f, 5f, 8.5f), graphiteM, 0.02f, NeonBlock.Shape.Stairs);
            ws.steps = 18; NeonBlockTools.Rebuild(ws);
            B(root, "Balcony_W_Fill", new Vector3(-14.75f, 4.75f, 31.5f), new Vector3(1.5f, 0.5f, 5f), floorM, 0.03f);
            B(root, "Cover_V1", new Vector3(-5f, 0.55f, 15.2f), new Vector3(3f, 1.1f, 0.8f), graphiteM, 0.05f);
            B(root, "Cover_V2", new Vector3(5.5f, 0.55f, 15.2f), new Vector3(3f, 1.1f, 0.8f), graphiteM, 0.05f);
            B(root, "Cover_V3", new Vector3(-10.5f, 0.55f, 25f), new Vector3(0.8f, 1.1f, 3f), graphiteM, 0.05f);
            B(root, "Cover_V4", new Vector3(9.8f, 0.55f, 26f), new Vector3(2f, 1.1f, 0.8f), graphiteM, 0.05f);

            // ---- drone route: keep clear of the spindle/rings ----
            var dr = GameObject.FindFirstObjectByType<SentinelDrone>();
            if (dr && dr.route != null)
                foreach (var p in dr.route) if (p && (p.position - new Vector3(0, 7, 24)).sqrMagnitude < 1f) { p.position = new Vector3(0, 7.5f, 16.5f); log.Add("drone P moved"); }

            // ---- rebake paint atlas (new paintable blocks) + navmesh ----
            string atlas = NeonBlockTools.BakePaintAtlas();
            var nav = arch.GetComponent<NavMeshSurface>(); if (nav) { nav.BuildNavMesh(); log.Add("navmesh"); }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            return "vault encounter built | " + string.Join(",", log) + " | " + atlas;
        }

        // ---------- helpers ----------
        static Material Load(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
        static AudioClip Clip(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/NeonProtocol/Audio/Kenney/{n}.ogg");

        static Material Glow(string n, Color hdr)
        {
            var path = $"{MatDir}/{n}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;
            m = new Material(Shader.Find("NeonProtocol/Surface")) { name = n };
            m.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.03f)); m.SetColor("_EmissionColor", hdr); m.SetFloat("_SeamGlow", 0);
            m.SetFloat("_TrimSpacing", 0.0001f); m.SetFloat("_TrimWidth", 10f);
            m.DisableKeyword("_NP_PAINT"); m.SetFloat("_Paintable", 0); m.enableInstancing = false;   // no instancing on Web (compile stalls)
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static NeonBlock B(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, float chamfer, NeonBlock.Shape shape = NeonBlock.Shape.Box)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.position = pos;
            go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var b = go.AddComponent<NeonBlock>(); b.shape = shape; b.size = size; b.chamfer = chamfer;
            NeonBlockTools.Rebuild(b);
            return b;
        }

        // Moving or decorative: no collider, not paintable, no shadows.
        static Renderer Deco(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var b = B(parent, name, pos, size, mat, 0.02f);
            b.paintable = false; b.addCollider = false; NeonBlockTools.Rebuild(b);
            var col = b.GetComponent<Collider>(); if (col) Object.DestroyImmediate(col);
            var r = b.GetComponent<Renderer>(); r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        static GameObject Beam(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool alongX)
        {
            var go = new GameObject(name); go.transform.SetParent(parent); go.transform.position = pos;
            Deco(go.transform, "Ray", pos, size, mat);
            float half = (alongX ? size.x : size.z) * 0.5f;
            foreach (int sgn in new[] { -1, 1 })
            {
                var e = Deco(go.transform, "Emitter", pos, new Vector3(0.3f, 0.3f, 0.3f), metalM);
                e.transform.position = pos + (alongX ? Vector3.right : Vector3.forward) * (half * sgn);
            }
            return go;
        }

        static Transform Shutter(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            var b = B(parent, name, pos, size, metalM, 0.04f);
            b.paintable = false; NeonBlockTools.Rebuild(b);
            var rb = b.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var mod = b.gameObject.AddComponent<NavMeshModifier>(); mod.ignoreFromBuild = true;
            var ob = b.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>(); ob.carving = true; ob.shape = UnityEngine.AI.NavMeshObstacleShape.Box; ob.size = size; ob.enabled = false;
            bool wide = size.x > size.z;
            var strip = Deco(b.transform, "Shutter_Strip", pos + Vector3.up * (size.y * 0.5f - 0.3f), wide ? new Vector3(size.x - 0.4f, 0.12f, size.z + 0.04f) : new Vector3(size.x + 0.04f, 0.12f, size.z - 0.4f), Load("M_ScanBeam"));
            return b.transform;
        }
    }
}
