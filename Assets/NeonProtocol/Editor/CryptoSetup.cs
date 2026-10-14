using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Swaps the Player's body to CRYPTO (Mixamo Ch45_nonPBR). Keeps every gameplay component on the Player root;
    // the rifle is lifted off the old hand, the old body removed (scene is in git), the new body wired to the same
    // humanoid controller, then the rifle is re-calibrated onto CRYPTO's right hand from the real aim clip.
    public static class CryptoSetup
    {
        const string Fbx = CharacterImportRules.CryptoRoot + "/Ch45_nonPBR.fbx";
        const string Tex = CharacterImportRules.CryptoRoot + "/Textures/Ch45_1001_";

        public static Material CryptoMaterial()
        {
            const string path = "Assets/NeonProtocol/Art/Materials/M_Crypto_Body.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("NeonProtocol/Character")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "Diffuse.png"));
            m.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "Normal.png"));
            m.SetTexture("_GlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "Glossiness.png"));
            m.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "Emissive.png"));
            m.SetColor("_EmissionColor", new Color(0.2f, 2.2f, 2.0f));        // the suit's chest/belt lights, pushed to the game's cyan
            m.SetColor("_BaseColor", new Color(0.6f, 0.63f, 0.66f)); m.SetFloat("_Desaturate", 0.55f);   // keep the suit's graphite-teal
            m.SetColor("_RimColor", new Color(0.12f, 0.09f, 0.24f)); m.SetFloat("_RimPower", 5f);
            m.SetFloat("_Smoothness", 0.55f); m.SetFloat("_Metallic", 0.15f);
            EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("Neon Protocol/Player: Use CRYPTO")]
        public static string Apply()
        {
            var player = GameObject.Find("Player");
            if (!player) return "no Player";
            var old = player.transform.Find("Model");
            var oldCa = old ? old.GetComponent<CharacterAnimator>() : null;
            var rifle = oldCa && oldCa.weapon ? oldCa.weapon : player.GetComponentInChildren<PaintRifle>(true)?.transform;
            if (!rifle) return "no rifle";
            if (old && old.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("mixamorig1:"))) return "already CRYPTO";

            rifle.SetParent(player.transform, true);
            int paintTeam = old && old.GetComponent<PaintDynamic>() ? old.GetComponent<PaintDynamic>().team : 0;
            float spinePitch = oldCa ? oldCa.spinePitchShare : 0.85f, maxYaw = oldCa ? oldCa.maxSpineYaw : 35f, maxHand = oldCa ? oldCa.maxHandCorrection : 14f;
            if (old) Object.DestroyImmediate(old.gameObject);

            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src, player.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
            // Ch45 stands ~1.98 m in T-pose bounds (head-top ~1.9 m); scale to the 1.8 m controller
            model.transform.localScale = Vector3.one * 0.95f;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 8;

            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.sharedMaterials = new[] { CryptoMaterial() };
            smr.updateWhenOffscreen = false;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var anim = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            anim.avatar = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Avatar>().First();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorBuilder.ControllerPath);
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var ca = model.AddComponent<CharacterAnimator>();
            ca.weapon = rifle; ca.spinePitchShare = spinePitch; ca.maxSpineYaw = maxYaw; ca.maxHandCorrection = maxHand;
            var pd = model.AddComponent<PaintDynamic>();
            pd.textureSize = 512; pd.renderers = new Renderer[] { smr }; pd.team = paintTeam;

            EditorUtility.SetDirty(player);
            EditorSceneManager.MarkSceneDirty(player.scene);
            EditorSceneManager.SaveOpenScenes();
            string cal = WeaponRig.CalibrateAll();
            return $"CRYPTO on Player (verts {smr.sharedMesh.vertexCount}) | {cal}";
        }

        static Material RifleMat(string n, Color baseC, Color emit, float smooth, float metal)
        {
            string path = $"Assets/NeonProtocol/Art/Materials/M_CRifle_{n}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("NeonProtocol/Character")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", Texture2D.whiteTexture); m.SetTexture("_NormalMap", null);
            m.SetTexture("_GlossMap", Texture2D.whiteTexture); m.SetTexture("_EmissionMap", Texture2D.whiteTexture);
            m.SetColor("_BaseColor", baseC); m.SetColor("_EmissionColor", emit); m.SetFloat("_Desaturate", 0f);
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            m.SetColor("_RimColor", new Color(0.1f, 0.08f, 0.2f)); m.SetFloat("_RimPower", 5f);
            m.enableInstancing = false; EditorUtility.SetDirty(m);
            return m;
        }

        // Replaces the toy blaster mesh on the player's rifle with the custom CRYPTO paint rifle (Tools/blender/crypto_rifle.py).
        [MenuItem("Neon Protocol/Player: CRYPTO Rifle Model")]
        public static string ApplyRifle()
        {
            var player = GameObject.Find("Player");
            var rifle = player.GetComponentInChildren<PaintRifle>(true).transform;
            var old = rifle.Find("Mesh"); if (old) Object.DestroyImmediate(old.gameObject);
            var tank = rifle.Find("PaintTank"); if (tank) Object.DestroyImmediate(tank.gameObject);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NeonProtocol/Art/Models/CryptoRifle/crypto_rifle.fbx");
            var mesh = ((GameObject)PrefabUtility.InstantiatePrefab(src, rifle)).transform;
            mesh.name = "Mesh"; mesh.localPosition = Vector3.zero; mesh.localEulerAngles = new Vector3(0, 180, 0); mesh.localScale = Vector3.one;
            var body = RifleMat("Body", new Color(0.05f, 0.052f, 0.062f), Color.black, 0.62f, 0.7f);
            var trim = RifleMat("Trim", new Color(0.24f, 0.245f, 0.27f), Color.black, 0.78f, 0.9f);
            var glow = RifleMat("Glow", new Color(0.02f, 0.02f, 0.02f), new Color(0.15f, 2.6f, 2.3f), 0.5f, 0f);
            var glass = RifleMat("Canister", new Color(0.25f, 0.05f, 0.45f), new Color(1.3f, 0.25f, 2.6f), 0.9f, 0f);
            foreach (var r in mesh.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] ? mats[i].name : "";
                    mats[i] = n.Contains("Glow") ? glow : n.Contains("Canister") ? glass : n.Contains("Trim") ? trim : body;
                }
                r.sharedMaterials = mats; r.gameObject.layer = 8;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            rifle.Find("Muzzle").localPosition = new Vector3(0, 0.05f, 0.565f);
            rifle.Find("RightGrip").localPosition = new Vector3(0, -0.045f, -0.012f);
            rifle.Find("LeftGrip").localPosition = new Vector3(0, -0.01f, 0.29f);
            EditorSceneManager.MarkSceneDirty(player.scene); EditorSceneManager.SaveOpenScenes();
            CryptoMaterial();
            return "CRYPTO rifle mesh on | " + WeaponRig.CalibrateAll();
        }

        [MenuItem("Neon Protocol/Player: Rifle Reload Visual")]
        public static string SetupReload()
        {
            var player = GameObject.Find("Player");
            var pr = player.GetComponentInChildren<PaintRifle>(true);
            var v = pr.GetComponent<RifleReloadVisual>() ?? pr.gameObject.AddComponent<RifleReloadVisual>();
            var mesh = pr.transform.Find("Mesh");
            v.rifle = pr; v.body = player.GetComponentInChildren<CharacterAnimator>();
            v.canister = mesh.Find("Canister"); v.canisterRenderer = v.canister.GetComponent<Renderer>();
            v.bodyRenderer = mesh.Find("Rifle_Body").GetComponent<Renderer>();
            v.audioSource = pr.fireSource;
            AudioClip C(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/NeonProtocol/Audio/Generated/{n}.wav");
            v.releaseClip = C("reload_release"); v.dropClip = C("reload_drop"); v.insertClip = C("reload_insert"); v.pressurizeClip = C("reload_pressurize");
            pr.reloadClip = null;   // the visual plays the per-phase sounds
            EditorUtility.SetDirty(v); EditorUtility.SetDirty(pr);
            EditorSceneManager.MarkSceneDirty(player.scene); EditorSceneManager.SaveOpenScenes();
            return $"reload visual: canister={(v.canister != null)} body={(v.bodyRenderer != null)} src={(v.audioSource != null)}";
        }
    }
}
