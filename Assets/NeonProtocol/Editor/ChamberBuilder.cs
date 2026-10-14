using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonProtocol.EditorTools
{
    // One-time creation of the Gate 1 test chamber. Never overwrites: if the scene exists it refuses.
    // After creation everything is ordinary, hand-editable GameObjects.
    public static class ChamberBuilder
    {
        public const string ScenePath = "Assets/NeonProtocol/Scenes/TestChamber.unity";
        const string MatDir = "Assets/NeonProtocol/Art/Materials";

        [MenuItem("Neon Protocol/Create Test Chamber (one-time)")]
        public static string Create()
        {
            if (File.Exists(ScenePath)) return "exists: " + ScenePath + " (not overwritten)";
            EnsureLayers();
            Directory.CreateDirectory(MatDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var mats = CreateMaterials();

            // --- systems ---
            var systems = new GameObject("_Systems");
            var paint = systems.AddComponent<PaintSystem>();
            paint.splatShader = Shader.Find("Hidden/NeonProtocol/PaintSplat");
            paint.dryShader = Shader.Find("Hidden/NeonProtocol/PaintDry");
            paint.paintableMask = 1 << 0;
            var proj = systems.AddComponent<PaintProjectiles>();
            proj.ballMaterial = BallMaterial();
            proj.hitMask = ~((1 << 11) | (1 << 12));
            var fx = systems.AddComponent<ImpactFX>();
            fx.sprayA = CreateSpray(systems.transform, "SprayA", mats.sprayA);
            fx.sprayB = CreateSpray(systems.transform, "SprayB", mats.sprayB);
            fx.flashLights = new Light[4];
            for (int i = 0; i < 4; i++)
            {
                var l = new GameObject("ImpactFlash" + i).AddComponent<Light>();
                l.transform.SetParent(systems.transform);
                l.type = LightType.Point; l.range = 4.5f; l.intensity = 0; l.shadows = LightShadows.None; l.enabled = false;
                fx.flashLights[i] = l;
            }
            systems.AddComponent<GameBoot>();

            // --- architecture ---
            var arch = new GameObject("Architecture").transform;
            Block(arch, "Floor", NeonBlock.Shape.Box, new Vector3(0, -0.25f, 0), new Vector3(36, 0.5f, 36), mats.floor, 0.02f);
            Block(arch, "Wall_N", NeonBlock.Shape.Box, new Vector3(0, 5f, 18.5f), new Vector3(38, 10, 1), mats.trimWall, 0.05f);
            Block(arch, "Wall_S", NeonBlock.Shape.Box, new Vector3(0, 5f, -18.5f), new Vector3(38, 10, 1), mats.trimWall, 0.05f);
            Block(arch, "Wall_E", NeonBlock.Shape.Box, new Vector3(18.5f, 5f, 0), new Vector3(1, 10, 36), mats.graphite, 0.05f);
            Block(arch, "Wall_W", NeonBlock.Shape.Box, new Vector3(-18.5f, 5f, 0), new Vector3(1, 10, 36), mats.obsidian, 0.05f);
            // balcony along the north wall at 3.6 m, reached by a staircase
            Block(arch, "Balcony", NeonBlock.Shape.Box, new Vector3(-4f, 3.35f, 14.5f), new Vector3(20, 0.5f, 7), mats.floor, 0.04f);
            Block(arch, "BalconyRail", NeonBlock.Shape.Box, new Vector3(-4f, 4.15f, 11.15f), new Vector3(20, 1.1f, 0.3f), mats.metal, 0.03f);
            for (int i = 0; i < 4; i++)
                Block(arch, "BalconyPillar" + i, NeonBlock.Shape.Box, new Vector3(-12f + i * 5.33f, 1.55f, 11.5f), new Vector3(0.7f, 3.1f, 0.7f), mats.obsidian, 0.06f);
            var stairs = Block(arch, "Stairs", NeonBlock.Shape.Stairs, new Vector3(8.6f, 0f, 3.8f), new Vector3(2.6f, 3.6f, 7.2f), mats.graphite, 0.02f);
            stairs.steps = 18;
            Block(arch, "StairLanding", NeonBlock.Shape.Box, new Vector3(8.6f, 3.35f, 13.5f), new Vector3(2.6f, 0.5f, 5f), mats.floor, 0.03f);
            // big cover monolith + low cover
            Block(arch, "Monolith", NeonBlock.Shape.Box, new Vector3(-3f, 2.5f, -2f), new Vector3(3.2f, 5f, 3.2f), mats.obsidian, 0.08f);
            Block(arch, "CoverLow_A", NeonBlock.Shape.Box, new Vector3(4f, 0.55f, -6f), new Vector3(3.5f, 1.1f, 0.8f), mats.graphite, 0.05f);
            Block(arch, "CoverLow_B", NeonBlock.Shape.Box, new Vector3(-9f, 0.55f, -9f), new Vector3(0.8f, 1.1f, 3.5f), mats.graphite, 0.05f);
            Block(arch, "Ramp", NeonBlock.Shape.Ramp, new Vector3(-12f, 0f, 1f), new Vector3(3f, 1.2f, 4f), mats.graphite, 0f);
            Block(arch, "RampTop", NeonBlock.Shape.Box, new Vector3(-12f, 0.6f, 6f), new Vector3(3f, 1.2f, 2f), mats.graphite, 0.04f);

            // --- moving paint target ---
            var target = Block(arch, "PaintTarget", NeonBlock.Shape.Box, new Vector3(0, 1.1f, 12f), new Vector3(1.2f, 2.0f, 0.16f), mats.target, 0.03f);
            target.paintable = false;
            target.gameObject.AddComponent<PaintDynamic>().textureSize = 256;
            var mover = target.gameObject.AddComponent<TargetMover>();
            mover.travel = new Vector3(6f, 0, 0); mover.speed = 0.35f;
            target.gameObject.AddComponent<PaintTarget>();

            // --- lighting ---
            var lights = new GameObject("Lighting").transform;
            var sun = new GameObject("MoonLight").AddComponent<Light>();
            sun.transform.SetParent(lights);
            sun.type = LightType.Directional; sun.color = new Color(0.62f, 0.66f, 1f); sun.intensity = 0.45f;
            sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(58f, -35f, 0);
            AccentLight(lights, "Violet_N", new Vector3(-10, 7.5f, 16.5f), new Color(0.55f, 0.25f, 1f), 9f, 18f);
            AccentLight(lights, "Cyan_E", new Vector3(16.5f, 4f, -6f), new Color(0.2f, 0.85f, 1f), 7f, 14f);
            AccentLight(lights, "Violet_SW", new Vector3(-15f, 3f, -14f), new Color(0.6f, 0.2f, 1f), 6f, 14f);
            AccentLight(lights, "Warm_Monolith", new Vector3(-3f, 6.5f, -2f), new Color(1f, 0.55f, 0.35f), 4f, 9f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.05f, 0.05f, 0.09f);
            RenderSettings.ambientEquatorColor = new Color(0.025f, 0.02f, 0.04f);
            RenderSettings.ambientGroundColor = new Color(0.01f, 0.008f, 0.015f);
            RenderSettings.skybox = null;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.018f; RenderSettings.fogColor = new Color(0.02f, 0.016f, 0.04f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            // --- post-processing ---
            var volGo = new GameObject("PostVolume");
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, "Assets/NeonProtocol/Art/Materials/TestChamberPost.asset");
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1.05f); bloom.intensity.Override(0.7f); bloom.scatter.Override(0.62f);
            var tm = profile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.Override(0.35f); ca.contrast.Override(12f); ca.saturation.Override(6f);
            var vg = profile.Add<Vignette>(true); vg.intensity.Override(0.24f); vg.smoothness.Override(0.45f);
            vol.sharedProfile = profile;

            // --- player ---
            var player = new GameObject("Player");
            player.layer = 8;
            player.transform.position = new Vector3(0, 0.05f, -10f);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.32f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.42f; cc.slopeLimit = 50f; cc.skinWidth = 0.04f;
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "PlaceholderBody"; Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(player.transform, false); body.transform.localPosition = new Vector3(0, 0.9f, 0);
            body.GetComponent<Renderer>().sharedMaterial = mats.character; body.layer = 8;
            var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Visor"; Object.DestroyImmediate(visor.GetComponent<Collider>());
            visor.transform.SetParent(body.transform, false); visor.transform.localPosition = new Vector3(0, 0.55f, 0.36f); visor.transform.localScale = new Vector3(0.5f, 0.12f, 0.12f);
            visor.GetComponent<Renderer>().sharedMaterial = mats.ballA; visor.layer = 8;

            var gun = new GameObject("PaintRifle"); gun.layer = 8;
            gun.transform.SetParent(player.transform, false); gun.transform.localPosition = new Vector3(0.28f, 1.28f, 0.32f);
            var gunBody = Block(gun.transform, "RifleBody", NeonBlock.Shape.Box, new Vector3(0, 0, 0.18f), new Vector3(0.09f, 0.14f, 0.72f), mats.metal, 0.012f);
            gunBody.paintable = false; gunBody.addCollider = false; gunBody.gameObject.layer = 8;
            var tank = Block(gun.transform, "PaintTank", NeonBlock.Shape.Box, new Vector3(0, 0.12f, 0.05f), new Vector3(0.08f, 0.1f, 0.26f), mats.ballA, 0.02f);
            tank.paintable = false; tank.addCollider = false; tank.gameObject.layer = 8;
            var muzzle = new GameObject("Muzzle"); muzzle.transform.SetParent(gun.transform, false); muzzle.transform.localPosition = new Vector3(0, 0.01f, 0.56f);
            var ml = muzzle.AddComponent<Light>(); ml.type = LightType.Point; ml.range = 3f; ml.color = new Color(0.3f, 1f, 0.9f); ml.enabled = false; ml.shadows = LightShadows.None;

            var rigGo = new GameObject("CameraRig");
            var cam = rigGo.AddComponent<Camera>();
            rigGo.tag = "MainCamera";
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 400f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.006f, 0.005f, 0.012f);
            var camData = rigGo.AddComponent<UniversalAdditionalCameraData>(); camData.renderPostProcessing = true;
            rigGo.AddComponent<AudioListener>();
            var rig = rigGo.AddComponent<ShoulderRig>();
            rig.target = player.transform; rig.cam = cam;
            rig.collisionMask = ~((1 << 8) | (1 << 9) | (1 << 10) | (1 << 11) | (1 << 12));
            rig.aimMask = ~((1 << 8) | (1 << 11) | (1 << 12));

            var motor = player.AddComponent<PlayerMotor>(); motor.rig = rig;
            var rifle = gun.AddComponent<PaintRifle>(); rifle.rig = rig; rifle.motor = motor; rifle.muzzle = muzzle.transform; rifle.muzzleLight = ml;
            rifle.fireSource = gun.AddComponent<AudioSource>(); rifle.fireSource.playOnAwake = false; rifle.fireSource.spatialBlend = 0.2f;
            player.AddComponent<DevHud>().rifle = rifle;

            NeonBlockTools.RebuildDirtyBlocks();
            NeonBlockTools.BakePaintAtlas();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == "Assets/Scenes/SampleScene.unity");
            if (!list.Exists(s => s.path == ScenePath)) list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            return "created " + ScenePath;
        }

        public static Material BallMaterial()
        {
            const string path = MatDir + "/M_Ball.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("NeonProtocol/Ball")) { name = "M_Ball" }; m.SetFloat("_Intensity", 4f); AssetDatabase.CreateAsset(m, path); }
            return m;
        }

        static NeonBlock Block(Transform parent, string name, NeonBlock.Shape shape, Vector3 pos, Vector3 size, Material mat, float chamfer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var b = go.AddComponent<NeonBlock>();
            b.shape = shape; b.size = size; b.chamfer = chamfer;
            if (shape == NeonBlock.Shape.Stairs) go.transform.localPosition = pos;   // stairs origin = first step front edge
            NeonBlockTools.Rebuild(b);
            return b;
        }

        static void AccentLight(Transform parent, string name, Vector3 pos, Color c, float intensity, float range)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(parent); l.transform.position = pos;
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        }

        static ParticleSystem CreateSpray(Transform parent, string name, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(parent);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400; main.gravityModifier = 1.6f; main.startSpeed = 0; main.duration = 1f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.3f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1.2f;
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        struct Mats { public Material floor, obsidian, graphite, trimWall, metal, target, character, ballA, ballB, sprayA, sprayB; }

        static Mats CreateMaterials()
        {
            var surf = Shader.Find("NeonProtocol/Surface");
            Material S(string n, Color c, float metal, float smooth, Vector2 wall, Vector2 floor, float grime, Color emi, float seamGlow, float trimSpacing)
            {
                var m = new Material(surf) { name = n };
                m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
                m.SetVector("_PanelWall", wall); m.SetVector("_PanelFloor", floor); m.SetFloat("_Grime", grime);
                m.SetColor("_EmissionColor", emi); m.SetFloat("_SeamGlow", seamGlow); m.SetFloat("_TrimSpacing", trimSpacing);
                m.SetFloat("_Paintable", 1f); m.EnableKeyword("_NP_PAINT");
                m.enableInstancing = true;
                AssetDatabase.CreateAsset(m, $"{MatDir}/{n}.mat");
                return m;
            }
            var mats = new Mats
            {
                floor = S("M_Floor", new Color(0.03f, 0.03f, 0.036f), 0.25f, 0.6f, new Vector2(2, 1), new Vector2(2, 2), 0.3f, new Color(0.5f, 0.15f, 1.6f), 0.18f, 0f),
                obsidian = S("M_Obsidian", new Color(0.016f, 0.016f, 0.02f), 0.4f, 0.78f, new Vector2(3, 1.5f), new Vector2(3, 3), 0.2f, Color.black, 0f, 0f),
                graphite = S("M_Graphite", new Color(0.06f, 0.06f, 0.066f), 0.05f, 0.36f, new Vector2(2, 1), new Vector2(2, 2), 0.45f, Color.black, 0f, 0f),
                trimWall = S("M_TrimWall", new Color(0.05f, 0.05f, 0.058f), 0.1f, 0.42f, new Vector2(2.5f, 1.25f), new Vector2(2, 2), 0.4f, new Color(0.9f, 0.3f, 2.6f), 0f, 3.2f),
                metal = S("M_Metal", new Color(0.22f, 0.22f, 0.24f), 0.9f, 0.62f, new Vector2(0.5f, 0.25f), new Vector2(0.5f, 0.5f), 0.15f, Color.black, 0f, 0f),
                target = S("M_Target", new Color(0.55f, 0.55f, 0.6f), 0.1f, 0.45f, new Vector2(0.4f, 0.4f), new Vector2(0.4f, 0.4f), 0.1f, new Color(0.4f, 0.4f, 0.5f), 0.4f, 0f),
                character = S("M_CharacterPlaceholder", new Color(0.04f, 0.04f, 0.05f), 0.5f, 0.55f, new Vector2(0.3f, 0.3f), new Vector2(0.3f, 0.3f), 0.1f, Color.black, 0f, 0f),
            };
            mats.target.DisableKeyword("_NP_PAINT"); mats.target.SetFloat("_Paintable", 0); mats.target.EnableKeyword("_NP_PAINT_LOCAL"); mats.target.SetFloat("_PaintLocal", 1);
            mats.metal.DisableKeyword("_NP_PAINT"); mats.metal.SetFloat("_Paintable", 0);
            mats.character.DisableKeyword("_NP_PAINT"); mats.character.SetFloat("_Paintable", 0);

            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material U(string n, Color hdr)
            {
                var m = new Material(unlit) { name = n }; m.SetColor("_BaseColor", hdr); m.enableInstancing = true;
                AssetDatabase.CreateAsset(m, $"{MatDir}/{n}.mat"); return m;
            }
            mats.ballA = U("M_BallA", new Color(0.25f, 4.2f, 3.6f));
            mats.ballB = U("M_BallB", new Color(4.4f, 0.35f, 3.2f));
            var particle = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            Material P(string n, Color hdr)
            {
                var m = new Material(particle) { name = n };
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 2);   // transparent, additive
                m.SetColor("_BaseColor", hdr);
                m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000;
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.One); m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                AssetDatabase.CreateAsset(m, $"{MatDir}/{n}.mat"); return m;
            }
            mats.sprayA = P("M_SprayA", new Color(0.3f, 3.5f, 3f));
            mats.sprayB = P("M_SprayB", new Color(3.6f, 0.3f, 2.8f));
            AssetDatabase.SaveAssets();
            return mats;
        }

        public static void EnsureLayers()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            string[] names = { "Player", "Competitor", "Drone", "Projectile", "TriggerZone" };
            for (int i = 0; i < names.Length; i++)
            {
                var p = layers.GetArrayElementAtIndex(8 + i);
                if (string.IsNullOrEmpty(p.stringValue)) p.stringValue = names[i];
            }
            tm.ApplyModifiedProperties();
        }
    }
}
