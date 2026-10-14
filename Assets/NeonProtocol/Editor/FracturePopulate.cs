using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace NeonProtocol.EditorTools
{
    // Adds the rivals, the drone and the gameplay wiring (HUD feeds, tag controller) to THE FRACTURE.
    // Idempotent: existing "Rivals"/"Drones" groups are left alone.
    public static class FracturePopulate
    {
        const string Root = CharacterImportRules.Root;
        const string MatDir = "Assets/NeonProtocol/Art/Materials";

        [MenuItem("Neon Protocol/Populate THE FRACTURE")]
        public static string Populate()
        {
            var log = new List<string>();
            var player = GameObject.Find("Player");
            var sys = GameObject.Find("_Systems");

            // --- player gameplay components ---
            var tag = player.GetComponent<TagHealth>() ? player.GetComponent<TagHealth>() : player.AddComponent<TagHealth>();
            tag.team = 0;
            var ptc = player.GetComponent<PlayerTagController>() ? player.GetComponent<PlayerTagController>() : player.AddComponent<PlayerTagController>();
            ptc.hitClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Generated/splat_2.wav");
            ptc.tagClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Generated/tag_out.wav");
            // paint on the player lives on the root (where the CharacterController collider is)
            var model = player.transform.Find("Model");
            var oldPd = model ? model.GetComponent<PaintDynamic>() : null; if (oldPd) Object.DestroyImmediate(oldPd);
            var pd = player.GetComponent<PaintDynamic>() ? player.GetComponent<PaintDynamic>() : player.AddComponent<PaintDynamic>();
            pd.textureSize = 512; pd.renderers = new Renderer[] { model.GetComponentInChildren<SkinnedMeshRenderer>() };
            var hud = sys.GetComponent<WebHud>() ? sys.GetComponent<WebHud>() : sys.AddComponent<WebHud>();
            hud.rifle = player.GetComponentInChildren<PaintRifle>(); hud.tags = ptc;
            var paint = sys.GetComponent<PaintSystem>(); paint.paintableMask = (1 << 0) | (1 << 8) | (1 << 9) | (1 << 10);

            // --- rivals ---
            if (!GameObject.Find("Rivals"))
            {
                var group = new GameObject("Rivals").transform;
                var routes = new[]
                {
                    new[] { new Vector3(-6, 0, -9), new Vector3(6, 0, -9), new Vector3(6, 0, 4), new Vector3(-6, 0, 4) },          // hall loop around the reactor
                    new[] { new Vector3(19.5f, 4.5f, -10), new Vector3(19.5f, 4.5f, 8), new Vector3(0, 4.5f, 9), new Vector3(-19, 4.5f, 9) },   // catwalk + bridge
                    new[] { new Vector3(-8, 0, 17), new Vector3(8, 0, 17), new Vector3(8, 0, 29), new Vector3(-8, 0, 29) },          // vault guard
                };
                var names = new[] { "VANTA", "HALO", "KESTREL" };
                var accent = new Color(2.6f, 0.2f, 1.9f);
                for (int i = 0; i < routes.Length; i++)
                {
                    var r = MakeRival(group, names[i], routes[i], accent);
                    log.Add(r.name);
                }
            }

            // --- drone ---
            if (!GameObject.Find("Drones"))
            {
                var group = new GameObject("Drones").transform;
                var route = new[] { new Vector3(0, 5, -16), new Vector3(14, 6, -6), new Vector3(10, 6, 14), new Vector3(0, 7, 24), new Vector3(-10, 6, 14), new Vector3(-14, 5, -6) };
                MakeDrone(group, route);
                log.Add("drone");
            }

            EditorSceneManager.MarkSceneDirty(player.scene);
            EditorSceneManager.SaveOpenScenes();
            return "populated: " + string.Join(",", log);
        }

        static CompetitorAI MakeRival(Transform parent, string name, Vector3[] route, Color accent)
        {
            var go = new GameObject("Rival_" + name); go.layer = 9;
            go.transform.SetParent(parent); go.transform.position = route[0];
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Swat.fbx");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src, go.transform);
            model.name = "Model";
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 9;
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.sharedMaterials = new[] { RivalMat("Body", accent * 0.7f), RivalMat("Gear", accent) };
            var anim = model.GetComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorBuilder.ControllerPath);
            anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // rifle
            var rifle = new GameObject("PaintRifle").transform; rifle.SetParent(go.transform, false); rifle.gameObject.layer = 9;
            var mesh = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NeonProtocol/Art/Models/Blasters/blaster-d.fbx"), rifle);
            mesh.transform.localPosition = new Vector3(0, 0.1f, 0.1f); mesh.transform.localEulerAngles = new Vector3(0, 180, 0);
            foreach (var r in mesh.GetComponentsInChildren<Renderer>()) { r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Rifle.mat"); r.gameObject.layer = 9; }
            var muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(rifle, false); muzzle.localPosition = new Vector3(0, 0.16f, 0.57f);
            var rg = new GameObject("RightGrip").transform; rg.SetParent(rifle, false); rg.localPosition = new Vector3(0, -0.04f, -0.03f); rg.localEulerAngles = new Vector3(0, 0, -90);
            var lg = new GameObject("LeftGrip").transform; lg.SetParent(rifle, false); lg.localPosition = new Vector3(0, 0.05f, 0.33f); lg.localEulerAngles = new Vector3(0, 0, 90);
            var ca = model.AddComponent<CharacterAnimator>(); ca.weapon = rifle; ca.rightGrip = rg; ca.leftGrip = lg;

            var cap = go.AddComponent<CapsuleCollider>(); cap.height = 1.8f; cap.radius = 0.35f; cap.center = new Vector3(0, 0.9f, 0);
            var agent = go.AddComponent<NavMeshAgent>(); agent.radius = 0.35f; agent.height = 1.8f; agent.speed = 1.8f; agent.angularSpeed = 540f; agent.acceleration = 14f; agent.stoppingDistance = 0.3f;
            var th = go.AddComponent<TagHealth>(); th.team = 1; th.hitsToTag = 3; th.downTime = 6f;
            var pd = go.AddComponent<PaintDynamic>(); pd.textureSize = 256; pd.renderers = new Renderer[] { smr };
            var ai = go.AddComponent<CompetitorAI>(); ai.displayName = name; ai.muzzle = muzzle;
            ai.fireClips = new[] { AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Generated/rifle_shot_1.wav"), AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Generated/rifle_shot_3.wav") };
            var pts = new Transform[route.Length];
            var routeRoot = new GameObject("Route").transform; routeRoot.SetParent(parent);
            routeRoot.name = "Route_" + name;
            for (int i = 0; i < route.Length; i++) { pts[i] = new GameObject("P" + i).transform; pts[i].SetParent(routeRoot); pts[i].position = route[i]; }
            ai.patrol = pts;
            // floating name tag "BOT" label is in the HUD layer later; the name makes the AI status explicit
            return ai;
        }

        static Material RivalMat(string slot, Color emission)
        {
            string path = $"{MatDir}/M_Rival_{slot}.mat";
            var src = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Suit_{slot}.mat");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            m.CopyPropertiesFromMaterial(src);
            m.SetColor("_EmissionColor", emission);
            m.SetColor("_BaseColor", slot == "Body" ? new Color(0.2f, 0.17f, 0.2f) : new Color(0.15f, 0.12f, 0.15f));
            m.SetColor("_RimColor", new Color(0.4f, 0.1f, 0.3f));
            EditorUtility.SetDirty(m);
            return m;
        }

        static void MakeDrone(Transform parent, Vector3[] route)
        {
            var go = new GameObject("Sentinel"); go.layer = 10; go.transform.SetParent(parent); go.transform.position = route[0];
            var body = new GameObject("Body").transform; body.SetParent(go.transform, false);
            var metal = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Metal.mat");
            var obsidian = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Obsidian.mat");
            NeonBlock Part(string n, Vector3 lp, Vector3 size, Material m, float ch, Vector3 euler = default)
            {
                var p = new GameObject(n); p.layer = 10; p.transform.SetParent(body, false); p.transform.localPosition = lp; p.transform.localEulerAngles = euler;
                p.AddComponent<MeshFilter>(); p.AddComponent<MeshRenderer>().sharedMaterial = m;
                var b = p.AddComponent<NeonBlock>(); b.size = size; b.chamfer = ch; b.paintable = false; b.addCollider = false;
                NeonBlockTools.Rebuild(b);
                var c = p.GetComponent<Collider>(); if (c) Object.DestroyImmediate(c);
                return b;
            }
            Part("Hull", Vector3.zero, new Vector3(0.9f, 0.32f, 1.1f), obsidian, 0.1f);
            Part("Spine", new Vector3(0, 0.2f, -0.05f), new Vector3(0.36f, 0.14f, 0.8f), metal, 0.05f);
            var eyeB = Part("Eye", new Vector3(0, -0.02f, 0.58f), new Vector3(0.34f, 0.16f, 0.08f), AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_RelayCore.mat"), 0.03f);
            var rotors = new List<Transform>();
            foreach (var s in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            {
                Part("Arm", new Vector3(s.x * 0.5f, 0.05f, s.y * 0.45f), new Vector3(0.7f, 0.06f, 0.1f), metal, 0.02f, new Vector3(0, s.x * s.y * 45f, 0));
                var hub = new GameObject("Rotor"); hub.layer = 10; hub.transform.SetParent(body, false); hub.transform.localPosition = new Vector3(s.x * 0.78f, 0.12f, s.y * 0.72f);
                for (int k = 0; k < 2; k++)
                {
                    var bl = new GameObject("Blade"); bl.layer = 10; bl.transform.SetParent(hub.transform, false); bl.transform.localEulerAngles = new Vector3(0, k * 90, 0);
                    bl.AddComponent<MeshFilter>(); bl.AddComponent<MeshRenderer>().sharedMaterial = metal;
                    var b = bl.AddComponent<NeonBlock>(); b.size = new Vector3(0.62f, 0.015f, 0.06f); b.chamfer = 0.005f; b.paintable = false; b.addCollider = false; NeonBlockTools.Rebuild(b);
                    var c = bl.GetComponent<Collider>(); if (c) Object.DestroyImmediate(c);
                }
                rotors.Add(hub.transform);
            }
            var lightGo = new GameObject("Searchlight"); lightGo.transform.SetParent(go.transform, false); lightGo.transform.localPosition = new Vector3(0, -0.15f, 0.5f);
            var spot = lightGo.AddComponent<Light>(); spot.type = LightType.Spot; spot.spotAngle = 52f; spot.innerSpotAngle = 30f; spot.range = 24f; spot.intensity = 9f; spot.shadows = LightShadows.None;
            var muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(go.transform, false); muzzle.localPosition = new Vector3(0, -0.25f, 0.6f);
            var col = go.AddComponent<SphereCollider>(); col.radius = 0.75f;
            var drone = go.AddComponent<SentinelDrone>();
            drone.body = body; drone.rotors = rotors.ToArray(); drone.searchlight = spot; drone.eye = eyeB.GetComponent<Renderer>(); drone.muzzle = muzzle;
            var pts = new Transform[route.Length];
            var rr = new GameObject("Route_Sentinel").transform; rr.SetParent(parent);
            for (int i = 0; i < route.Length; i++) { pts[i] = new GameObject("P" + i).transform; pts[i].SetParent(rr); pts[i].position = route[i]; }
            drone.route = pts;
            var hum = go.AddComponent<AudioSource>(); hum.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Kenney/engineCircular_000.ogg");
            hum.loop = true; hum.playOnAwake = true; hum.spatialBlend = 1f; hum.minDistance = 2f; hum.maxDistance = 30f; hum.volume = 0.45f; hum.rolloffMode = AudioRolloffMode.Logarithmic;
            drone.hum = hum;
            drone.alarmClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Kenney/computerNoise_000.ogg");
            drone.shutdownClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Generated/tag_out.wav");
            drone.fireClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Generated/rifle_shot_2.wav");
        }
    }
}
