using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonProtocol.EditorTools
{
    // Additive art pass on THE FRACTURE: structured ceilings with light strips (no black void), stronger hero lighting,
    // the community art wall, and render-cost hygiene. Idempotent: skipped if "ArtPass" already exists.
    public static class FractureArtPass
    {
        const string MatDir = "Assets/NeonProtocol/Art/Materials";

        [MenuItem("Neon Protocol/Art Pass THE FRACTURE")]
        public static string Run()
        {
            if (GameObject.Find("ArtPass")) return "art pass already applied";
            var root = new GameObject("ArtPass").transform;
            var obsidian = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Obsidian.mat");
            var glow = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Glow.mat");
            var glowCyan = Glow("M_GlowCyan", new Color(0.25f, 2.2f, 2.4f));

            NeonBlock B(string n, Vector3 p, Vector3 s, Material m, bool paint = false, bool col = true)
            {
                var go = new GameObject(n); go.transform.SetParent(root, false); go.transform.position = p;
                go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>().sharedMaterial = m;
                var b = go.AddComponent<NeonBlock>(); b.size = s; b.chamfer = 0.04f; b.paintable = paint; b.addCollider = col;
                NeonBlockTools.Rebuild(b);
                if (!col) { var c = go.GetComponent<Collider>(); if (c) Object.DestroyImmediate(c); }
                return b;
            }
            // Hall + arrival ceilings with recessed light strips: gives the space a top and soft overhead light.
            B("Hall_Ceiling", new Vector3(-0.5f, 14.25f, -6f), new Vector3(46, 0.5f, 38), obsidian);
            B("Arrival_Ceiling", new Vector3(0, 14.25f, -29.5f), new Vector3(20, 0.5f, 12), obsidian);
            for (int i = 0; i < 5; i++)
            {
                var s = B("Ceil_Strip_" + i, new Vector3(-0.5f, 13.95f, -20f + i * 7f), new Vector3(36, 0.12f, 0.35f), i % 2 == 0 ? glow : glowCyan, false, false);
                s.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            // Vertical light blades on the hall walls (silhouette + readable routes)
            foreach (var z in new[] { -18f, -10f, -2f, 6f })
            {
                B("Blade_W", new Vector3(-22.95f, 3.2f, z), new Vector3(0.1f, 5.5f, 0.25f), glowCyan, false, false).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                B("Blade_E", new Vector3(21.95f, 3.2f, z - 4f), new Vector3(0.1f, 5.5f, 0.25f), glow, false, false).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            // Vault: glowing floor ring around the pedestal + strips framing the art wall
            B("Vault_FloorRing_N", new Vector3(0, 0.02f, 26.6f), new Vector3(7.2f, 0.04f, 0.18f), glowCyan, false, false);
            B("Vault_FloorRing_S", new Vector3(0, 0.02f, 19.4f), new Vector3(7.2f, 0.04f, 0.18f), glowCyan, false, false);
            B("Vault_FloorRing_E", new Vector3(3.6f, 0.02f, 23f), new Vector3(0.18f, 0.04f, 7.2f), glowCyan, false, false);
            B("Vault_FloorRing_W", new Vector3(-3.6f, 0.02f, 23f), new Vector3(0.18f, 0.04f, 7.2f), glowCyan, false, false);

            // Community art wall panel (texture supplied at runtime by the page; empty-state until then)
            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "ArtWall_Panel"; panel.transform.SetParent(root, false);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.position = new Vector3(0, 10.5f, 33.7f); panel.transform.rotation = Quaternion.Euler(0, 180, 0); panel.transform.localScale = new Vector3(5.6f, 5.6f, 1);
            var unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "M_ArtWall" };
            unlit.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.08f));
            AssetDatabase.CreateAsset(unlit, $"{MatDir}/M_ArtWall.mat");
            panel.GetComponent<Renderer>().sharedMaterial = unlit;
            panel.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var wallLight = new GameObject("ArtWall_Light").AddComponent<Light>(); wallLight.transform.SetParent(panel.transform, false);
            wallLight.transform.localPosition = new Vector3(0, 0, -0.6f); wallLight.type = LightType.Point; wallLight.range = 9f; wallLight.intensity = 2.5f; wallLight.color = new Color(0.6f, 0.5f, 1f); wallLight.enabled = false;
            var aw = panel.AddComponent<ArtWall>(); aw.panel = panel.GetComponent<Renderer>(); aw.glow = wallLight;

            // Lighting: brighter, more contrast between pools
            RenderSettings.ambientSkyColor = new Color(0.27f, 0.25f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.15f, 0.12f, 0.22f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.045f, 0.08f);
            RenderSettings.fogDensity = 0.011f; RenderSettings.fogColor = new Color(0.035f, 0.028f, 0.07f);
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Point && l.transform.parent && l.transform.parent.name == "Lighting") { l.intensity *= 1.5f; l.range *= 1.15f; }
            var moon = GameObject.Find("MoonLight")?.GetComponent<Light>(); if (moon) moon.intensity = 1.25f;
            var vol = Object.FindFirstObjectByType<Volume>();
            if (vol && vol.sharedProfile.TryGet<Bloom>(out var bloom)) { bloom.intensity.value = 0.85f; bloom.threshold.value = 1.0f; }
            if (vol && vol.sharedProfile.TryGet<ColorAdjustments>(out var ca)) { ca.postExposure.value = 0.75f; ca.contrast.value = 14f; }
            EditorUtility.SetDirty(vol.sharedProfile);

            // Render-cost hygiene: SRP Batcher handles architecture; instancing only where it's used (paintballs).
            foreach (var n in new[] { "M_Floor", "M_Obsidian", "M_Graphite", "M_TrimWall", "M_Metal", "M_Glow", "M_RelayCore", "M_CoreGlow", "M_GlowCyan", "M_Target", "M_CharacterPlaceholder" })
            { var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat"); if (m) { m.enableInstancing = false; EditorUtility.SetDirty(m); } }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            return "art pass applied";
        }

        static Material Glow(string n, Color hdr)
        {
            var path = $"{MatDir}/{n}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m) return m;
            m = new Material(AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Glow.mat")) { name = n };
            m.SetColor("_EmissionColor", hdr);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
