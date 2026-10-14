using UnityEditor;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Imports the CC0 Quaternius Ultimate Modular Sci-Fi kit with ONE project palette (URP Lit), remapping the 7
    // material names every FBX embeds. Colour language (OPUS_NEXT_BRIEF): warm off-white structure, deep-blue
    // recesses, restrained violet identity (the kit's orange accents), amber only for direction cues we place.
    public static class QuaterniusSetup
    {
        public const string Kit = "Assets/ThirdParty/Quaternius_UltimateModularSciFi";
        public const string MatDir = "Assets/NeonProtocol/Art/Materials/R2";

        static Material Mat(string n, Color c, float smooth, float metal = 0f, Color? emission = null)
        {
            System.IO.Directory.CreateDirectory(MatDir);
            string p = $"{MatDir}/{n}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m); return m;
        }

        public static Material Palette(string kitName) => kitName switch
        {
            "Main" => Mat("R2_Main", new Color(0.84f, 0.82f, 0.78f), 0.32f),
            "Light" => Mat("R2_Light", new Color(0.95f, 0.94f, 0.91f), 0.38f),
            "DarkGrey" => Mat("R2_Recess", new Color(0.17f, 0.21f, 0.37f), 0.45f, 0.1f),
            "Accent" => Mat("R2_Violet", new Color(0.58f, 0.54f, 0.78f), 0.5f, 0.1f),   // restrained: soft lavender, not saturated
            "DarkAccent" => Mat("R2_VioletDeep", new Color(0.27f, 0.22f, 0.50f), 0.45f, 0.1f),
            "Black" => Mat("R2_Ink", new Color(0.07f, 0.08f, 0.13f), 0.55f),
            "Glass" => Mat("R2_Glass", new Color(0.55f, 0.72f, 0.92f), 0.9f, 0f, new Color(0.10f, 0.16f, 0.26f)),
            _ => null
        };
        // floors read as one calm blue-grey surface: quiet seams instead of a dark grid
        public static Material Floor() => Mat("R2_Floor", new Color(0.56f, 0.60f, 0.70f), 0.42f);
        public static Material FloorSeam() => Mat("R2_FloorSeam", new Color(0.44f, 0.48f, 0.60f), 0.4f);
        public static Material GlassClear()
        {
            var m = Mat("R2_GlassClear", new Color(0.70f, 0.85f, 1f, 0.22f), 0.95f);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent; EditorUtility.SetDirty(m); return m;
        }
        public static Material CeilingPanel() => Mat("R2_CeilingPanel", new Color(1f, 0.98f, 0.94f), 0.2f, 0f, new Color(1.3f, 1.25f, 1.15f));
        public static Material Amber() => Mat("R2_Amber", new Color(0.9f, 0.62f, 0.25f), 0.4f, 0f, new Color(1.6f, 0.85f, 0.25f));
        public static Material Cyan() => Mat("R2_Cyan", new Color(0.3f, 0.85f, 0.95f), 0.4f, 0f, new Color(0.25f, 1.7f, 1.9f));

        [MenuItem("Neon Protocol/R2/Import Quaternius Kit (palette remap)")]
        public static string Setup()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Kit }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
                bool floor = System.IO.Path.GetFileName(path).StartsWith("FloorTile");
                foreach (var name in new[] { "Main", "Light", "DarkGrey", "Accent", "DarkAccent", "Black", "Glass" })
                {
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                    var m = floor && name == "Main" ? Floor() : floor && name == "DarkGrey" ? FloorSeam() : Palette(name);
                    if (m) imp.AddRemap(id, m);
                }
                imp.generateSecondaryUV = true;        // lightmap UVs for the baked encounter lighting
                imp.addCollider = false;               // colliders are authored per piece by the room builder
                imp.importAnimation = false; imp.animationType = ModelImporterAnimationType.None;
                imp.SaveAndReimport(); n++;
            }
            AssetDatabase.SaveAssets();
            return "remapped " + n + " kit models to the R2 palette";
        }
    }
}
