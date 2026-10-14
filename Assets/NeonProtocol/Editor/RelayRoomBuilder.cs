using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // R2 "finished room": the Relay Room, built from the CC0 Quaternius Ultimate Modular Sci-Fi kit at x = -300.
    // Closed two-storey hall (20 x 28 m, 8.9 m high): arrival door behind the spawn, open floor with cover,
    // three-flight stairs on both sides to a mezzanine, the Iris relay on the back wall above the mezzanine,
    // HALO's lesson post on the mezzanine, and the EXIT (double door, amber -> cyan) in the recess under it.
    // Real geometry with authored colliders (walls, floor, ceiling, ramps, rails): nothing can leave the room.
    // Re-runnable. Moves Relay_A (gameplay root + Iris) and HALO's lesson post here; the old arrival stays intact.
    public static class RelayRoomBuilder
    {
        public static readonly Vector3 O = new Vector3(-300f, 0f, 0f);
        const string K = QuaterniusSetup.Kit + "/";
        const float W = 4f, H = 4.43f;                 // kit wall module
        const float X0 = -10f, X1 = 10f, Z0 = 0f, Z1 = 28f, Mez = 4.43f, MezZ = 18.8f;
        static Transform root, art, col, gameplay;

        static GameObject Piece(string n, Vector3 p, float yaw, Transform parent = null, Vector3? scale = null)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(K + n + ".fbx");
            if (!src) { Debug.LogWarning("kit piece missing " + n); return null; }
            var g = (GameObject)PrefabUtility.InstantiatePrefab(src, parent ? parent : art);
            g.transform.localPosition = p; g.transform.localRotation = Quaternion.Euler(0, yaw, 0) * src.transform.localRotation;   // keep the FBX's own (Blender Z-up) root rotation
            if (scale.HasValue) g.transform.localScale = scale.Value;
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return g;
        }
        static BoxCollider Box(string n, Vector3 center, Vector3 size, float yaw = 0f, float pitch = 0f)
        {
            var g = new GameObject(n); g.transform.SetParent(col, false); g.transform.localPosition = center; g.transform.localRotation = Quaternion.Euler(pitch, yaw, 0);
            var b = g.AddComponent<BoxCollider>(); b.size = size; g.layer = 0;
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.NavigationStatic);
            return b;
        }
        static Light Lamp(string n, Vector3 p, Color c, float i, float range)
        {
            var g = new GameObject(n); g.transform.SetParent(art, false); g.transform.localPosition = p;
            var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = i; l.range = range; l.shadows = LightShadows.None; return l;
        }
        static GameObject Slab(string n, Vector3 c, Vector3 s, Material m, Transform parent = null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent ? parent : art, false); g.transform.localPosition = c; g.transform.localScale = s;
            var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return g;
        }

        [MenuItem("Neon Protocol/R2/Build Relay Room")]
        public static string Build()
        {
            var old = GameObject.Find("RelayRoom"); if (old) Object.DestroyImmediate(old);
            root = new GameObject("RelayRoom").transform; root.position = O;
            art = new GameObject("Art").transform; art.SetParent(root, false);
            col = new GameObject("Colliders").transform; col.SetParent(root, false);
            gameplay = new GameObject("Gameplay").transform; gameplay.SetParent(root, false);
            var main = QuaterniusSetup.Palette("Main"); var recess = QuaterniusSetup.Palette("DarkGrey"); var violet = QuaterniusSetup.Palette("Accent");
            var amber = QuaterniusSetup.Amber(); var cyan = QuaterniusSetup.Cyan();

            // ---- floors: kit tiles (visual) + one solid slab each (collision) ----
            for (float x = X0 + 1f; x < X1; x += 2f)
                for (float z = Z0 + 1f; z < Z1; z += 2f)
                {
                    bool aisle = Mathf.Abs(x) < 2f && z < MezZ;   // central walkway toward the relay, slightly different tile
                    Piece(aisle ? "FloorTile_Basic2" : "FloorTile_Basic", new Vector3(x, 0f, z), 0f);
                    if (z > MezZ + 0.2f) Piece("FloorTile_Basic", new Vector3(x, Mez, z), 0f);
                }
            Box("Floor", new Vector3(0, -0.4f, (Z0 + Z1) / 2f), new Vector3(X1 - X0, 0.98f, Z1 - Z0));
            Box("Mezzanine", new Vector3(0, Mez - 0.15f, (MezZ + Z1) / 2f), new Vector3(X1 - X0, 0.48f, Z1 - MezZ));
            // mezzanine underside + front fascia (structure, deep blue recess)
            Slab("Mez_Underside", new Vector3(0, Mez - 0.3f, (MezZ + Z1) / 2f), new Vector3(X1 - X0, 0.2f, Z1 - MezZ), recess);
            Slab("Mez_Fascia", new Vector3(0, Mez - 0.35f, MezZ + 0.1f), new Vector3(12.4f, 0.75f, 0.35f), main);
            Slab("Mez_FasciaLine", new Vector3(0, Mez - 0.52f, MezZ - 0.09f), new Vector3(12.4f, 0.06f, 0.04f), violet);

            // ---- walls: two storeys, detailed side facing the room ----
            void WallRun(float fixedCoord, bool alongX, float yaw, string[] lower, string[] upper)
            {
                int n = lower.Length;
                for (int i = 0; i < n; i++)
                {
                    float t = (alongX ? X0 : Z0) + W * 0.5f + i * W;
                    Vector3 p = alongX ? new Vector3(t, 0, fixedCoord) : new Vector3(fixedCoord, 0, t);
                    if (lower[i] != null) Piece(lower[i], p, yaw);
                    if (upper[i] != null) Piece(upper[i], p + Vector3.up * H, yaw);
                }
            }
            // south (spawn side, faces +z): the sealed arrival door behind the player
            WallRun(Z0, true, 0f, new[] { "Walls/Wall_2", "Walls/Wall_1", "Walls/DoorDouble_Wall_SideA", "Walls/Wall_1", "Walls/Wall_2" },
                                  new[] { "Walls/Wall_Empty", "Walls/SmallWindows_Wall_SideA", "Walls/Wall_5", "Walls/SmallWindows_Wall_SideA", "Walls/Wall_Empty" });
            // north (back, faces -z): EXIT door in the recess, relay wall above the mezzanine
            WallRun(Z1, true, 180f, new[] { "Walls/Wall_3", "Walls/Wall_1", "Walls/DoorDouble_Wall_SideA", "Walls/Wall_1", "Walls/Wall_3" },
                                  new[] { "Walls/Wall_4", "Walls/Wall_Empty", "Walls/Wall_Empty", "Walls/Wall_Empty", "Walls/Wall_4" });
            // west (faces +x) and east (faces -x): long windows upstairs for daylight and depth
            var sideLow = new[] { "Walls/Wall_1", "Walls/Wall_3", "Walls/Wall_2", "Walls/Wall_1", "Walls/Wall_5", "Walls/Wall_1", "Walls/Wall_3" };
            var sideUp = new[] { "Walls/LongWindow_Wall_SideA", "Walls/LongWindow_Wall_SideA", "Walls/Wall_Empty", "Walls/LongWindow_Wall_SideA", "Walls/LongWindow_Wall_SideA", "Walls/Wall_Empty", "Walls/Wall_Empty" };
            WallRun(X0, false, 90f, sideLow, sideUp);
            WallRun(X1, false, -90f, sideLow, sideUp);
            // columns at every wall joint, both storeys
            for (float z = Z0; z <= Z1 + 0.01f; z += W) foreach (float x in new[] { X0 + 0.2f, X1 - 0.2f }) { Piece("Column_1", new Vector3(x, 0, z), 0f); Piece("Column_1", new Vector3(x, H, z), 0f); }
            for (float x = X0 + W; x < X1; x += W) foreach (float z in new[] { Z0 + 0.2f, Z1 - 0.2f }) { Piece("Column_Slim", new Vector3(x, 0, z), 0f); Piece("Column_Slim", new Vector3(x, H, z), 0f); }
            // wall collision: one slab per side (two storeys)
            Box("Wall_S", new Vector3(0, H, Z0 - 0.35f), new Vector3(X1 - X0 + 2f, 2 * H + 1f, 0.7f));
            Box("Wall_N", new Vector3(0, H, Z1 + 0.35f), new Vector3(X1 - X0 + 2f, 2 * H + 1f, 0.7f));
            Box("Wall_W", new Vector3(X0 - 0.35f, H, (Z0 + Z1) / 2f), new Vector3(0.7f, 2 * H + 1f, Z1 - Z0 + 2f));
            Box("Wall_E", new Vector3(X1 + 0.35f, H, (Z0 + Z1) / 2f), new Vector3(0.7f, 2 * H + 1f, Z1 - Z0 + 2f));
            // ceiling: one calm light plane with recessed light panels over the floor pools (no busy tile grid)
            Slab("Ceiling_Plane", new Vector3(0, 2 * H + 0.1f, (Z0 + Z1) / 2f), new Vector3(X1 - X0, 0.2f, Z1 - Z0), QuaterniusSetup.Palette("Light"));
            foreach (var p in new[] { new Vector3(-5, 0, 5), new Vector3(5, 0, 5), new Vector3(-5, 0, 13), new Vector3(5, 0, 13), new Vector3(0, 0, 23.5f) })
            {
                Slab("Ceiling_Coffer", new Vector3(p.x, 2 * H - 0.02f, p.z), new Vector3(4.2f, 0.12f, 3.2f), QuaterniusSetup.Palette("DarkGrey"));
                Slab("Ceiling_Panel", new Vector3(p.x, 2 * H - 0.09f, p.z), new Vector3(3.6f, 0.04f, 2.6f), QuaterniusSetup.CeilingPanel());
            }
            for (float z = Z0 + 4f; z < Z1; z += 8f) Slab("Ceiling_Beam", new Vector3(0, 2 * H - 0.25f, z), new Vector3(X1 - X0, 0.5f, 0.6f), main);
            Box("Ceiling", new Vector3(0, 2 * H + 0.4f, (Z0 + Z1) / 2f), new Vector3(X1 - X0 + 2f, 0.8f, Z1 - Z0 + 2f));

            // ---- stairs: three kit flights per side, rising toward the mezzanine edge (z = MezZ) ----
            foreach (float sx in new[] { -8.1f, 8.1f })
            {
                for (int k = 0; k < 3; k++)
                {
                    float topY = Mez - k * 1.45f, topZ = MezZ - k * 3.11f;
                    Piece("Staircase", new Vector3(sx, topY - 0.215f, topZ), 180f);
                }
                // one smooth ramp collider for the 3 flights (CharacterController + NavMesh friendly)
                float run = 3 * 3.11f, len = Mathf.Sqrt(run * run + Mez * Mez), ang = Mathf.Atan2(Mez, run) * Mathf.Rad2Deg;
                Box("Stair_Ramp_" + (sx < 0 ? "W" : "E"), new Vector3(sx, Mez * 0.5f - 0.12f, MezZ - run * 0.5f), new Vector3(3.6f, 0.25f, len), 0f, -ang);
                // inner stair side wall so nobody drops off the open side of the flight
                Slab("Stair_Side_" + (sx < 0 ? "W" : "E"), new Vector3(sx + (sx < 0 ? 1.95f : -1.95f), Mez * 0.5f, MezZ - run * 0.5f), new Vector3(0.18f, 1.0f, len), main).transform.localRotation = Quaternion.Euler(-ang, 0, 0);
                Box("Stair_SideCol_" + (sx < 0 ? "W" : "E"), new Vector3(sx + (sx < 0 ? 1.95f : -1.95f), Mez * 0.5f + 1.2f, MezZ - run * 0.5f), new Vector3(0.2f, 3.6f, len + 0.4f), 0f, -ang);   // too tall to stand on
            }
            // mezzanine rail between the stairs: chunky posts + top bar, real collision
            for (float x = -6f; x <= 6.01f; x += 2f) Slab("Rail_Post", new Vector3(x, Mez + 0.52f, MezZ + 0.15f), new Vector3(0.16f, 1.05f, 0.16f), main);
            Slab("Rail_Top", new Vector3(0, Mez + 1.05f, MezZ + 0.15f), new Vector3(12.3f, 0.12f, 0.22f), main);
            Slab("Rail_Line", new Vector3(0, Mez + 0.98f, MezZ + 0.03f), new Vector3(12.3f, 0.04f, 0.03f), violet);
            Slab("Rail_Glass", new Vector3(0, Mez + 0.5f, MezZ + 0.15f), new Vector3(12.0f, 0.9f, 0.04f), QuaterniusSetup.GlassClear());
            Box("Rail", new Vector3(0, Mez + 1.25f, MezZ + 0.15f), new Vector3(12.4f, 2.5f, 0.3f));   // taller than a sprint jump

            // ---- cover + props (ground floor), all with simple box collision ----
            void Prop(string n, Vector3 p, float yaw)
            {
                var g = Piece(n, p, yaw); if (!g) return;
                // the kit roots are scaled x100: collision goes on its own unscaled box at the prop's world bounds
                var rs = g.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                Box("Prop_" + n, b.center - O, b.size);
            }
            Prop("Props_ContainerFull", new Vector3(-3.2f, 0.09f, 9.5f), 0f); Prop("Props_Crate", new Vector3(-4.2f, 0.09f, 9.6f), 15f);
            Prop("Props_CrateLong", new Vector3(3.6f, 0.09f, 12.5f), 90f); Prop("Props_Crate", new Vector3(4.4f, 0.09f, 11.5f), -10f);
            Prop("Props_Shelf", new Vector3(-8.9f, 0.09f, 4.5f), 90f); Prop("Props_Computer", new Vector3(8.8f, 0.09f, 4f), -90f);
            Prop("Props_Vessel_Tall", new Vector3(-6.5f, 0.09f, 1.6f), 0f); Prop("Props_Vessel", new Vector3(6.5f, 0.09f, 1.6f), 0f);
            Prop("Props_Pod", new Vector3(-8.3f, Mez + 0.09f, 26.2f), 90f); Prop("Props_Capsule", new Vector3(8.3f, Mez + 0.09f, 26.2f), -90f);
            // direction: amber chevrons from the spawn toward the exit recess (sparse, low glow)
            for (int i = 0; i < 4; i++)
            {
                var c = new GameObject("Chevron"); c.transform.SetParent(art, false); c.transform.localPosition = new Vector3(0, 0.12f, 4f + i * 3.4f);
                foreach (float s in new[] { -1f, 1f }) { var a = Slab("Arm", Vector3.zero, new Vector3(0.09f, 0.02f, 0.6f), amber, c.transform); a.transform.localPosition = new Vector3(s * 0.2f, 0, -0.1f); a.transform.localRotation = Quaternion.Euler(0, -s * 40f, 0); }
            }

            // ---- exterior: sky gradient behind the windows (depth, daylight) ----
            var skyIds = AssetDatabase.FindAssets("M_SkyGradient t:Material");
            var sky = skyIds.Length > 0 ? AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(skyIds[0])) : null;
            foreach (float sx in new[] { -1f, 1f })
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "SkyOutside"; Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.SetParent(art, false); q.transform.localPosition = new Vector3(sx * 16f, 7f, 14f); q.transform.localRotation = Quaternion.Euler(0, sx * 90f, 0); q.transform.localScale = new Vector3(60f, 22f, 1f);
                if (sky) q.GetComponent<MeshRenderer>().sharedMaterial = sky;
            }

            // ---- light: broad warm ceiling pools, cyan on the relay, amber/cyan at the exit, cool fill in the recess ----
            foreach (var p in new[] { new Vector3(-5, 8f, 5), new Vector3(5, 8f, 5), new Vector3(-5, 8f, 13), new Vector3(5, 8f, 13), new Vector3(0, 8f, 23.5f) })
                Lamp("Ceiling_Pool", p, new Color(1f, 0.95f, 0.88f), 5.5f, 13f);
            Lamp("Recess_Fill", new Vector3(0, 3.2f, 23f), new Color(0.65f, 0.72f, 1f), 3.2f, 11f);
            Slab("Recess_Panel", new Vector3(0, Mez - 0.42f, 24f), new Vector3(8f, 0.04f, 2.2f), QuaterniusSetup.CeilingPanel());

            // ---- gameplay: spawn, checkpoint, relay A, HALO post, exit ----
            var spawn = new GameObject("CP_RelayRoom").transform; spawn.SetParent(gameplay, false); spawn.localPosition = new Vector3(0, 0.12f, 2.2f); spawn.localRotation = Quaternion.identity;
            var relayGo = GameObject.Find("Relay_A");
            if (relayGo)
            {
                relayGo.transform.position = O + new Vector3(0f, Mez + 2.35f, Z1 - 0.55f);
                var iris = relayGo.GetComponent<IrisRelay>();
                if (iris && iris.visual)
                {   // turn the whole relay so the Iris face (visual +Y) points into the room (-z); idempotent
                    var f = iris.visual.up; f.y = 0f;
                    relayGo.transform.rotation = Quaternion.Euler(0, Vector3.SignedAngle(f, Vector3.back, Vector3.up), 0) * relayGo.transform.rotation;
                }
                if (iris && iris.visual) iris.visual.localScale = Vector3.one * 1.7f;          // readable from the floor
                var pulse = relayGo.transform.Find("PulsePath");
                if (pulse && iris)
                {
                    var pts = new[] { relayGo.transform.position + Vector3.back * 0.4f, O + new Vector3(0, Mez + 1.2f, MezZ + 1f), O + new Vector3(0, 3.4f, Z1 - 1.2f) };
                    for (int i = 0; i < iris.pulsePath.Length; i++) if (iris.pulsePath[i]) iris.pulsePath[i].position = pts[Mathf.Min(i, pts.Length - 1)];
                }
                var key = relayGo.GetComponentInChildren<Light>(true);
            }
            Lamp("Relay_Key", new Vector3(0, Mez + 3.2f, Z1 - 3.2f), new Color(0.75f, 0.95f, 1f), 3.5f, 7f);
            // HALO: lesson post on the mezzanine, overlooking the floor; patrol stays inside the room
            CompetitorAI halo = null; foreach (var a in Object.FindObjectsByType<CompetitorAI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (a.name.Contains("HALO")) halo = a;
            if (halo)
            {
                var post = new GameObject("HALO_RoomPost").transform; post.SetParent(gameplay, false); post.localPosition = new Vector3(5.5f, Mez + 0.1f, 24.5f); post.localRotation = Quaternion.Euler(0, 200f, 0);
                var pA = new GameObject("HALO_Patrol_A").transform; pA.SetParent(gameplay, false); pA.localPosition = new Vector3(5.5f, Mez + 0.1f, 22f);
                var pB = new GameObject("HALO_Patrol_B").transform; pB.SetParent(gameplay, false); pB.localPosition = new Vector3(-5.5f, Mez + 0.1f, 22f);
                halo.dormantPost = post; halo.patrol = new[] { pA, pB };
                halo.transform.position = post.position; halo.transform.rotation = post.rotation; EditorUtility.SetDirty(halo);
            }
            // EXIT: kit double door in the recess (north wall, x = 0), sealed until the relay is online
            // unscaled Exit root (the kit FBX roots are x100): RoomExit, trigger and block live here, the door model is a child
            var exitRoot = new GameObject("Exit").transform; exitRoot.SetParent(gameplay, false); exitRoot.localPosition = new Vector3(0, 0.09f, Z1 - 0.62f); exitRoot.localRotation = Quaternion.Euler(0, 180f, 0);
            var doorGo = Piece("Door_Double", Vector3.zero, 0f, exitRoot);
            // Animated panels must not be baked into static geometry or occluders.
            foreach (var renderer in doorGo.GetComponentsInChildren<MeshRenderer>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, 0);
                renderer.receiveGI = ReceiveGI.LightProbes;
            }
            var exit = exitRoot.gameObject.AddComponent<RoomExit>();
            exit.relay = relayGo ? relayGo.GetComponent<Relay>() : null;
            var leaves = new List<Transform>(); foreach (Transform t in doorGo.transform) leaves.Add(t);
            if (leaves.Count >= 2) { leaves.Sort((a, b) => a.localPosition.x.CompareTo(b.localPosition.x)); exit.leafL = leaves[0]; exit.leafR = leaves[leaves.Count - 1]; }
            else if (leaves.Count == 1) exit.leafL = leaves[0];
            var dl = new GameObject("Exit_Light").AddComponent<Light>(); dl.transform.SetParent(gameplay, false); dl.transform.localPosition = new Vector3(0, 3.0f, Z1 - 2.2f); dl.type = LightType.Point; dl.range = 7f; dl.intensity = 4f; dl.shadows = LightShadows.None; exit.doorLight = dl;
            var sigs = new List<Renderer>();
            foreach (float sx in new[] { -1.75f, 1.75f }) sigs.Add(Slab("Exit_Signal", new Vector3(sx, 2.95f, Z1 - 0.75f), new Vector3(0.18f, 0.5f, 0.06f), amber, gameplay).GetComponent<Renderer>());
            exit.signals = sigs.ToArray(); exit.lockedMat = amber; exit.openMat = cyan;
            var tb = exitRoot.gameObject.AddComponent<BoxCollider>(); tb.isTrigger = true; tb.center = new Vector3(0, 1.3f, -0.7f); tb.size = new Vector3(2.4f, 2.6f, 1.0f);   // in front of the door (room side), on the RoomExit object
            // the closed door blocks the player physically until it opens (the trigger handles the hand-over)
            var doorBlock = new GameObject("Exit_Block"); doorBlock.transform.SetParent(exitRoot, false); doorBlock.transform.localPosition = new Vector3(0, 1.4f, -0.3f);
            var db = doorBlock.AddComponent<BoxCollider>(); db.size = new Vector3(2.7f, 2.8f, 0.4f); doorBlock.layer = 2;   // ignore-raycast: not a NavMesh source, not paintable
            exit.block = db;
            var eText = new GameObject("Exit_Label").AddComponent<TextMeshPro>(); eText.transform.SetParent(gameplay, false); eText.transform.localPosition = new Vector3(0, 3.55f, Z1 - 0.72f); eText.text = "EXIT"; eText.fontSize = 3.2f; eText.alignment = TextAlignmentOptions.Center; eText.color = new Color(1f, 0.85f, 0.6f);
            eText.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/NeonProtocol/Art/Fonts/Orbitron-900 SDF.asset"); eText.rectTransform.sizeDelta = new Vector2(4f, 1f);
            // where the door leads: featured -> the vault approach; full adventure -> the old arrival
            var tFeat = new GameObject("Exit_To_Vault").transform; tFeat.SetParent(gameplay, false); tFeat.position = new Vector3(0f, 0.08f, 9f); tFeat.rotation = Quaternion.identity;
            var tFull = new GameObject("Exit_To_Fracture").transform; tFull.SetParent(gameplay, false); tFull.position = new Vector3(0f, 2.1f, -31f); tFull.rotation = Quaternion.identity;
            exit.featuredTarget = tFeat; exit.fullTarget = tFull;

            // checkpoint list: the room becomes the first checkpoint (spawn for both routes)
            var hd = Object.FindFirstObjectByType<HeistDirector>();
            if (hd)
            {
                var cps = new List<Transform>(hd.checkpoints); cps.RemoveAll(c => !c || c.name == "CP_RelayRoom"); cps.Insert(0, spawn); hd.checkpoints = cps.ToArray(); EditorUtility.SetDirty(hd);
            }
            var scene = root.gameObject.scene; EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            int tris = 0; foreach (var mf in root.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) tris += mf.sharedMesh.triangles.Length / 3;
            return $"relay room built: {art.childCount} art objects, {col.childCount} colliders, {tris} triangles";
        }

        [MenuItem("Neon Protocol/R2/Rebake NavMesh (saved asset)")]
        public static string BakeNav()
        {
            var nav = Object.FindFirstObjectByType<Unity.AI.Navigation.NavMeshSurface>();
            const string path = "Assets/NeonProtocol/Scenes/TheFracture/NavMesh-Architecture.asset";
            // the room's colliders live outside the Architecture hierarchy: collect all colliders in the scene
            nav.collectObjects = Unity.AI.Navigation.CollectObjects.All;
            nav.BuildNavMesh();
            var data = nav.navMeshData;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(data, path);
            nav.navMeshData = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(path);
            EditorUtility.SetDirty(nav); EditorSceneManager.SaveScene(nav.gameObject.scene);
            return "navmesh rebaked and saved to " + path;
        }
    }
}
