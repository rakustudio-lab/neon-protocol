using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Rescue art direction (OPUS_RESCUE_ORDER §3, founder feedback): bright stylized sci-fi instead of black + glow lines.
    //  * warm off-white structure, medium blue-grey floor, deep blue recesses, matte trim, a few amber landmarks
    //  * luminous grid/strip lines removed from architecture: glow material SLOTS on kit meshes become matte trim
    //    (geometry kept), procedural floor seam glow and wall trim lines off
    //  * the opening statistics board and the floating title are hidden from the entry view
    //  * one warm key direction + brighter ambient explain the shapes; bloom restrained
    // Gameplay objects (relays, core, Iris), route chevrons and the Finality track are not touched here.
    // Re-runnable; the original state is in git (scene + material assets).
    public static class R1Art
    {
        const string Dir = "Assets/NeonProtocol/Art/Materials/R1";
        static readonly HashSet<string> GlowNames = new HashSet<string> { "G_Violet", "G_Magenta", "G_Cyan", "M_Glow", "M_GlowCyan" };

        static Material Find(string n)
        {
            foreach (var id in AssetDatabase.FindAssets(n + " t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(id));
                if (m && m.name == n) return m;
            }
            return null;
        }

        static Material Matte(string n, Color c, float smooth)
        {
            System.IO.Directory.CreateDirectory(Dir);
            string p = $"{Dir}/{n}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m); return m;
        }

        static void Surf(string n, Color baseC, float metal, float smooth, float seamGlow = 0f, float trimSpacing = 0f, float grime = -1f)
        {
            var m = Find(n); if (!m) return;
            m.SetColor("_BaseColor", baseC); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_SeamGlow")) m.SetFloat("_SeamGlow", seamGlow);
            if (m.HasProperty("_TrimSpacing")) m.SetFloat("_TrimSpacing", trimSpacing);
            if (grime >= 0f && m.HasProperty("_Grime")) m.SetFloat("_Grime", grime);
            if (m.HasProperty("_EmissionColor") && seamGlow <= 0f && trimSpacing <= 0f) m.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(m);
        }

        static bool Protected(Transform t)
        {
            for (; t; t = t.parent)
            {
                string n = t.name;
                if (n == "Gameplay" || n == "ArtPass_RouteA" || n == "FinalityRun" || n.StartsWith("Relay_") || n == "CoreObjective" || n.Contains("Elevator")) return true;
            }
            return false;
        }

        [MenuItem("Neon Protocol/Art/R1 Bright Direction")]
        public static string Apply()
        {
            // 1) palette (material assets)
            Surf("M_Obsidian", new Color(0.74f, 0.72f, 0.70f), 0f, 0.32f, grime: 0.2f);     // warm off-white structure
            Surf("M_TrimWall", new Color(0.60f, 0.61f, 0.66f), 0.05f, 0.38f);                 // light grey, no glowing trim lines
            Surf("M_Floor", new Color(0.34f, 0.36f, 0.44f), 0.05f, 0.42f, grime: 0.25f);      // medium blue-grey floor
            Surf("M_Floor_Vault", new Color(0.26f, 0.27f, 0.36f), 0.05f, 0.45f);
            Surf("M_Graphite", new Color(0.12f, 0.15f, 0.30f), 0.1f, 0.4f);                   // deep blue recesses
            Surf("M_Metal", new Color(0.52f, 0.54f, 0.60f), 0.4f, 0.45f);
            var kd = Find("K_Dark"); if (kd) { kd.SetColor("_BaseColor", new Color(0.09f, 0.12f, 0.26f)); kd.SetFloat("_Metallic", 0.1f); kd.SetFloat("_Smoothness", 0.4f); EditorUtility.SetDirty(kd); }
            var kp = Find("K_Plate"); if (kp) { kp.SetColor("_BaseColor", new Color(0.86f, 0.84f, 0.82f)); kp.SetFloat("_Metallic", 0f); kp.SetFloat("_Smoothness", 0.3f); EditorUtility.SetDirty(kp); }
            var kt = Find("K_Trim"); if (kt) { kt.SetColor("_BaseColor", new Color(0.30f, 0.32f, 0.40f)); EditorUtility.SetDirty(kt); }
            var trimMatte = Matte("M_R1_TrimMatte", new Color(0.16f, 0.19f, 0.34f), 0.35f);   // replaces glow slots: thin dark-blue joint trim

            // 2) remove luminous lines from architecture: glow slots -> matte trim (geometry stays)
            int swapped = 0, renderers = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.transform.position.x > 500f || Protected(r.transform)) continue;
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++) if (mats[i] && GlowNames.Contains(mats[i].name)) { mats[i] = trimMatte; changed = true; swapped++; }
                if (changed) { r.sharedMaterials = mats; renderers++; EditorUtility.SetDirty(r); }
            }

            // 3) opening view: no statistics board, no floating title (the menu carries the title)
            int hidden = 0;
            foreach (var n in new[] { "Architecture/BlockReactor_Arrival", "ArtPass_Arrival/HoloTitle" })
            { var g = GameObject.Find(n); if (g) { g.SetActive(false); hidden++; } }

            // 4) light: one warm key direction + brighter, cooler-above ambient; restrained bloom
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { l.color = new Color(1f, 0.94f, 0.86f); l.intensity = 1.6f; l.transform.rotation = Quaternion.Euler(52f, -38f, 0f); EditorUtility.SetDirty(l); }
            // coloured point/spot lights tinted the new off-white structure lavender/cyan: cap them and pull their colour
            // halfway to warm white (converges when re-run). Gameplay and route-guidance lights keep their colour.
            int lit = 0; var warm = new Color(1f, 0.93f, 0.84f);
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional || l.transform.position.x > 500f || Protected(l.transform)) continue;
                l.intensity = Mathf.Min(l.intensity, 3f); l.color = Color.Lerp(l.color, warm, 0.5f); EditorUtility.SetDirty(l); lit++;
            }
            var amb = Object.FindFirstObjectByType<AmbientFromTrilight>();
            if (amb) { amb.sky = new Color(0.62f, 0.64f, 0.72f); amb.equator = new Color(0.52f, 0.50f, 0.52f); amb.ground = new Color(0.28f, 0.26f, 0.32f); amb.intensity = 1.25f; amb.Apply(); EditorUtility.SetDirty(amb); }
            var vp = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>("Assets/NeonProtocol/Art/Materials/NP_GamePost.asset");
            if (vp)
            {
                if (vp.TryGet(out UnityEngine.Rendering.Universal.Bloom bl)) { bl.intensity.value = 0.3f; bl.threshold.value = 1.3f; }
                if (vp.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments ca)) { ca.postExposure.value = 0.15f; ca.contrast.value = 6f; ca.saturation.value = 4f; }
                if (vp.TryGet(out UnityEngine.Rendering.Universal.Vignette vg)) vg.intensity.value = 0.15f;
                EditorUtility.SetDirty(vp);
            }
            // no black voids: deep-blue sky tone behind openings, pale haze for depth instead of near-black fog
            var cam = Camera.main; if (cam) { cam.backgroundColor = new Color(0.16f, 0.18f, 0.34f); EditorUtility.SetDirty(cam); }
            RenderSettings.fogColor = new Color(0.40f, 0.42f, 0.54f); RenderSettings.fogDensity = 0.008f;
            var paint = Object.FindFirstObjectByType<PaintSystem>(); if (paint) { paint.glow = 0.35f; EditorUtility.SetDirty(paint); }   // matte paint, no white-hot sheets

            AssetDatabase.SaveAssets();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return $"R1 art: glow slots swapped {swapped} on {renderers} renderers; hidden {hidden}; lights softened {lit}";
        }
    }
}
