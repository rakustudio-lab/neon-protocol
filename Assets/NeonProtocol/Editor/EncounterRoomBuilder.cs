using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Encounter reset (NEON_NEXT/release/encounter-reset/layout.json): one playable floor inside the R2 footprint
    // (origin -300,0,0; x -10..10, z 0..28). Opaque entry baffle hides guard and relay from the spawn; a chamfered
    // reactor mass splits the room into a direct peek route (right, low cover) and a flank (left, tall retreat cover);
    // one ground-level HALO behind low guard cover; the Iris on a pylon in a protected pocket; the core door ahead.
    // Visible geometry and collision are the same boxes. Lighting: baked warm key + cool fill (contact shadows),
    // light probes for characters, one restrained cyan accent on the reactor. Replaces the R2 RelayRoom (kept in git).
    public static class EncounterRoomBuilder
    {
        public static readonly Vector3 O = new Vector3(-300f, 0f, 0f);
        const string K = QuaterniusSetup.Kit + "/";
        const float X0 = -10f, X1 = 10f, Z0 = 0f, Z1 = 28f, H = 4.43f, F = 0.09f;
        static Transform root, art, col, gameplay, lights;
        static Material main, light, recess, violet, amber, cyan, floorM;

        static GameObject Piece(string n, Vector3 p, float yaw, Vector3? stretch = null, Transform parent = null)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(K + n + ".fbx");
            if (!src) { Debug.LogWarning("kit piece missing " + n); return null; }
            var g = (GameObject)PrefabUtility.InstantiatePrefab(src, parent ? parent : art);
            g.transform.localPosition = p; g.transform.localRotation = Quaternion.Euler(0, yaw, 0) * src.transform.localRotation;
            if (stretch.HasValue)
            {   // stretch in WORLD axes after the import rotation (root is x100, Blender Z-up)
                var s = stretch.Value; var ls = src.transform.localScale;
                Vector3 wx = g.transform.InverseTransformDirection(Quaternion.Euler(0, yaw, 0) * Vector3.right);
                Vector3 wy = g.transform.InverseTransformDirection(Vector3.up);
                Vector3 local = new Vector3(1, 1, 1);
                for (int a = 0; a < 3; a++)
                {
                    float fx = Mathf.Abs(wx[a]), fy = Mathf.Abs(wy[a]);
                    local[a] = fx > 0.9f ? s.x : fy > 0.9f ? s.y : s.z;
                }
                g.transform.localScale = Vector3.Scale(ls, local);
            }
            Statics(g);
            return g;
        }
        static void Statics(GameObject g)
        {
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveGI = ReceiveGI.Lightmaps;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
        }
        static GameObject Slab(string n, Vector3 c, Vector3 s, Material m, Transform parent = null, bool collide = false)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n;
            if (!collide) Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent ? parent : art, false); g.transform.localPosition = c; g.transform.localScale = s;
            g.GetComponent<MeshRenderer>().sharedMaterial = m; Statics(g);
            if (collide) GameObjectUtility.SetStaticEditorFlags(g, GameObjectUtility.GetStaticEditorFlags(g) | StaticEditorFlags.NavigationStatic);
            return g;
        }
        static BoxCollider Box(string n, Vector3 center, Vector3 size)
        {
            var g = new GameObject(n); g.transform.SetParent(col, false); g.transform.localPosition = center;
            var b = g.AddComponent<BoxCollider>(); b.size = size; GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.NavigationStatic); return b;
        }
        // a solid block with one collider = exactly its visible volume; kit panels dress its faces
        static GameObject Block(string id, float x0, float x1, float z0, float z1, float h, Material body, bool dressFaces, bool trim = true)
        {
            var c = new Vector3((x0 + x1) / 2f, F + h / 2f, (z0 + z1) / 2f); var s = new Vector3(x1 - x0, h, z1 - z0);
            var g = Slab(id, c, s, body, art, collide: true); g.layer = 0;
            if (trim) { Slab(id + "_Cap", c + Vector3.up * (h / 2f + 0.03f), new Vector3(s.x + 0.08f, 0.06f, s.z + 0.08f), light); Slab(id + "_Line", c + Vector3.up * (h / 2f - 0.14f), new Vector3(s.x + 0.02f, 0.05f, s.z + 0.02f), violet); }
            return g;
        }

        [MenuItem("Neon Protocol/R2/Build Encounter Room (reset layout)")]
        public static string Build()
        {
            foreach (var n in new[] { "RelayRoom", "EncounterRoom" }) { var old = GameObject.Find(n); if (old) Object.DestroyImmediate(old); }
            root = new GameObject("EncounterRoom").transform; root.position = O;
            art = new GameObject("Art").transform; art.SetParent(root, false);
            col = new GameObject("Colliders").transform; col.SetParent(root, false);
            gameplay = new GameObject("Gameplay").transform; gameplay.SetParent(root, false);
            lights = new GameObject("Lights").transform; lights.SetParent(root, false);
            main = QuaterniusSetup.Palette("Main"); light = QuaterniusSetup.Palette("Light"); recess = QuaterniusSetup.Palette("DarkGrey");
            violet = QuaterniusSetup.Palette("Accent"); amber = QuaterniusSetup.Amber(); cyan = QuaterniusSetup.Cyan(); floorM = QuaterniusSetup.Floor();

            // ---- shell: quiet floor (one material), one-storey kit walls, low light ceiling with light wells ----
            Slab("Floor", new Vector3(0, F / 2f, 14f), new Vector3(20f, F, 28f), floorM);
            for (float z = 4f; z < Z1; z += 4f) Slab("Floor_Seam", new Vector3(0, F + 0.002f, z), new Vector3(20f, 0.004f, 0.05f), QuaterniusSetup.FloorSeam());
            Box("FloorCol", new Vector3(0, -0.4f, 14f), new Vector3(20f, 0.98f, 28f));
            void Run(float fixedC, bool alongX, float yaw, string[] pieces)
            {
                for (int i = 0; i < pieces.Length; i++)
                {
                    float t = (alongX ? X0 : Z0) + 2f + i * 4f;
                    if (pieces[i] != null) Piece(pieces[i], alongX ? new Vector3(t, 0, fixedC) : new Vector3(fixedC, 0, t), yaw);
                }
            }
            Run(Z0, true, 0f, new[] { "Walls/Wall_2", "Walls/Wall_1", "Walls/Wall_5", "Walls/Wall_1", "Walls/Wall_2" });
            Run(Z1, true, 180f, new[] { "Walls/Wall_3", "Walls/Wall_1", "Walls/DoorDouble_Wall_SideA", "Walls/Wall_1", "Walls/Wall_Empty" });
            var side = new[] { "Walls/Wall_1", "Walls/Wall_Empty", "Walls/Wall_2", "Walls/Wall_1", "Walls/Wall_Empty", "Walls/Wall_3", "Walls/Wall_1" };
            Run(X0, false, 90f, side); Run(X1, false, -90f, side);
            for (float z = Z0; z <= Z1 + 0.01f; z += 4f) foreach (float x in new[] { X0 + 0.2f, X1 - 0.2f }) Piece("Column_1", new Vector3(x, 0, z), 0f);
            Box("Wall_S", new Vector3(0, H / 2f, Z0 - 0.35f), new Vector3(22f, H + 1f, 0.7f)); Box("Wall_N", new Vector3(0, H / 2f, Z1 + 0.35f), new Vector3(22f, H + 1f, 0.7f));
            Box("Wall_W", new Vector3(X0 - 0.35f, H / 2f, 14f), new Vector3(0.7f, H + 1f, 30f)); Box("Wall_E", new Vector3(X1 + 0.35f, H / 2f, 14f), new Vector3(0.7f, H + 1f, 30f));
            // ceiling plane with two light wells (warm key falls through them)
            Slab("Ceiling_A", new Vector3(0, H + 0.1f, 4.5f), new Vector3(20f, 0.2f, 9f), light);
            Slab("Ceiling_B", new Vector3(-6f, H + 0.1f, 14.5f), new Vector3(8f, 0.2f, 11f), light);
            Slab("Ceiling_C", new Vector3(6.5f, H + 0.1f, 17.5f), new Vector3(7f, 0.2f, 17f), light);
            Slab("Ceiling_D", new Vector3(-3.5f, H + 0.1f, 25f), new Vector3(13f, 0.2f, 6f), light);
            Slab("Well_Key_A", new Vector3(1f, H + 0.7f, 11.5f), new Vector3(6f, 0.1f, 5f), QuaterniusSetup.CeilingPanel());   // over the reveal/peek
            Slab("Well_Key_B", new Vector3(-3.5f, H + 0.7f, 20.5f), new Vector3(13f, 0.1f, 3f), QuaterniusSetup.CeilingPanel()); // over the flank/relay pocket
            foreach (var (c, s) in new[] { (new Vector3(1f, H + 0.45f, 9f), new Vector3(6f, 0.7f, 0.2f)), (new Vector3(1f, H + 0.45f, 14f), new Vector3(6f, 0.7f, 0.2f)), (new Vector3(-2f, H + 0.45f, 11.5f), new Vector3(0.2f, 0.7f, 5f)), (new Vector3(4f, H + 0.45f, 11.5f), new Vector3(0.2f, 0.7f, 5f)),
                (new Vector3(-3.5f, H + 0.45f, 19f), new Vector3(13f, 0.7f, 0.2f)), (new Vector3(-3.5f, H + 0.45f, 22f), new Vector3(13f, 0.7f, 0.2f)) })
                Slab("Well_Side", c, s, recess);
            Box("CeilingCol", new Vector3(0, H + 0.6f, 14f), new Vector3(22f, 0.8f, 30f));

            // ---- solids from layout.json (visual == collision) ----
            // EntryBaffle x -10..3 z 6.5..7.3 h 3.6: two kit wall runs back to back, solid
            // full height (opaque to the ceiling); unstretched kit modules on both faces + a column cap at the open end
            Block("EntryBaffle", -10f, 3f, 6.5f, 7.3f, H - F, main, false, trim: false);
            for (int i = 0; i < 3; i++) { float x = -7.5f + i * 4f; Piece("Walls/Wall_2", new Vector3(x, 0, 6.25f), 180f); Piece("Walls/Wall_1", new Vector3(x, 0, 7.55f), 0f); }
            Piece("Column_1", new Vector3(2.75f, 0, 6.9f), 0f);
            // ReactorMass x -2..2 z 11..18.4 h 3.6: chamfered housing, recessed machinery, one restrained cyan light
            Block("ReactorMass", -2f, 2f, 10.7f, 18.7f, 3.6f, recess, false, trim: false);   // 8 m: two whole wall modules per side
            Slab("Reactor_Plinth", new Vector3(0, F + 0.12f, 14.7f), new Vector3(5.0f, 0.24f, 8.9f), main);
            foreach (float sx in new[] { -1f, 1f })
            {
                Piece("Walls/Wall_4", new Vector3(sx * 2.25f, 0f, 12.7f), sx > 0 ? 90f : -90f);   // detailed face outward
                Piece("Walls/Wall_2", new Vector3(sx * 2.25f, 0f, 16.7f), sx > 0 ? 90f : -90f);
                var ch = Slab("Reactor_Chamfer", new Vector3(sx * 1.55f, F + 3.6f + 0.15f, 14.7f), new Vector3(1.4f, 0.3f, 7.6f), main); ch.transform.localRotation = Quaternion.Euler(0, 0, sx * 32f);
            }
            Piece("Walls/Wall_5", new Vector3(0, 0f, 10.45f), 180f);
            Piece("Walls/Wall_3", new Vector3(0, 0f, 18.95f), 0f);
            Slab("Reactor_Top", new Vector3(0, F + 3.6f + 0.2f, 14.7f), new Vector3(2.0f, 0.25f, 7.6f), light);
            Piece("Props_Teleporter_1", new Vector3(0, F + 3.95f, 14.7f), 0f);
            Slab("Reactor_Glow", new Vector3(0, F + 2.2f, 10.0f), new Vector3(1.6f, 0.08f, 0.04f), cyan);
            // cover: low right peek, tall left retreat, low guard cover, relay screen
            Block("RightPeekCover", 4.3f, 7.2f, 13f, 13.8f, 1.2f, main, true);   // layout said 1.35: Unity AI-ray test showed only ~2 cm clearance for a standing peek
            Piece("Details/Details_Vent_2", new Vector3(5.75f, F + 0.55f, 12.96f), 180f);
            Block("LeftRetreatCover", -7.5f, -5.5f, 16f, 17.2f, 2.5f, main, true);
            Piece("Details/Details_Plate_Large", new Vector3(-6.5f, F + 1.4f, 15.96f), 180f);
            Block("GuardCover", 3.8f, 6.8f, 19.7f, 20.5f, 1.2f, main, true);
            Block("RelayScreen", 0.5f, 2.5f, 22f, 26f, H - F, main, false, trim: false);
            Piece("Walls/Wall_1", new Vector3(0.25f, 0f, 24f), -90f);
            Piece("Walls/Wall_3", new Vector3(2.75f, 0f, 24f), 90f);
            // a few honest props in corners (not on the routes)
            foreach (var (n, p, y) in new[] { ("Props_Shelf", new Vector3(-9.2f, F, 2f), 90f), ("Props_ComputerSmall", new Vector3(9.2f, F, 2.5f), -90f), ("Props_Vessel_Tall", new Vector3(-9.1f, F, 26.8f), 0f), ("Props_Pod", new Vector3(9f, F, 26.5f), -90f) })
            {
                var g = Piece(n, p, y); if (!g) continue;
                var rs = g.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                Box("Prop_" + n, b.center - O, b.size);
            }
            // amber at turns only: one chevron at the first turn, one at the breach
            foreach (var (p, yaw) in new[] { (new Vector3(5.2f, F + 0.01f, 4.4f), 90f), (new Vector3(6f, F + 0.01f, 8.6f), 0f) })
            {
                var c = new GameObject("Chevron"); c.transform.SetParent(art, false); c.transform.localPosition = p; c.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                foreach (float sgn in new[] { -1f, 1f }) { var a = Slab("Arm", Vector3.zero, new Vector3(0.1f, 0.02f, 0.65f), amber, c.transform); a.transform.localPosition = new Vector3(sgn * 0.22f, 0, -0.1f); a.transform.localRotation = Quaternion.Euler(0, -sgn * 40f, 0); }
            }

            // ---- lighting: baked warm key through the wells, cool fill, contact shadows; probes for characters ----
            Light Area(string n, Vector3 p, Vector2 size, Color c, float intensity)
            {
                var g = new GameObject(n); g.transform.SetParent(lights, false); g.transform.localPosition = p; g.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                var l = g.AddComponent<Light>(); l.type = LightType.Rectangle; l.areaSize = size; l.color = c; l.intensity = intensity; l.lightmapBakeType = LightmapBakeType.Baked; l.shadows = LightShadows.Soft; return l;
            }
            Light Point(string n, Vector3 p, Color c, float i, float r, LightmapBakeType mode)
            {
                var g = new GameObject(n); g.transform.SetParent(lights, false); g.transform.localPosition = p;
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = i; l.range = r; l.lightmapBakeType = mode; l.shadows = mode == LightmapBakeType.Baked ? LightShadows.Soft : LightShadows.None; return l;
            }
            Area("Key_Warm_Reveal", new Vector3(1f, H + 0.6f, 11.5f), new Vector2(5.5f, 4.5f), new Color(1f, 0.86f, 0.68f), 9f);
            Area("Key_Warm_Pocket", new Vector3(-3.5f, H + 0.6f, 20.5f), new Vector2(12f, 2.5f), new Color(1f, 0.88f, 0.72f), 7f);
            Area("Fill_Cool_Entry", new Vector3(-2f, H - 0.02f, 3.5f), new Vector2(14f, 5f), new Color(0.78f, 0.84f, 1f), 1.3f);
            Area("Fill_Cool_Back", new Vector3(3f, H - 0.02f, 25f), new Vector2(10f, 4f), new Color(0.62f, 0.72f, 1f), 2.0f);
            Point("Breach_Pull", new Vector3(6f, 2.6f, 8.6f), new Color(1f, 0.75f, 0.45f), 3.5f, 7f, LightmapBakeType.Baked);
            Point("Reactor_Accent", new Vector3(0f, 1.9f, 10.2f), new Color(0.4f, 0.95f, 1f), 1.6f, 4.5f, LightmapBakeType.Baked);
            Point("Relay_Accent", new Vector3(-5.5f, 2.8f, 23.2f), new Color(0.55f, 0.95f, 1f), 2.2f, 5f, LightmapBakeType.Baked);
            var probes = new GameObject("LightProbes").AddComponent<LightProbeGroup>(); probes.transform.SetParent(lights, false);
            var pts = new List<Vector3>(); for (float x = -9f; x <= 9.01f; x += 3f) for (float z = 1f; z <= 27.01f; z += 3f) foreach (float y in new[] { 0.5f, 1.8f, 3.4f }) pts.Add(new Vector3(x, y, z));
            probes.probePositions = pts.ToArray();

            // ---- gameplay: spawn, relay pylon, HALO ground guard, breach trigger, core door ----
            var spawn = new GameObject("CP_RelayRoom").transform; spawn.SetParent(gameplay, false); spawn.localPosition = new Vector3(-6f, 0.12f, 2.2f); spawn.localRotation = Quaternion.Euler(0, 78f, 0);
            Slab("Relay_Pylon", new Vector3(-5.5f, F + 1.5f, 24.95f), new Vector3(1.9f, 3.0f, 0.5f), main, art, collide: true);
            Slab("Relay_PylonTrim", new Vector3(-5.5f, F + 3.05f, 24.95f), new Vector3(2.0f, 0.08f, 0.55f), violet);
            var relayGo = GameObject.Find("Relay_A");
            if (relayGo)
            {
                var iris = relayGo.GetComponent<IrisRelay>();
                relayGo.transform.position = O + new Vector3(-5.5f, 1.75f, 24.62f);
                if (iris && iris.visual) { var f = iris.visual.up; f.y = 0f; relayGo.transform.rotation = Quaternion.Euler(0, Vector3.SignedAngle(f, Vector3.back, Vector3.up), 0) * relayGo.transform.rotation; iris.visual.localScale = Vector3.one * 1.35f; }
                if (iris && iris.pulsePath != null) { var pp = new[] { relayGo.transform.position + Vector3.back * 0.4f, O + new Vector3(-3f, 2.5f, 23f), O + new Vector3(0f, 2.5f, 26.6f) }; for (int i = 0; i < iris.pulsePath.Length; i++) if (iris.pulsePath[i]) iris.pulsePath[i].position = pp[Mathf.Min(i, pp.Length - 1)]; }
            }
            CompetitorAI halo = null; foreach (var a in Object.FindObjectsByType<CompetitorAI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (a.name.Contains("HALO")) halo = a;
            if (halo)
            {
                var post = new GameObject("HALO_GuardPost").transform; post.SetParent(gameplay, false); post.localPosition = new Vector3(5.5f, 0.1f, 21.5f); post.localRotation = Quaternion.Euler(0, 180f, 0);
                var pA = new GameObject("HALO_Patrol_A").transform; pA.SetParent(gameplay, false); pA.localPosition = new Vector3(5.5f, 0.1f, 21.6f);
                var pB = new GameObject("HALO_Patrol_B").transform; pB.SetParent(gameplay, false); pB.localPosition = new Vector3(3.2f, 0.1f, 21.6f);
                halo.dormantPost = post; halo.patrol = new[] { pA, pB }; halo.wakeAt = 99; halo.lessonGuard = true;   // armed by the breach trigger, not by a pad hit
                var th = halo.GetComponent<TagHealth>(); th.hitsToTag = 7; EditorUtility.SetDirty(th);   // a lesson guard: long enough for warn / cover / reacquire to happen
                halo.transform.position = post.position; halo.transform.rotation = post.rotation; EditorUtility.SetDirty(halo);
                var trig = new GameObject("Encounter_Breach").transform; trig.SetParent(gameplay, false); trig.localPosition = new Vector3(4f, 1.2f, 9.2f);
                var tb = trig.gameObject.AddComponent<BoxCollider>(); tb.isTrigger = true; tb.size = new Vector3(13f, 2.6f, 1.2f);
                var arm = trig.gameObject.AddComponent<EncounterArm>(); arm.guard = halo; trig.gameObject.layer = 2;
            }
            // core door (existing RoomExit behaviour), unscaled root
            var exitRoot = new GameObject("Exit").transform; exitRoot.SetParent(gameplay, false); exitRoot.localPosition = new Vector3(0, F, Z1 - 0.62f); exitRoot.localRotation = Quaternion.Euler(0, 180f, 0);
            var doorGo = Piece("Door_Double", Vector3.zero, 0f, null, exitRoot);
            // Animated panels must not be baked into static geometry or occluders.
            foreach (var renderer in doorGo.GetComponentsInChildren<MeshRenderer>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, 0);
                renderer.receiveGI = ReceiveGI.LightProbes;
            }
            var exit = exitRoot.gameObject.AddComponent<RoomExit>(); exit.relay = relayGo ? relayGo.GetComponent<Relay>() : null;
            var leaves = new List<Transform>(); foreach (Transform t in doorGo.transform) leaves.Add(t);
            if (leaves.Count >= 2) { leaves.Sort((a, b) => a.localPosition.x.CompareTo(b.localPosition.x)); exit.leafL = leaves[0]; exit.leafR = leaves[leaves.Count - 1]; }
            var dl = new GameObject("Exit_Light").AddComponent<Light>(); dl.transform.SetParent(gameplay, false); dl.transform.localPosition = new Vector3(0, 3.0f, Z1 - 2.0f); dl.type = LightType.Point; dl.range = 6f; dl.intensity = 2.5f; dl.shadows = LightShadows.None; dl.lightmapBakeType = LightmapBakeType.Realtime; exit.doorLight = dl;
            var sigs = new List<Renderer>(); foreach (float sx in new[] { -1.75f, 1.75f }) sigs.Add(Slab("Exit_Signal", new Vector3(sx, 2.95f, Z1 - 0.75f), new Vector3(0.18f, 0.5f, 0.06f), amber, gameplay).GetComponent<Renderer>());
            exit.signals = sigs.ToArray(); exit.lockedMat = amber; exit.openMat = cyan;
            var et = exitRoot.gameObject.AddComponent<BoxCollider>(); et.isTrigger = true; et.center = new Vector3(0, 1.3f, 0.75f);   // root is turned 180: +z local = the room side et.size = new Vector3(2.4f, 2.6f, 1.0f);
            var block = new GameObject("Exit_Block"); block.transform.SetParent(exitRoot, false); block.transform.localPosition = new Vector3(0, 1.4f, -0.3f);
            var db = block.AddComponent<BoxCollider>(); db.size = new Vector3(2.7f, 2.8f, 0.4f); block.layer = 2; exit.block = db;
            var tFeat = new GameObject("Exit_To_Vault").transform; tFeat.SetParent(gameplay, false); tFeat.position = new Vector3(0f, 0.08f, 9f);
            var tFull = new GameObject("Exit_To_Fracture").transform; tFull.SetParent(gameplay, false); tFull.position = new Vector3(0f, 2.1f, -31f);
            exit.featuredTarget = tFeat; exit.fullTarget = tFull;
            var eText = new GameObject("Exit_Label").AddComponent<TextMeshPro>(); eText.transform.SetParent(gameplay, false); eText.transform.localPosition = new Vector3(0, 3.55f, Z1 - 0.72f); eText.text = "CORE"; eText.fontSize = 3f; eText.alignment = TextAlignmentOptions.Center; eText.color = new Color(1f, 0.85f, 0.6f);
            eText.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/NeonProtocol/Art/Fonts/Orbitron-900 SDF.asset"); eText.rectTransform.sizeDelta = new Vector2(4f, 1f);

            PassageCore();

            var hd = Object.FindFirstObjectByType<HeistDirector>();
            if (hd) { var cps = new List<Transform>(hd.checkpoints); cps.RemoveAll(c => !c || c.name == "CP_RelayRoom"); cps.Insert(0, spawn); hd.checkpoints = cps.ToArray(); EditorUtility.SetDirty(hd); }
            var scene = root.gameObject.scene; EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            int tris = 0; foreach (var mf in root.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) tris += mf.sharedMesh.triangles.Length / 3;
            return $"encounter room: {art.childCount} art, {col.childCount + 9} colliders, {tris} tris, {pts.Count} probes";
        }

        // R4 door fix: the core door used to be a door model on a solid wall (Wall_N collider 0.6 m behind the leaves)
        // with a 1 m trigger at its centre (the size line was commented out), so an open door could not be crossed
        // unless the runner hit that 1 m box. Now: a real opening in the wall collider, a short lit vestibule behind
        // the door, and a full-width transition trigger inside the vestibule (the runner physically walks through).
        // Idempotent; does not touch baked geometry, so it can patch the saved scene without a rebake.
        [MenuItem("Neon Protocol/R2/Patch Core Door Passage")]
        public static string PassageCore()
        {
            root = GameObject.Find("EncounterRoom")?.transform; if (!root) return "no EncounterRoom";
            col = root.Find("Colliders"); gameplay = root.Find("Gameplay"); art = root.Find("Art");
            if (!main) { main = QuaterniusSetup.Palette("Main"); light = QuaterniusSetup.Palette("Light"); recess = QuaterniusSetup.Palette("DarkGrey"); violet = QuaterniusSetup.Palette("Accent"); amber = QuaterniusSetup.Amber(); cyan = QuaterniusSetup.Cyan(); floorM = QuaterniusSetup.Floor(); }
            var old = root.Find("Passage"); if (old) Object.DestroyImmediate(old.gameObject);
            foreach (var n in new[] { "Wall_N", "Wall_N_L", "Wall_N_R", "Wall_N_Lintel" }) { var o = col.Find(n); if (o) Object.DestroyImmediate(o.gameObject); }
            const float gap = 1.25f, D0 = Z1, D1 = Z1 + 4.2f, VH = 3.2f;   // opening half-width (kit door hole ~2.6 m), vestibule end / height
            Box("Wall_N_L", new Vector3((-11f - gap) / 2f, H / 2f, Z1 + 0.35f), new Vector3(11f - gap, H + 1f, 0.7f));
            Box("Wall_N_R", new Vector3((11f + gap) / 2f, H / 2f, Z1 + 0.35f), new Vector3(11f - gap, H + 1f, 0.7f));
            Box("Wall_N_Lintel", new Vector3(0, (VH + H + 1f) / 2f, Z1 + 0.35f), new Vector3(2f * gap, H + 1f - VH, 0.7f));
            var p = new GameObject("Passage").transform; p.SetParent(root, false);
            GameObject Dress(string n, Vector3 c, Vector3 s, Material m, bool collide)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(p, false); g.transform.localPosition = c; g.transform.localScale = s;
                g.GetComponent<MeshRenderer>().sharedMaterial = m;
                if (!collide) Object.DestroyImmediate(g.GetComponent<Collider>());
                else GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);
                return g;
            }
            float zc = (D0 + D1) / 2f, len = D1 - D0;
            Dress("Passage_Floor", new Vector3(0, F / 2f - 0.45f, zc), new Vector3(2f * gap + 0.8f, F + 0.9f, len), floorM, true);
            Dress("Passage_WallL", new Vector3(-gap - 0.2f, VH / 2f, zc), new Vector3(0.4f, VH, len), recess, true);
            Dress("Passage_WallR", new Vector3(gap + 0.2f, VH / 2f, zc), new Vector3(0.4f, VH, len), recess, true);
            Dress("Passage_Ceiling", new Vector3(0, VH + 0.1f, zc), new Vector3(2f * gap + 0.8f, 0.2f, len), light, true);
            Dress("Passage_End", new Vector3(0, VH / 2f, D1 + 0.2f), new Vector3(2f * gap + 0.8f, VH, 0.4f), recess, true);
            Dress("Passage_EndGlow", new Vector3(0, 1.6f, D1 - 0.02f), new Vector3(2f * gap - 0.4f, 2.4f, 0.04f), cyan, false);
            foreach (float sx in new[] { -1f, 1f }) Dress("Passage_Guide", new Vector3(sx * (gap - 0.12f), F + 0.01f, zc), new Vector3(0.06f, 0.02f, len), cyan, false);
            var pl = new GameObject("Passage_Light").AddComponent<Light>(); pl.transform.SetParent(p, false); pl.transform.localPosition = new Vector3(0, 2.6f, zc);
            pl.type = LightType.Point; pl.range = 6f; pl.intensity = 2.2f; pl.color = new Color(0.55f, 0.9f, 1f); pl.shadows = LightShadows.None; pl.lightmapBakeType = LightmapBakeType.Realtime;
            var lab = new GameObject("Passage_Label").AddComponent<TextMeshPro>(); lab.transform.SetParent(p, false); lab.transform.localPosition = new Vector3(0, 2.75f, D1 - 0.06f);
            lab.text = "THE VAULT"; lab.fontSize = 2.4f; lab.alignment = TextAlignmentOptions.Center; lab.color = new Color(0.05f, 0.08f, 0.12f); lab.rectTransform.sizeDelta = new Vector2(3f, 0.6f);
            lab.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/NeonProtocol/Art/Fonts/Orbitron-900 SDF.asset");
            // the transition trigger: the whole vestibule width, world z D0+1.4 .. D0+3.8 (past the door plane). Exit root is
            // at z = Z1-0.62 and turned 180, so local +z points back into the room.
            var exitRoot = gameplay.Find("Exit"); var et = exitRoot ? exitRoot.GetComponent<BoxCollider>() : null;
            if (et) { et.isTrigger = true; et.center = new Vector3(0, 1.3f, -((D0 + 2.6f) - (Z1 - 0.62f))); et.size = new Vector3(2f * gap, 2.6f, 2.4f); EditorUtility.SetDirty(et); }
            var scene = root.gameObject.scene; EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return et ? $"passage ok, trigger world {et.bounds}" : "passage built, NO exit trigger";
        }

        // R4 ghost-wall fix (QA "GW": walk into every visible object on the route): the core room's 16 m pillars had no
        // collider at all, and the encounter's kit wall panels / columns stood up to 0.4 m proud of their box colliders
        // (the runner visibly walked into them). Adds one unscaled box per such object = its visible bounds. Stairs, the
        // core dais and the door wall (contains the door hole) are excluded on purpose. Idempotent.
        [MenuItem("Neon Protocol/R2/Seal Ghost Walls")]
        public static string SealGhosts()
        {
            var old = GameObject.Find("R4_GhostSeal"); if (old) Object.DestroyImmediate(old);
            var rootGo = new GameObject("R4_GhostSeal"); int n = 0; var names = new List<string>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var parent = r.transform.parent ? r.transform.parent.name : ""; var b = r.bounds; var c = b.center;
                bool pillar = parent == "ArtPass_Vault" && r.name == "Pillar";
                bool encPanel = parent == "Art" && c.x > -312 && c.x < -288 && c.z > 0.5f && c.z < 27.5f && (r.name.StartsWith("Wall_") || r.name == "Column_1") && b.min.y < 0.5f;
                if (!pillar && !encPanel) continue;
                var g = new GameObject("Seal_" + r.name); g.transform.SetParent(rootGo.transform, false); g.transform.position = new Vector3(c.x, b.min.y + Mathf.Min(b.size.y, 5f) / 2f, c.z);
                var bc = g.AddComponent<BoxCollider>(); bc.size = new Vector3(b.size.x, Mathf.Min(b.size.y, 5f), b.size.z);
                GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.NavigationStatic); n++;
            }
            var scene = rootGo.scene; EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return "sealed " + n;
        }

        // Bake ONLY this room (it is the only ContributeGI geometry): small lightmaps, soft contact shadows.
        [MenuItem("Neon Protocol/R2/Bake Encounter Lighting")]
        public static string Bake()
        {
            const string lsPath = "Assets/NeonProtocol/Scenes/TheFracture/EncounterLighting.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
            if (!ls) { ls = new LightingSettings(); AssetDatabase.CreateAsset(ls, lsPath); }
            ls.bakedGI = true; ls.realtimeGI = false; ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.lightmapResolution = 14f; ls.lightmapPadding = 2; ls.lightmapMaxSize = 1024; ls.directSampleCount = 48; ls.indirectSampleCount = 512; ls.environmentSampleCount = 256;
            ls.denoiserTypeDirect = LightingSettings.DenoiserType.OpenImage; ls.denoiserTypeIndirect = LightingSettings.DenoiserType.OpenImage; ls.denoiserTypeAO = LightingSettings.DenoiserType.OpenImage;
            ls.maxBounces = 2; ls.ao = true; ls.aoMaxDistance = 0.8f; ls.aoExponentDirect = 0.6f; ls.aoExponentIndirect = 1f; ls.lightmapCompression = LightmapCompression.NormalQuality;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly; ls.directionalityMode = LightmapsMode.NonDirectional;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
            bool ok = Lightmapping.Bake();   // synchronous
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return "bake " + (ok ? "ok" : "FAILED") + ", lightmaps " + LightmapSettings.lightmaps.Length;
        }
    }
}
