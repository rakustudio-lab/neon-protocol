using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.AI.Navigation;

namespace NeonProtocol.EditorTools
{
    // One-time blockout of THE FRACTURE (never overwrites an existing scene). Afterwards the level is ordinary,
    // hand-editable NeonBlocks: move/resize in the Editor, then Bake Paint Atlas + bake the NavMesh.
    //
    //   ARRIVAL (south, raised)  ->  MAIN HALL (catwalks E/W, bridge, reactor, trench on the west)
    //   LOWER TRENCH (stealth, Relay B)  ->  CORE VAULT (north: pedestal, balcony + Relay C, community art wall)
    //   Relay A on the east catwalk.  Pursuit: vault east door -> service corridor -> FINALITY GATE (east).
    public static class FractureBuilder
    {
        public const string ScenePath = "Assets/NeonProtocol/Scenes/TheFracture.unity";
        const string MatDir = "Assets/NeonProtocol/Art/Materials";

        static Material floorM, obsidianM, graphiteM, trimM, metalM, glowM, glassM;
        static Transform arch;

        [MenuItem("Neon Protocol/Create THE FRACTURE (one-time)")]
        public static string Create()
        {
            if (File.Exists(ScenePath)) return "exists: " + ScenePath + " (not overwritten)";
            // Start from the test chamber's systems so player/camera/paint wiring is identical.
            EditorSceneManager.OpenScene(ChamberBuilder.ScenePath, OpenSceneMode.Single);
            var scene = EditorSceneManager.GetActiveScene();
            if (!EditorSceneManager.SaveScene(scene, ScenePath, true)) return "save-as failed";
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            foreach (var n in new[] { "Architecture", "Lighting", "ReflectionProbe" }) { var g = GameObject.Find(n); if (g) Object.DestroyImmediate(g); }
            floorM = Load("M_Floor"); obsidianM = Load("M_Obsidian"); graphiteM = Load("M_Graphite"); trimM = Load("M_TrimWall"); metalM = Load("M_Metal");
            glowM = GlowMaterial("M_Glow", new Color(0.35f, 0.15f, 1.6f));
            glassM = GlowMaterial("M_RelayCore", new Color(0.4f, 0.1f, 1.2f));

            arch = new GameObject("Architecture").transform;
            var gameplay = new GameObject("Gameplay").transform;

            // ---------------- ARRIVAL (raised platform, south) ----------------
            var arrival = Group("01_Arrival");
            B(arrival, "Platform", new Vector3(0, 1.0f, -29.5f), new Vector3(18, 2f, 11), floorM, 0.04f);
            B(arrival, "Wall_S", new Vector3(0, 7f, -35.5f), new Vector3(20, 14, 1), trimM, 0.06f);
            B(arrival, "Wall_W", new Vector3(-9.5f, 7f, -29.5f), new Vector3(1, 14, 13), obsidianM, 0.06f);
            B(arrival, "Wall_E", new Vector3(9.5f, 7f, -29.5f), new Vector3(1, 14, 13), obsidianM, 0.06f);
            // monumental frame looking into the hall
            B(arrival, "Frame_L", new Vector3(-6.5f, 7f, -24.2f), new Vector3(2.4f, 12, 1.6f), obsidianM, 0.1f);
            B(arrival, "Frame_R", new Vector3(6.5f, 7f, -24.2f), new Vector3(2.4f, 12, 1.6f), obsidianM, 0.1f);
            B(arrival, "Frame_Top", new Vector3(0, 12.2f, -24.2f), new Vector3(15.4f, 1.6f, 1.6f), obsidianM, 0.1f);
            Glow(arrival, "Frame_Light", new Vector3(0, 11.3f, -23.35f), new Vector3(10.6f, 0.08f, 0.08f));
            var st = B(arrival, "Stairs_Down", new Vector3(0, 0, -18f), new Vector3(8f, 2f, 5.6f), graphiteM, 0.02f, NeonBlock.Shape.Stairs);
            st.transform.localRotation = Quaternion.Euler(0, 180, 0); st.steps = 10; Rebuild(st);

            // ---------------- MAIN HALL ----------------
            var hall = Group("02_Hall");
            B(hall, "Floor_Main", new Vector3(4.75f, -0.25f, -6f), new Vector3(34.5f, 0.5f, 36), floorM, 0.02f);
            B(hall, "Floor_West", new Vector3(-19.25f, -0.25f, -6f), new Vector3(7.5f, 0.5f, 36), floorM, 0.02f);
            B(hall, "Wall_W", new Vector3(-23.5f, 7f, -6f), new Vector3(1, 14, 36), trimM, 0.06f);
            B(hall, "Wall_E", new Vector3(22.5f, 7f, -10f), new Vector3(1, 14, 28), trimM, 0.06f);
            B(hall, "Wall_E_North", new Vector3(22.5f, 9f, 9.5f), new Vector3(1, 10, 5), trimM, 0.06f);   // window gap to the service corridor below y=4
            B(hall, "Wall_SW", new Vector3(-16.25f, 7f, -24.5f), new Vector3(13.5f, 14, 1), obsidianM, 0.06f);
            B(hall, "Wall_SE", new Vector3(15.75f, 7f, -24.5f), new Vector3(12.5f, 14, 1), obsidianM, 0.06f);
            // reactor housing: the landmark and big cover in the middle
            B(hall, "Reactor", new Vector3(0, 3.5f, -4f), new Vector3(6, 7, 6), obsidianM, 0.12f);
            B(hall, "Reactor_Cap", new Vector3(0, 7.4f, -4f), new Vector3(4, 0.8f, 4), metalM, 0.06f);
            Glow(hall, "Reactor_Seam_N", new Vector3(0, 3.5f, -0.96f), new Vector3(0.12f, 6.2f, 0.08f));
            Glow(hall, "Reactor_Seam_S", new Vector3(0, 3.5f, -7.04f), new Vector3(0.12f, 6.2f, 0.08f));
            // intentional cover
            B(hall, "Cover_A", new Vector3(-7f, 0.6f, -12f), new Vector3(4, 1.2f, 1), graphiteM, 0.05f);
            B(hall, "Cover_B", new Vector3(8f, 0.6f, -11f), new Vector3(4, 1.2f, 1), graphiteM, 0.05f);
            B(hall, "Cover_C", new Vector3(-8f, 0.6f, 2f), new Vector3(1, 1.2f, 4), graphiteM, 0.05f);
            B(hall, "Cover_D", new Vector3(9f, 0.6f, 3f), new Vector3(1, 1.2f, 4), graphiteM, 0.05f);
            B(hall, "Machine_W", new Vector3(-6f, 1.6f, -18f), new Vector3(3, 3.2f, 2.2f), metalM, 0.05f);
            B(hall, "Machine_E", new Vector3(6f, 1.6f, -18.5f), new Vector3(3, 3.2f, 2.2f), metalM, 0.05f);
            foreach (var p in new[] { new Vector2(-10.5f, -14), new Vector2(10.5f, -14), new Vector2(-10.5f, 8), new Vector2(10.5f, 8) })
                B(hall, "Column", new Vector3(p.x, 6f, p.y), new Vector3(1.6f, 12, 1.6f), obsidianM, 0.08f);
            // east catwalk (upper layer) + stairs
            B(hall, "Catwalk_E", new Vector3(19.5f, 4.25f, -4f), new Vector3(5, 0.5f, 28), floorM, 0.03f);
            B(hall, "Catwalk_E_Rail", new Vector3(17.15f, 5.05f, -2f), new Vector3(0.3f, 1.1f, 24), metalM, 0.03f);
            var se = B(hall, "Stairs_E", new Vector3(19.5f, 0, -21f), new Vector3(3f, 4.5f, 8f), graphiteM, 0.02f, NeonBlock.Shape.Stairs);
            se.steps = 18; Rebuild(se);
            // west catwalk + stairs
            B(hall, "Catwalk_W", new Vector3(-20.5f, 4.25f, -2f), new Vector3(5, 0.5f, 24), floorM, 0.03f);
            B(hall, "Catwalk_W_Rail", new Vector3(-17.85f, 5.05f, -2f), new Vector3(0.3f, 1.1f, 24), metalM, 0.03f);
            var sw = B(hall, "Stairs_W", new Vector3(-20.5f, 0, -22.5f), new Vector3(3f, 4.5f, 8f), graphiteM, 0.02f, NeonBlock.Shape.Stairs);
            sw.steps = 18; Rebuild(sw);
            // bridge across the hall (overwatch on the vault entrance)
            B(hall, "Bridge", new Vector3(-0.5f, 4.25f, 9f), new Vector3(35, 0.5f, 3), floorM, 0.03f);
            B(hall, "Bridge_Rail_S", new Vector3(-0.5f, 5.05f, 7.6f), new Vector3(30, 1.1f, 0.25f), metalM, 0.03f);
            B(hall, "Catwalk_W_Link", new Vector3(-20.5f, 4.25f, 11f), new Vector3(5, 0.5f, 2), floorM, 0.03f);

            // ---------------- LOWER TRENCH (stealth route, west) ----------------
            var trench = Group("03_Trench");
            B(trench, "Trench_Floor", new Vector3(-14f, -3.75f, -1f), new Vector3(3, 0.5f, 30), graphiteM, 0.02f);
            B(trench, "Trench_Wall_W", new Vector3(-15.75f, -1.75f, -1f), new Vector3(0.5f, 3.5f, 30), obsidianM, 0.03f);
            B(trench, "Trench_Wall_E", new Vector3(-12.25f, -1.75f, -1f), new Vector3(0.5f, 3.5f, 30), obsidianM, 0.03f);
            var ts = B(trench, "Trench_Stairs_S", new Vector3(-14f, -3.5f, -16f), new Vector3(3f, 3.5f, 6f), graphiteM, 0.02f, NeonBlock.Shape.Stairs);
            ts.transform.localRotation = Quaternion.Euler(0, 180, 0); ts.steps = 14; Rebuild(ts);
            var tn = B(trench, "Trench_Stairs_N", new Vector3(-14f, -3.5f, 14f), new Vector3(3f, 3.5f, 6f), graphiteM, 0.02f, NeonBlock.Shape.Stairs);
            tn.steps = 14; Rebuild(tn);
            B(trench, "Trench_Cover_1", new Vector3(-14f, -0.25f, -3f), new Vector3(3.6f, 0.5f, 5), floorM, 0.02f);
            B(trench, "Trench_Cover_2", new Vector3(-14f, -0.25f, 7f), new Vector3(3.6f, 0.5f, 5), floorM, 0.02f);
            // the trench cuts the main floor: carve by splitting the slab (west slab ends at -15.5, main starts at -12.5)
            Object.DestroyImmediate(GameObject.Find("Floor_West"));
            B(hall, "Floor_West", new Vector3(-19.75f, -0.25f, -6f), new Vector3(7.5f, 0.5f, 36), floorM, 0.02f);
            Object.DestroyImmediate(GameObject.Find("Floor_Main"));
            B(hall, "Floor_Main", new Vector3(5f, -0.25f, -6f), new Vector3(35f, 0.5f, 36), floorM, 0.02f);
            B(hall, "Floor_TrenchEnds", new Vector3(-14f, -0.25f, -21.5f), new Vector3(3f, 0.5f, 5f), floorM, 0.02f);

            // ---------------- CORE VAULT (north) ----------------
            var vault = Group("04_Vault");
            B(vault, "Vault_Floor", new Vector3(0, -0.25f, 23f), new Vector3(30, 0.5f, 22), floorM, 0.02f);
            B(vault, "Vault_Wall_N", new Vector3(0, 8f, 34.5f), new Vector3(32, 16, 1), obsidianM, 0.08f);
            B(vault, "Vault_Wall_W", new Vector3(-15.5f, 8f, 23f), new Vector3(1, 16, 24), trimM, 0.06f);
            B(vault, "Vault_Wall_E_S", new Vector3(15.5f, 8f, 15.5f), new Vector3(1, 16, 9), trimM, 0.06f);
            B(vault, "Vault_Wall_E_N", new Vector3(15.5f, 8f, 30.5f), new Vector3(1, 16, 9), trimM, 0.06f);
            B(vault, "Vault_Wall_E_Top", new Vector3(15.5f, 11f, 23f), new Vector3(1, 10, 6), trimM, 0.06f);   // east door below
            B(vault, "Vault_Front_W", new Vector3(-10.75f, 8f, 11.5f), new Vector3(10.5f, 16, 1), obsidianM, 0.08f);
            B(vault, "Vault_Front_E", new Vector3(10.75f, 8f, 11.5f), new Vector3(10.5f, 16, 1), obsidianM, 0.08f);
            B(vault, "Vault_Front_Top", new Vector3(0, 12.5f, 11.5f), new Vector3(11, 7, 1), obsidianM, 0.08f);
            Glow(vault, "Vault_Arch_Light", new Vector3(0, 8.95f, 10.95f), new Vector3(10.6f, 0.1f, 0.1f));
            B(vault, "Vault_Ceiling", new Vector3(0, 16.25f, 23f), new Vector3(32, 0.5f, 24), obsidianM, 0.04f).paintable = false;
            // pedestal + cage
            B(vault, "Pedestal", new Vector3(0, 0.6f, 23f), new Vector3(4.2f, 1.2f, 4.2f), metalM, 0.15f);
            B(vault, "Pedestal_Ring", new Vector3(0, 1.25f, 23f), new Vector3(5.2f, 0.1f, 5.2f), obsidianM, 0.04f);
            // balcony (upper vault) + stairs
            B(vault, "Balcony", new Vector3(0, 4.75f, 31.5f), new Vector3(28, 0.5f, 5), floorM, 0.03f);
            B(vault, "Balcony_Rail", new Vector3(0, 5.55f, 29.15f), new Vector3(24, 1.1f, 0.3f), metalM, 0.03f);
            var vs = B(vault, "Vault_Stairs", new Vector3(12.5f, 0, 19f), new Vector3(3f, 5f, 9.5f), graphiteM, 0.02f, NeonBlock.Shape.Stairs);
            vs.steps = 20; Rebuild(vs);
            // community art wall frame
            B(vault, "ArtWall_Frame", new Vector3(0, 10.5f, 33.9f), new Vector3(10, 6, 0.3f), metalM, 0.06f).paintable = false;

            // ---------------- SERVICE CORRIDOR + FINALITY GATE (east) ----------------
            var gateG = Group("05_Gate");
            B(gateG, "Corridor_Floor_A", new Vector3(20.5f, -0.25f, 23f), new Vector3(9, 0.5f, 6), floorM, 0.02f);
            B(gateG, "Corridor_Floor_B", new Vector3(27f, -0.25f, 13f), new Vector3(6, 0.5f, 26), floorM, 0.02f);
            B(gateG, "Corridor_Wall_N", new Vector3(22.5f, 4f, 26.5f), new Vector3(14, 8, 1), obsidianM, 0.05f);
            B(gateG, "Corridor_Wall_E", new Vector3(30.5f, 4f, 13f), new Vector3(1, 8, 28), trimM, 0.05f);
            B(gateG, "Corridor_Wall_W", new Vector3(23.5f, 4f, 9.5f), new Vector3(1, 8, 19), obsidianM, 0.05f);
            B(gateG, "Corridor_Ceiling", new Vector3(26.5f, 8.25f, 15f), new Vector3(9, 0.5f, 24), obsidianM, 0.04f).paintable = false;
            B(gateG, "Gate_Plaza", new Vector3(27f, -0.25f, -5f), new Vector3(8, 0.5f, 10), floorM, 0.02f);
            B(gateG, "Plaza_Wall_E", new Vector3(31.5f, 7f, -5f), new Vector3(1, 14, 12), obsidianM, 0.06f);
            B(gateG, "Plaza_Wall_W", new Vector3(22.95f, 7f, -5f), new Vector3(0.1f, 14, 12), obsidianM, 0.0f).gameObject.SetActive(false);
            B(gateG, "Gate_Frame_L", new Vector3(23.6f, 7f, -10.4f), new Vector3(2.4f, 14, 1.6f), obsidianM, 0.1f);
            B(gateG, "Gate_Frame_R", new Vector3(30.4f, 7f, -10.4f), new Vector3(2.4f, 14, 1.6f), obsidianM, 0.1f);
            B(gateG, "Gate_Frame_Top", new Vector3(27f, 12.5f, -10.4f), new Vector3(9.2f, 3, 1.6f), obsidianM, 0.1f);
            var doorL = B(gateG, "Gate_Door_L", new Vector3(25.6f, 5.5f, -10.4f), new Vector3(2.8f, 11, 0.8f), metalM, 0.05f);
            var doorR = B(gateG, "Gate_Door_R", new Vector3(28.4f, 5.5f, -10.4f), new Vector3(2.8f, 11, 0.8f), metalM, 0.05f);
            doorL.paintable = doorR.paintable = false; Rebuild(doorL); Rebuild(doorR);
            var gateStripL = Glow(gateG, "Gate_Strip_L", new Vector3(24.85f, 5.5f, -9.55f), new Vector3(0.1f, 10f, 0.1f));
            var gateStripR = Glow(gateG, "Gate_Strip_R", new Vector3(29.15f, 5.5f, -9.55f), new Vector3(0.1f, 10f, 0.1f));
            B(gateG, "Beyond_Floor", new Vector3(27f, -0.25f, -14f), new Vector3(8, 0.5f, 6), floorM, 0.02f);
            B(gateG, "Beyond_Back", new Vector3(27f, 7f, -17.5f), new Vector3(10, 14, 1), obsidianM, 0.06f);

            // ---------------- gameplay objects ----------------
            var relayA = MakeRelay(gameplay, "Relay_A", new Vector3(21.9f, 6.6f, -2f), Quaternion.Euler(0, -90, 0), SignatureRecorder.Milestone.RelayA);
            var relayB = MakeRelay(gameplay, "Relay_B", new Vector3(-15.4f, -1.9f, 7f), Quaternion.Euler(0, 90, 0), SignatureRecorder.Milestone.RelayB);
            var relayC = MakeRelay(gameplay, "Relay_C", new Vector3(0f, 7.2f, 33.9f), Quaternion.Euler(0, 180, 0), SignatureRecorder.Milestone.RelayC);
            var core = MakeCore(gameplay, new Vector3(0, 2.4f, 23f));
            var gate = MakeGate(gameplay, doorL.transform, doorR.transform, new[] { gateStripL, gateStripR });

            var cps = new List<Transform>();
            foreach (var (n, p, y) in new[] { ("CP_Arrival", new Vector3(0, 2.05f, -31f), 0f), ("CP_Hall", new Vector3(0, 0.05f, -15f), 0f), ("CP_VaultDoor", new Vector3(0, 0.05f, 9f), 0f), ("CP_Corridor", new Vector3(27f, 0.05f, 16f), 180f) })
            { var t = new GameObject(n).transform; t.SetParent(gameplay); t.position = p; t.rotation = Quaternion.Euler(0, y, 0); cps.Add(t); }

            // lighting
            Lights();

            // director + recorder
            var sys = GameObject.Find("_Systems");
            var director = sys.GetComponent<HeistDirector>() ? sys.GetComponent<HeistDirector>() : sys.AddComponent<HeistDirector>();
            var recorder = sys.GetComponent<SignatureRecorder>() ? sys.GetComponent<SignatureRecorder>() : sys.AddComponent<SignatureRecorder>();
            recorder.mapBounds = new Bounds(new Vector3(0, 5, 0), new Vector3(72, 20, 72));
            var player = GameObject.Find("Player");
            director.player = player.GetComponent<PlayerMotor>();
            director.relays = new[] { relayA, relayB, relayC };
            director.core = core; director.gate = gate; director.recorder = recorder;
            director.checkpoints = cps.ToArray();

            player.transform.position = cps[0].position;
            var target = GameObject.Find("PaintTarget"); if (target) Object.DestroyImmediate(target);

            // navmesh surface over the architecture
            var nav = arch.gameObject.AddComponent<NavMeshSurface>();
            nav.collectObjects = CollectObjects.All;
            nav.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            nav.layerMask = 1 << 0;

            NeonBlockTools.RebuildDirtyBlocks();
            string atlas = NeonBlockTools.BakePaintAtlas();
            nav.BuildNavMesh();
            if (!Directory.Exists("Assets/NeonProtocol/Generated/TheFracture")) Directory.CreateDirectory("Assets/NeonProtocol/Generated/TheFracture");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == ScenePath);
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            return "created " + ScenePath + " | " + atlas;
        }

