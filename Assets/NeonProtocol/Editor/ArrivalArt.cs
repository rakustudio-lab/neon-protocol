using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Rescue 2026-10-11: ARRIVAL PLATFORM art pass. The first thing a player sees.
    //  * The upper south wall becomes a 20 m panoramic window (glass + mullions) over a procedural neon megacity with a
    //    distant Monad spire, haze and a violet moon (all combined meshes: few draw calls for WebGL).
    //  * Interior: kit pillars, wall ribs and panels, ceiling trusses, hanging cables, edge rails with amber warning trim,
    //    a floor seal, a holographic title, warm amber key light + cyan/magenta accents, light shafts through the window.
    //  * Colliders untouched (gameplay identical). Re-runnable: ArtPass_Arrival is rebuilt; Revert() restores the walls.
    public static class ArrivalArt
    {
        const string Kit = "Assets/NeonProtocol/Art/Models/VaultKit/vault_kit.fbx";
        const string MatDir = "Assets/NeonProtocol/Art/Materials/Kit";
        static Transform root;
        static Dictionary<string, GameObject> pieces;
        static readonly string[] OpenWalls = { "Wall_S_Upper", "Wall_S_Upper_1_0", "Wall_S_Upper_2_0", "Wall_W_Upper", "Wall_E_Upper" };

        static Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

        static Material WindowGlass()
        {
            string path = $"{MatDir}/M_WindowGlass.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
            m.SetColor("_BaseColor", new Color(0.55f, 0.85f, 1f, 0.07f)); m.SetFloat("_Smoothness", 0.95f); m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m); return m;
        }

        static Material SkyGradient()
        {
            string tpath = "Assets/NeonProtocol/Art/Textures/T_SkyGradient.png", mpath = $"{MatDir}/M_SkyGradient.mat";
            if (!System.IO.File.Exists(tpath))
            {
                var tex = new Texture2D(4, 256, TextureFormat.RGBA32, false);
                for (int y = 0; y < 256; y++) { float t = y / 255f; var low = new Color(0.95f, 0.32f, 0.62f); var mid = new Color(0.42f, 0.14f, 0.62f); var top = new Color(0.02f, 0.02f, 0.07f); var c = t < 0.24f ? Color.Lerp(low, mid, Mathf.SmoothStep(0, 1, t / 0.24f)) : Color.Lerp(mid, top, Mathf.Pow((t - 0.24f) / 0.76f, 0.6f)); for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c); }
                System.IO.File.WriteAllBytes(tpath, tex.EncodeToPNG()); AssetDatabase.ImportAsset(tpath);
                var ti = (TextureImporter)AssetImporter.GetAtPath(tpath); ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = false; ti.SaveAndReimport();
            }
            var m = AssetDatabase.LoadAssetAtPath<Material>(mpath);
            if (!m) { m = new Material(Shader.Find("NeonProtocol/SkyBackdrop")); AssetDatabase.CreateAsset(m, mpath); }
            m.shader = Shader.Find("NeonProtocol/SkyBackdrop");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tpath)); m.SetColor("_Color", Color.white * 1.1f);
            EditorUtility.SetDirty(m); return m;
        }

        [MenuItem("Neon Protocol/Art/Arrival Platform Pass")]
        public static string Build()
        {
            var old = GameObject.Find("ArtPass_Arrival"); if (old) Object.DestroyImmediate(old);
            root = new GameObject("ArtPass_Arrival").transform;
            pieces = new Dictionary<string, GameObject>();
            foreach (var t in AssetDatabase.LoadAssetAtPath<GameObject>(Kit).GetComponentsInChildren<MeshFilter>(true)) pieces[t.name] = t.gameObject;

            // 1) open the window: hide the upper south wall renderers (colliders stay)
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (OpenWalls.Contains(r.name) && r.transform.parent && r.transform.parent.name == "01_Arrival") r.enabled = false;
            Window();
            Skyline();

            // 2) interior architecture (floor y=2, ceiling ~14, walls x=+-9.5, south z=-35.5, open north edge z=-24)
            const float y0 = 2.0f;
            foreach (float z in new[] { -33.6f, -27.4f })
            {
                Put("Pillar", new Vector3(-8.7f, y0, z), 90f, new Vector3(1f, 0.76f, 1f));
                Put("Pillar", new Vector3(8.7f, y0, z), -90f, new Vector3(1f, 0.76f, 1f));
            }
            Put("WallPanel", new Vector3(-9.0f, y0 + 0.4f, -30.5f), -90f);
            Put("WallPanel", new Vector3(9.0f, y0 + 0.4f, -30.5f), 90f);
            Put("WallRib", new Vector3(-9.05f, y0, -24.6f), -90f, new Vector3(1f, 0.76f, 1f));
            Put("WallRib", new Vector3(9.05f, y0, -24.6f), 90f, new Vector3(1f, 0.76f, 1f));
            Put("CeilingFrame", new Vector3(0f, 13.9f, -29.7f), 0f, new Vector3(0.92f, 1f, 0.6f));
            foreach (var p in new[] { new Vector3(-4.2f, 13.5f, -26.5f), new Vector3(3.6f, 13.5f, -32.6f), new Vector3(6.8f, 13.5f, -27.8f) }) Put("CableDrop", p, 0f);
            Put("PipeRun", new Vector3(-5f, 12.6f, -35.0f), 180f); Put("PipeRun", new Vector3(5f, 12.6f, -35.0f), 180f);
            // edge rails either side of the stairs, amber warning trim
            foreach (float x in new[] { -7.6f, -5.4f, 5.4f, 7.6f }) Put("Rail", new Vector3(x, y0, -24.15f), 0f, new Vector3(0.55f, 1f, 1f));
            var seal = Put("FloorSeal", new Vector3(0f, y0 + 0.012f, -30.2f), 0f, new Vector3(0.6f, 1f, 0.6f)); seal.isStatic = true;
            Put("Capacitor", new Vector3(7.6f, y0, -34.4f), 0f, new Vector3(0.8f, 0.8f, 0.8f));
            Put("Capacitor", new Vector3(-7.6f, y0, -34.4f), 0f, new Vector3(0.8f, 0.8f, 0.8f));

            // 3) holographic title above the window
            var title = new GameObject("HoloTitle"); title.transform.SetParent(root);
            title.transform.position = new Vector3(0f, 12.1f, -34.7f); title.transform.rotation = Quaternion.Euler(0, 180f, 0);
            var tmp = title.AddComponent<TextMeshPro>();
            tmp.text = "NEON PROTOCOL  ·  THE FRACTURE"; tmp.fontSize = 9f; tmp.alignment = TextAlignmentOptions.Center;
            tmp.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/NeonProtocol/Art/Fonts/Orbitron-900 SDF.asset");
            tmp.color = new Color(0.55f, 1f, 1f, 0.95f); tmp.rectTransform.sizeDelta = new Vector2(20f, 2f);
            tmp.fontMaterial.SetColor("_FaceColor", new Color(1.6f, 3.2f, 3.4f));

            // 4) light: warm key through the window, accents, shafts, dust
            var key = new GameObject("L_ArrivalKey").AddComponent<Light>(); key.transform.SetParent(root);
            key.type = LightType.Spot; key.transform.position = new Vector3(0f, 12.5f, -36.5f); key.transform.rotation = Quaternion.Euler(38f, 0f, 0f);
            key.color = new Color(1f, 0.62f, 0.32f); key.intensity = 26f; key.range = 30f; key.spotAngle = 95f; key.shadows = LightShadows.None;
            Pt("L_ArrivalCyanW", new Vector3(-7.5f, 6f, -30f), new Color(0.2f, 0.95f, 1f), 7f, 12f);
            Pt("L_ArrivalMagE", new Vector3(7.5f, 6f, -30f), new Color(1f, 0.25f, 0.85f), 7f, 12f);
            Pt("L_ArrivalFloor", new Vector3(0f, 3.2f, -29f), new Color(0.6f, 0.45f, 1f), 3.5f, 9f);
            var cone = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/NeonProtocol/Art/Models/VaultKit/ShaftCone.asset");
            if (cone) foreach (float x in new[] { -5.5f, 0f, 5.5f })
            {
                var sh = new GameObject("Shaft", typeof(MeshFilter), typeof(MeshRenderer)); sh.transform.SetParent(root);
                sh.transform.position = new Vector3(x, 13.2f, -35.2f); sh.transform.rotation = Quaternion.Euler(-36f, 0f, 0f); sh.transform.localScale = new Vector3(0.9f, 0.62f, 0.9f);
                sh.GetComponent<MeshFilter>().sharedMesh = cone; var mr = sh.GetComponent<MeshRenderer>(); mr.sharedMaterial = M("Beam_Shaft"); NoShadow(mr);
            }
            Dust(new Vector3(0f, 7f, -30f), new Vector3(17f, 10f, 10f));

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            EditorSceneManager.SaveScene(root.gameObject.scene);
            return $"arrival pass: {root.childCount} objects";
        }

        [MenuItem("Neon Protocol/Art/Arrival Platform Revert")]
        public static string Revert()
        {
            var old = GameObject.Find("ArtPass_Arrival"); if (old) Object.DestroyImmediate(old);
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (OpenWalls.Contains(r.name) && r.transform.parent && r.transform.parent.name == "01_Arrival") r.enabled = true;
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return "reverted";
        }

        // ---------------------------------------------------------------------------------------------------------
        static void Window()
        {
            float z = -35.45f, y0 = 6.55f, y1 = 14.1f, x0 = -10f, x1 = 10f;
            var glass = GameObject.CreatePrimitive(PrimitiveType.Quad); glass.name = "WindowGlass"; Object.DestroyImmediate(glass.GetComponent<Collider>());
            glass.transform.SetParent(root); glass.transform.position = new Vector3(0, (y0 + y1) / 2, z - 0.05f); glass.transform.rotation = Quaternion.Euler(0, 180f, 0);
            glass.transform.localScale = new Vector3(x1 - x0, y1 - y0, 1); var gr = glass.GetComponent<MeshRenderer>(); gr.sharedMaterial = WindowGlass(); NoShadow(gr); gr.enabled = false;   // reflections washed out the view: open frame reads better
            // mullions + transom (trim), amber sill
            var parts = new List<(Vector3 c, Vector3 s, string m)>();
            for (int i = 0; i <= 5; i++) parts.Add((new Vector3(Mathf.Lerp(x0, x1, i / 5f), (y0 + y1) / 2, z), new Vector3(0.22f, y1 - y0, 0.35f), "K_Trim"));
            parts.Add((new Vector3(0, y0 + 2.6f, z), new Vector3(x1 - x0, 0.16f, 0.3f), "K_Trim"));
            parts.Add((new Vector3(0, y0 + 0.08f, z + 0.05f), new Vector3(x1 - x0, 0.16f, 0.45f), "G_Amber"));
            parts.Add((new Vector3(0, y1 - 0.1f, z), new Vector3(x1 - x0, 0.25f, 0.4f), "K_Trim"));
            foreach (var (c, s, m) in parts) Box("Mullion", c, s, M(m));
            // side windows (upper east/west walls)
            foreach (float sx in new[] { -9.45f, 9.45f })
            {
                float za = -36.05f, zb = -29.55f;
                var g2 = GameObject.CreatePrimitive(PrimitiveType.Quad); g2.name = "SideGlass"; Object.DestroyImmediate(g2.GetComponent<Collider>());
                g2.transform.SetParent(root); g2.transform.position = new Vector3(sx + Mathf.Sign(sx) * 0.05f, (y0 + y1) / 2, (za + zb) / 2);
                g2.transform.rotation = Quaternion.Euler(0, sx < 0 ? -90f : 90f, 0); g2.transform.localScale = new Vector3(zb - za, y1 - y0, 1);
                var r2 = g2.GetComponent<MeshRenderer>(); r2.sharedMaterial = WindowGlass(); NoShadow(r2); r2.enabled = false;
                for (int i = 0; i <= 2; i++) Box("Mullion", new Vector3(sx, (y0 + y1) / 2, Mathf.Lerp(za, zb, i / 2f)), new Vector3(0.35f, y1 - y0, 0.22f), M("K_Trim"));
                Box("Mullion", new Vector3(sx, y0 + 2.6f, (za + zb) / 2), new Vector3(0.3f, 0.16f, zb - za), M("K_Trim"));
                Box("Sill", new Vector3(sx, y0 + 0.08f, (za + zb) / 2), new Vector3(0.45f, 0.16f, zb - za), M("G_Amber"));
            }
        }

        // Procedural megacity: towers (one combined mesh), emissive window bands per colour (3 combined meshes),
        // rooftop beacons, a far Monad spire with rings, haze and a moon. Deterministic seed.
        static void Skyline()
        {
            var rnd = new System.Random(143);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var dark = new List<CombineInstance>(); var bands = new[] { new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>() };
            void Add(List<CombineInstance> l, Vector3 c, Vector3 s) => l.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(c, Quaternion.identity, s) });
            for (int i = 0; i < 80; i++)
            {
                float z, x;
                if (i < 48) { z = -R(60f, 230f); x = R(-1f, 1f) * (70f + -z * 1.0f); if (Mathf.Abs(x) < 24f) x = Mathf.Sign(x + 0.01f) * R(24f, 55f); }
                else { x = (rnd.NextDouble() < 0.5 ? -1f : 1f) * R(45f, 220f); z = R(-220f, -10f); }   // flanks, seen through the side windows
                float dist = Mathf.Max(-z, Mathf.Abs(x));
                float w = R(8f, 24f), d = R(8f, 24f), h = 45f + R(15f, 55f) + dist * R(0.12f, 0.38f), bw = 1f + dist / 90f;
                if (Mathf.Abs(x) < 9f && z > -120f) x += Mathf.Sign(x + 0.01f) * 14f;   // keep the centre view clear
                float yb = -45f;
                Add(dark, new Vector3(x, yb + h / 2, z), new Vector3(w, h, d));
                int col = rnd.Next(3);
                // rows of lit windows (thin horizontal strips with gaps) on the two faces we can see, plus one vertical accent
                float rowStep = R(4f, 7f) * bw;
                for (float yy = yb + h - rowStep; yy > yb + h * 0.35f; yy -= rowStep)
                {
                    if (rnd.NextDouble() < 0.15) continue;
                    float span = R(0.35f, 0.85f) * w, off = R(-0.5f, 0.5f) * (w - span);
                    Add(bands[col], new Vector3(x + off, yy, z + d / 2 + 0.05f), new Vector3(span, 0.28f * bw, 0.1f));
                    if (i >= 48 && rnd.NextDouble() < 0.6) Add(bands[col], new Vector3(x - Mathf.Sign(x) * (w / 2 + 0.05f), yy, z + R(-0.4f, 0.4f) * d), new Vector3(0.1f, 0.28f * bw, R(0.35f, 0.85f) * d));   // flank towers only
                }
                if (rnd.NextDouble() < 0.5) Add(bands[(col + 1) % 3], new Vector3(x + R(-0.45f, 0.45f) * w, yb + h - h * 0.2f, z + d / 2 + 0.06f), new Vector3(0.35f * bw, h * 0.35f, 0.1f));
                if (rnd.NextDouble() < 0.6) Add(bands[(col + 1) % 3], new Vector3(x, yb + h + 0.6f, z), new Vector3(w * 0.9f, 0.35f, d * 0.9f));   // roof crown
            }
            Combined("Skyline_Towers", dark, M("K_Dark"));
            Combined("Skyline_BandsCyan", bands[0], CityLight("Cyan", new Color(0.35f, 2.2f, 2.4f))); Combined("Skyline_BandsMagenta", bands[1], CityLight("Magenta", new Color(2.3f, 0.4f, 1.9f))); Combined("Skyline_BandsAmber", bands[2], CityLight("Amber", new Color(2.5f, 1.3f, 0.4f)));
            // far Monad spire with halo rings
            var sp = Box("MonadSpire", new Vector3(0f, 40f, -250f), new Vector3(9f, 240f, 9f), M("K_Dark"));
            Box("MonadSpireCore", new Vector3(0f, 60f, -245f), new Vector3(2.2f, 260f, 2.2f), M("Beam_Spire"));
            foreach (var (y, s) in new[] { (110f, 34f), (140f, 24f), (165f, 16f) }) Box("SpireRing", new Vector3(0, y, -250f), new Vector3(s, 0.6f, s), M("G_Violet"));
            // gradient sky backdrop (dark violet with a warm horizon) + moon
            var sky = GameObject.CreatePrimitive(PrimitiveType.Quad); sky.name = "SkyBackdrop"; Object.DestroyImmediate(sky.GetComponent<Collider>());
            sky.transform.SetParent(root); sky.transform.position = new Vector3(0, 170f, -385f); sky.transform.rotation = Quaternion.Euler(0, 180f, 0); sky.transform.localScale = new Vector3(1500f, 300f, 1f);
            var skr = sky.GetComponent<MeshRenderer>(); skr.sharedMaterial = SkyGradient(); NoShadow(skr);
            foreach (float sx in new[] { -1f, 1f })
            {
                var sd = GameObject.CreatePrimitive(PrimitiveType.Quad); sd.name = "SkyBackdropSide"; Object.DestroyImmediate(sd.GetComponent<Collider>());
                sd.transform.SetParent(root); sd.transform.position = new Vector3(sx * 385f, 170f, -150f); sd.transform.rotation = Quaternion.Euler(0, sx * 90f, 0); sd.transform.localScale = new Vector3(900f, 300f, 1f);
                var sr = sd.GetComponent<MeshRenderer>(); sr.sharedMaterial = SkyGradient(); NoShadow(sr);
            }
            var moon = GameObject.CreatePrimitive(PrimitiveType.Quad); moon.name = "Moon"; Object.DestroyImmediate(moon.GetComponent<Collider>());
            moon.transform.SetParent(root); moon.transform.position = new Vector3(-95f, 105f, -380f); moon.transform.rotation = Quaternion.Euler(0, 180f, 0); moon.transform.localScale = Vector3.one * 55f;
            var mm = new Material(Shader.Find("NeonProtocol/SkyBackdrop")) { name = "M_Moon" };
            string mp = $"{MatDir}/M_Moon.mat"; AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mm, mp);
            mm.SetColor("_Color", new Color(0.85f, 0.55f, 1.3f)); mm.SetFloat("_Round", 1f);
            var mr = moon.GetComponent<MeshRenderer>(); mr.sharedMaterial = mm; NoShadow(mr);
        }

        static Material CityLight(string n, Color c)
        {
            string path = $"{MatDir}/M_CityLight_{n}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); EditorUtility.SetDirty(m); return m;
        }

        static void Combined(string name, List<CombineInstance> parts, Material m)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; mesh.CombineMeshes(parts.ToArray(), true, true);
            string path = $"Assets/NeonProtocol/Art/Models/VaultKit/{name}.asset";
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(root);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; NoShadow(r);
        }

        static GameObject Put(string piece, Vector3 pos, float yaw, Vector3? scale = null)
        {
            var src = pieces[piece];
            var go = new GameObject(piece, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0) * src.transform.localRotation);
            go.transform.localScale = Vector3.Scale(src.transform.localScale, scale ?? Vector3.one);
            go.GetComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials.Select(m => M(m.name) ?? m).ToArray();
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        static GameObject Box(string name, Vector3 c, Vector3 s, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root); go.transform.position = c; go.transform.localScale = s;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; NoShadow(r);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        static void Pt(string name, Vector3 p, Color c, float i, float range)
        {
            var go = new GameObject(name); go.transform.SetParent(root); go.transform.position = p;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = i; l.range = range; l.shadows = LightShadows.None;
        }

        static void NoShadow(Renderer r) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }

        static void Dust(Vector3 c, Vector3 size)
        {
            var go = new GameObject("ArrivalDust"); go.transform.SetParent(root); go.transform.position = c;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.startLifetime = 10f; main.startSpeed = 0.15f; main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.maxParticles = 220; main.simulationSpace = ParticleSystemSimulationSpace.World; main.prewarm = true;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.7f, 0.45f, 0.8f), new Color(0.4f, 0.95f, 1f, 0.8f));
            var em = ps.emission; em.rateOverTime = 22f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = size;
            var vel = ps.velocityOverLifetime; vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f); vel.y = new ParticleSystem.MinMaxCurve(0.03f, 0.2f); vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = M("FX_Dust"); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
