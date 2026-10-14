using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace NeonProtocol.EditorTools
{
    // Rescue 2026-10-11: every human rival is built on CRYPTO (Ch45 + the same animator, rifle and IK) with a faction suit.
    //   team 1 = VECTOR crew (crimson), team 2 = HALO syndicate (gold). A 4th rival (ORBIT, HALO) is added so each
    //   faction has two members. Re-runnable: replaces each rival's "Model" child; patrol routes, colliders, agents kept.
    public static class RivalRebuild
    {
        const string MatDir = "Assets/NeonProtocol/Art/Materials";

        static Material Suit(string name, Color baseC, Color emission, Color rim)
        {
            string path = $"{MatDir}/M_Faction_{name}.mat";
            var src = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Crypto_Body.mat");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            m.CopyPropertiesFromMaterial(src);
            m.SetColor("_BaseColor", baseC); m.SetColor("_EmissionColor", emission); m.SetColor("_RimColor", rim);
            m.SetFloat("_Desaturate", 0.7f); m.SetFloat("_RimPower", 3.2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("Neon Protocol/Rebuild Rivals On CRYPTO")]
        public static string Build()
        {
            var log = new List<string>();
            var player = GameObject.Find("Player"); var pModel = player.transform.Find("Model");
            // the player's suit: brighter rim so CRYPTO reads against the dark set
            var crypto = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Crypto_Body.mat");
            crypto.SetColor("_RimColor", new Color(0.28f, 0.42f, 0.85f)); crypto.SetFloat("_RimPower", 3.0f); EditorUtility.SetDirty(crypto);

            var vector = Suit("VECTOR", new Color(0.62f, 0.3f, 0.36f), new Color(2.8f, 0.25f, 0.7f), new Color(0.75f, 0.12f, 0.3f));
            var halo = Suit("HALO", new Color(0.68f, 0.58f, 0.38f), new Color(2.8f, 1.6f, 0.2f), new Color(0.75f, 0.5f, 0.12f));

            var rivals = new List<CompetitorAI>(Object.FindObjectsByType<CompetitorAI>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            // 4th rival for the HALO syndicate: a clone of HALO patrolling the trench / west side
            if (!rivals.Exists(r => r.name == "Rival_ORBIT"))
            {
                var haloAi = rivals.Find(r => r.name.Contains("HALO"));
                var orbit = Object.Instantiate(haloAi.gameObject, haloAi.transform.parent); orbit.name = "Rival_ORBIT";
                var routeRoot = new GameObject("Route_ORBIT").transform; routeRoot.SetParent(haloAi.transform.parent);
                var pts = new[] { new Vector3(-20.5f, 0f, -18f), new Vector3(-20.5f, 0f, 4f), new Vector3(-9f, 0f, 6f), new Vector3(-9f, 0f, -12f) };
                var tr = new Transform[pts.Length];
                for (int i = 0; i < pts.Length; i++) { tr[i] = new GameObject("P" + i).transform; tr[i].SetParent(routeRoot); tr[i].position = pts[i]; }
                var oa = orbit.GetComponent<CompetitorAI>(); oa.patrol = tr; oa.displayName = "ORBIT";
                orbit.transform.position = pts[0];
                rivals.Add(oa);
                log.Add("added ORBIT");
            }

            foreach (var ai in rivals)
            {
                bool isHalo = ai.name.Contains("HALO") || ai.name.Contains("ORBIT");
                ai.team = isHalo ? 2 : 1;
                var th = ai.GetComponent<TagHealth>(); th.team = ai.team;
                var old = ai.transform.Find("Model"); if (old) Object.DestroyImmediate(old.gameObject);
                var oldRifle = ai.transform.Find("PaintRifle"); if (oldRifle) Object.DestroyImmediate(oldRifle.gameObject);
                var m = Object.Instantiate(pModel.gameObject, ai.transform); m.name = "Model";
                m.transform.localPosition = Vector3.zero; m.transform.localRotation = Quaternion.identity;
                // strip player-only behaviour from the copy (input-driven rifle, reload visual, its audio)
                foreach (var c in m.GetComponentsInChildren<RifleReloadVisual>(true)) Object.DestroyImmediate(c);
                foreach (var c in m.GetComponentsInChildren<PaintRifle>(true)) Object.DestroyImmediate(c);
                foreach (var c in m.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(c);
                foreach (var c in m.GetComponentsInChildren<PaintDynamic>(true)) Object.DestroyImmediate(c);
                foreach (var t in m.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 9;
                var smr = m.GetComponentInChildren<SkinnedMeshRenderer>();
                var mats = smr.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = isHalo ? halo : vector; smr.sharedMaterials = mats;
                var ca = m.GetComponent<CharacterAnimator>();
                ai.muzzle = ca.muzzle;
                var pd = ai.GetComponent<PaintDynamic>(); pd.renderers = new Renderer[] { smr };
                var anim = m.GetComponent<Animator>(); anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                EditorUtility.SetDirty(ai); EditorUtility.SetDirty(th); EditorUtility.SetDirty(pd);
                log.Add($"{ai.name}: team {ai.team}");
            }
            EditorSceneManager.MarkSceneDirty(player.scene);
            EditorSceneManager.SaveScene(player.scene);
            AssetDatabase.SaveAssets();
            return string.Join(" | ", log);
        }
    }
}