        // ---------- helpers ----------
        static Material Load(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

        static Material GlowMaterial(string n, Color hdr)
        {
            var path = $"{MatDir}/{n}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;
            m = new Material(Shader.Find("NeonProtocol/Surface")) { name = n };
            m.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.03f)); m.SetColor("_EmissionColor", hdr); m.SetFloat("_SeamGlow", 0);
            m.SetFloat("_TrimSpacing", 0.0001f); m.SetFloat("_TrimWidth", 10f);   // whole surface emissive via the trim band
            m.DisableKeyword("_NP_PAINT"); m.SetFloat("_Paintable", 0); m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Transform Group(string n) { var g = new GameObject(n).transform; g.SetParent(arch); return g; }

        static NeonBlock B(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, float chamfer, NeonBlock.Shape shape = NeonBlock.Shape.Box)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.position = pos;
            go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var b = go.AddComponent<NeonBlock>(); b.shape = shape; b.size = size; b.chamfer = chamfer;
            NeonBlockTools.Rebuild(b);
            return b;
        }

        static void Rebuild(NeonBlock b) => NeonBlockTools.Rebuild(b);

        static Renderer Glow(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            var b = B(parent, name, pos, size, glowM, 0f);
            b.paintable = false; b.addCollider = false; Rebuild(b);
            var c = b.GetComponent<Collider>(); if (c) Object.DestroyImmediate(c);
            b.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return b.GetComponent<Renderer>();
        }

        static Relay MakeRelay(Transform parent, string name, Vector3 pos, Quaternion rot, SignatureRecorder.Milestone ms)
        {
            var root = new GameObject(name).transform; root.SetParent(parent); root.SetPositionAndRotation(pos, rot);
            var housing = B(root, "Housing", pos, new Vector3(1.6f, 1.6f, 0.4f), metalM, 0.06f);
            housing.transform.rotation = rot; housing.paintable = false; Rebuild(housing);
            var coreB = B(root, "Core", pos + rot * new Vector3(0, 0, 0.25f), new Vector3(0.7f, 0.7f, 0.2f), glassM, 0.08f);
            coreB.transform.rotation = rot * Quaternion.Euler(0, 0, 45); coreB.paintable = false; Rebuild(coreB);
            var ringGo = new GameObject("Ring"); ringGo.transform.SetParent(root, false); ringGo.transform.position = pos + rot * new Vector3(0, 0, 0.3f); ringGo.transform.rotation = rot * Quaternion.Euler(90, 0, 0);
            for (int i = 0; i < 4; i++)
            {
                var seg = B(ringGo.transform, "Seg" + i, Vector3.zero, new Vector3(0.9f, 0.06f, 0.08f), glowM, 0f);
                seg.transform.localPosition = Quaternion.Euler(0, i * 90, 0) * new Vector3(0, 0, 0.62f);
                seg.transform.localRotation = Quaternion.Euler(0, i * 90, 0);
                seg.paintable = false; seg.addCollider = false; Rebuild(seg);
                var sc = seg.GetComponent<Collider>(); if (sc) Object.DestroyImmediate(sc);
            }
            var light = new GameObject("Glow").AddComponent<Light>(); light.transform.SetParent(root, false); light.transform.localPosition = new Vector3(0, 0, 1.2f);
            light.type = LightType.Point; light.range = 7f; light.shadows = LightShadows.None;
            var relay = root.gameObject.AddComponent<Relay>();
            relay.coreRenderer = coreB.GetComponent<Renderer>(); relay.ring = ringGo.transform; relay.glow = light; relay.milestone = ms;
            var col = root.gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0, 0, 0.2f); col.size = new Vector3(1.7f, 1.7f, 0.6f);
            relay.audioSource = root.gameObject.AddComponent<AudioSource>(); relay.audioSource.spatialBlend = 1f; relay.audioSource.maxDistance = 35f; relay.audioSource.playOnAwake = false;
            relay.chargeClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Kenney/forceField_000.ogg");
            relay.onlineClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Kenney/forceField_001.ogg");
            return relay;
        }

        static CoreObjective MakeCore(Transform parent, Vector3 pos)
        {
            var root = new GameObject("CoreObjective").transform; root.SetParent(parent); root.position = pos;
            var vis = B(root, "CoreVisual", pos, new Vector3(0.75f, 0.75f, 0.75f), GlowMaterial("M_CoreGlow", new Color(0.4f, 3.2f, 3.0f)), 0.2f);
            vis.transform.rotation = Quaternion.Euler(45, 0, 45); vis.paintable = false; vis.addCollider = false; Rebuild(vis);
            var vc = vis.GetComponent<Collider>(); if (vc) Object.DestroyImmediate(vc);
            var light = new GameObject("CoreLight").AddComponent<Light>(); light.transform.SetParent(vis.transform, false);
            light.type = LightType.Point; light.range = 9f; light.color = new Color(0.3f, 1f, 0.95f); light.shadows = LightShadows.None;
            var cage = new GameObject("Cage").transform; cage.SetParent(root, false); cage.position = pos;
            for (int i = 0; i < 6; i++)
            {
                var bar = B(cage, "Bar" + i, Vector3.zero, new Vector3(0.12f, 3.2f, 0.12f), metalM, 0.02f);
                bar.transform.position = pos + Quaternion.Euler(0, i * 60, 0) * new Vector3(0, 0, 1.3f);
                bar.paintable = false; Rebuild(bar);
            }
            var top = B(cage, "CageTop", pos + Vector3.up * 1.65f, new Vector3(2.9f, 0.15f, 2.9f), metalM, 0.04f); top.paintable = false; Rebuild(top);
            var co = root.gameObject.AddComponent<CoreObjective>();
            co.coreVisual = vis.transform; co.cage = cage; co.coreLight = light;
            co.audioSource = root.gameObject.AddComponent<AudioSource>(); co.audioSource.spatialBlend = 0.5f; co.audioSource.playOnAwake = false;
            co.unlockClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Kenney/doorOpen_000.ogg");
            co.pickupClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Kenney/forceField_001.ogg");
            return co;
        }

        static ExtractionGate MakeGate(Transform parent, Transform l, Transform r, Renderer[] strips)
        {
            var root = new GameObject("FinalityGate").transform; root.SetParent(parent); root.position = new Vector3(27f, 0, -10.4f);
            l.SetParent(root, true); r.SetParent(root, true);
            var g = root.gameObject.AddComponent<ExtractionGate>();
            g.leftDoor = l; g.rightDoor = r; g.energyRenderers = strips; g.openDistance = 2.9f;
            var light = new GameObject("GateLight").AddComponent<Light>(); light.transform.SetParent(root, false); light.transform.localPosition = new Vector3(0, 6f, 2f);
            light.type = LightType.Point; light.range = 16f; light.shadows = LightShadows.None; g.gateLight = light;
            var trig = new GameObject("ExtractTrigger").AddComponent<BoxCollider>(); trig.transform.SetParent(root, false);
            trig.transform.localPosition = new Vector3(0, 1.5f, -3.5f); trig.size = new Vector3(6f, 3f, 3f); trig.isTrigger = true; trig.gameObject.layer = 12;
            trig.gameObject.AddComponent<TriggerRelay>().target = g;
            g.extractTrigger = trig;
            g.audioSource = root.gameObject.AddComponent<AudioSource>(); g.audioSource.spatialBlend = 0.6f; g.audioSource.playOnAwake = false;
            g.openClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Kenney/doorOpen_000.ogg");
            return g;
        }

        static void Lights()
        {
            var lights = new GameObject("Lighting").transform;
            var sun = new GameObject("MoonLight").AddComponent<Light>(); sun.transform.SetParent(lights);
            sun.type = LightType.Directional; sun.color = new Color(0.62f, 0.66f, 1f); sun.intensity = 0.9f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(62f, -30f, 0);
            void P(string n, Vector3 p, Color c, float i, float r) { var l = new GameObject(n).AddComponent<Light>(); l.transform.SetParent(lights); l.transform.position = p; l.type = LightType.Point; l.color = c; l.intensity = i; l.range = r; l.shadows = LightShadows.None; }
            Color violet = new Color(0.55f, 0.25f, 1f), cyan = new Color(0.2f, 0.85f, 1f), cool = new Color(0.55f, 0.6f, 1f), warm = new Color(1f, 0.55f, 0.35f);
            P("Arrival_Cool", new Vector3(0, 8, -30), cool, 8, 18);
            P("Frame_Violet", new Vector3(0, 10, -22), violet, 10, 16);
            P("Hall_Center", new Vector3(0, 11, -10), cool, 12, 30);
            P("Reactor_Warm", new Vector3(0, 9, -4), warm, 6, 12);
            P("Hall_NE_Cyan", new Vector3(16, 8, 4), cyan, 9, 18);
            P("Hall_NW_Violet", new Vector3(-16, 8, 4), violet, 9, 18);
            P("Hall_S_Violet", new Vector3(0, 6, -20), violet, 6, 14);
            P("Trench_Dim", new Vector3(-14, -1.5f, -8), new Color(0.35f, 0.15f, 0.8f), 3, 9);
            P("Trench_Dim2", new Vector3(-14, -1.5f, 2), new Color(0.35f, 0.15f, 0.8f), 2.5f, 9);
            P("Vault_Center", new Vector3(0, 12, 23), cool, 10, 24);
            P("Vault_Cyan", new Vector3(-10, 6, 28), cyan, 7, 16);
            P("Vault_Violet", new Vector3(10, 6, 16), violet, 7, 16);
            P("Corridor_A", new Vector3(26, 6, 20), violet, 5, 14);
            P("Corridor_B", new Vector3(27, 6, 6), cyan, 5, 14);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.2f, 0.19f, 0.32f);
            RenderSettings.ambientEquatorColor = new Color(0.11f, 0.09f, 0.16f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.035f, 0.06f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.014f; RenderSettings.fogColor = new Color(0.025f, 0.02f, 0.05f);
            var probe = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>(); probe.transform.SetParent(lights); probe.transform.position = new Vector3(0, 5, 0);
            probe.mode = ReflectionProbeMode.Baked; probe.size = new Vector3(72, 24, 72); probe.boxProjection = false; probe.resolution = 128;
        }
    }

}
