using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Replaces the capsule placeholder on the open scene's Player with the Mixamo competitor, wired to the
    // animator, procedural rifle IK, paint (REVEAL) and audio. Safe to re-run: it reuses what exists.
    public static class CharacterSetup
    {
        const string Root = CharacterImportRules.Root;
        const string MatDir = "Assets/NeonProtocol/Art/Materials";

        public static Material SuitMaterial(string slot, Color emission)
        {
            string path = $"{MatDir}/M_Suit_{slot}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("NeonProtocol/Character")); AssetDatabase.CreateAsset(m, path); }
            string id = slot == "Body" ? "1001" : "1002";
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/Ch15_{id}_Diffuse.png"));
            m.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/Ch15_{id}_Normal.png"));
            m.SetTexture("_GlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/Ch15_{id}_Glossiness.png"));
            var emi = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/Ch15_{id}_Emissive.png");
            m.SetTexture("_EmissionMap", emi ? emi : Texture2D.blackTexture);
            m.SetColor("_EmissionColor", emission);
            m.SetColor("_BaseColor", slot == "Body" ? new Color(0.16f, 0.165f, 0.2f) : new Color(0.12f, 0.12f, 0.15f));
            m.SetColor("_RimColor", new Color(0.16f, 0.12f, 0.36f)); m.SetFloat("_RimPower", 4.5f);
            m.SetFloat("_Smoothness", 0.65f); m.SetFloat("_Metallic", 0.2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("Neon Protocol/Setup Player Character")]
        public static string SetupPlayer()
        {
            var player = GameObject.Find("Player");
            if (!player) return "no Player in scene";
            var placeholder = player.transform.Find("PlaceholderBody");
            if (placeholder) Object.DestroyImmediate(placeholder.gameObject);

            var model = player.transform.Find("Model")?.gameObject;
            if (!model)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Swat.fbx");
                model = (GameObject)PrefabUtility.InstantiatePrefab(src, player.transform);
                model.name = "Model";
            }
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 8;

            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.sharedMaterials = new[] { SuitMaterial("Body", new Color(0.1f, 1.6f, 1.5f)), SuitMaterial("Gear", new Color(0.15f, 2.4f, 2.2f)) };
            smr.updateWhenOffscreen = false;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var anim = model.GetComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorBuilder.ControllerPath);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Rifle: placed by CharacterAnimator every frame; grips are IK goals for the hands.
            var rifle = player.transform.Find("PaintRifle");
            Transform Grip(string n, Vector3 lp, Vector3 le)
            {
                var g = rifle.Find(n) ?? new GameObject(n).transform;
                g.SetParent(rifle, false); g.localPosition = lp; g.localEulerAngles = le; return g;
            }
            var rg = Grip("RightGrip", new Vector3(0.0f, -0.07f, -0.06f), new Vector3(0, 0, -90));
            var lg = Grip("LeftGrip", new Vector3(0.0f, -0.05f, 0.3f), new Vector3(0, 0, 90));

            var ca = GetOrAdd<CharacterAnimator>(model);
            ca.weapon = rifle; ca.rightGrip = rg; ca.leftGrip = lg;

            var pd = GetOrAdd<PaintDynamic>(model);
            pd.textureSize = 512; pd.renderers = new Renderer[] { smr };

            // Hit capsule for paint (the CharacterController is on the Player root, layer Player)
            var motor = player.GetComponent<PlayerMotor>();
            motor.crouchSpeed = 1.9f;

            // Audio
            var pr = rifle.GetComponent<PaintRifle>();
            pr.fireClips = Clips("Generated/rifle_shot_");
            pr.reloadClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Generated/reload.wav");
            pr.emptyClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/NeonProtocol/Audio/Generated/empty.wav");
            var fx = Object.FindFirstObjectByType<ImpactFX>();
            if (fx)
            {
                fx.splatClips = Clips("Generated/splat_").Concat(Clips("Kenney/slime_")).ToArray();
                fx.hitClips = Clips("Generated/hit_confirm_");
                EditorUtility.SetDirty(fx);
            }
            var steps = GetOrAdd<Footsteps>(player);
            steps.clips = Clips("Kenney/footstep_concrete_");

            EditorUtility.SetDirty(player);
            EditorSceneManager.MarkSceneDirty(player.scene);
            EditorSceneManager.SaveOpenScenes();
            return $"player set up: model={model.name} mats={smr.sharedMaterials.Length} clips fire={pr.fireClips.Length} steps={steps.clips.Length}";
        }

        [MenuItem("Neon Protocol/Setup Rifle Model")]
        public static string SetupRifle()
        {
            var player = GameObject.Find("Player");
            var rifle = player.transform.Find("PaintRifle");
            foreach (var n in new[] { "RifleBody" }) { var o = rifle.Find(n); if (o) Object.DestroyImmediate(o.gameObject); }
            var mesh = rifle.Find("Mesh");
            if (!mesh)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NeonProtocol/Art/Models/Blasters/blaster-d.fbx");
                mesh = ((GameObject)PrefabUtility.InstantiatePrefab(src, rifle)).transform;
                mesh.name = "Mesh";
            }
            mesh.localPosition = new Vector3(0, 0.1f, 0.1f); mesh.localEulerAngles = new Vector3(0, 180, 0); mesh.localScale = Vector3.one;
            string mp = $"{MatDir}/M_Rifle.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!m) { m = new Material(Shader.Find("NeonProtocol/Character")); AssetDatabase.CreateAsset(m, mp); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/NeonProtocol/Art/Models/Blasters/Textures/colormap.png"));
            m.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.24f)); m.SetFloat("_Desaturate", 0.8f);
            m.SetFloat("_Smoothness", 0.9f); m.SetFloat("_Metallic", 0.6f);
            m.SetTexture("_GlossMap", Texture2D.grayTexture); m.SetColor("_EmissionColor", Color.black);
            m.SetColor("_RimColor", new Color(0.12f, 0.1f, 0.25f)); EditorUtility.SetDirty(m);
            foreach (var r in mesh.GetComponentsInChildren<Renderer>()) { r.sharedMaterial = m; r.gameObject.layer = 8; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
            var tank = rifle.Find("PaintTank");
            if (tank) { tank.localPosition = new Vector3(0.0f, 0.215f, 0.02f); tank.localScale = Vector3.one; var nb = tank.GetComponent<NeonBlock>(); nb.size = new Vector3(0.07f, 0.06f, 0.2f); NeonBlockTools.Rebuild(nb); }
            var muzzle = rifle.Find("Muzzle"); muzzle.localPosition = new Vector3(0, 0.16f, 0.57f);
            var rg = rifle.Find("RightGrip"); rg.localPosition = new Vector3(0.0f, -0.04f, -0.03f);
            var lg = rifle.Find("LeftGrip"); lg.localPosition = new Vector3(0.0f, 0.05f, 0.33f);
            EditorSceneManager.MarkSceneDirty(player.scene); EditorSceneManager.SaveOpenScenes();
            return "rifle set up";
        }

        static T GetOrAdd<T>(GameObject go) where T : Component { var c = go.GetComponent<T>(); return c ? c : go.AddComponent<T>(); }

        static AudioClip[] Clips(string prefix)
        {
            string dir = "Assets/NeonProtocol/Audio/" + prefix.Substring(0, prefix.LastIndexOf('/'));
            string start = prefix.Substring(prefix.LastIndexOf('/') + 1);
            return AssetDatabase.FindAssets("t:AudioClip", new[] { dir })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => System.IO.Path.GetFileName(p).StartsWith(start)).OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
        }
    }
}
