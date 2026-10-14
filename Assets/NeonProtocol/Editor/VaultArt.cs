using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Core Vault art pass: dresses THE FRACTURE's vault with the Blender kit (Tools/blender/vault_kit.py).
    // Non-destructive: everything lives under "ArtPass_Vault" (rebuilt on each run); superseded blockout renderers are
    // only disabled (colliders, routes and gameplay objects stay untouched) and are listed on the root for Revert().
    public static class VaultArt
    {
        const string Kit = "Assets/NeonProtocol/Art/Models/VaultKit/vault_kit.fbx";
        const string MatDir = "Assets/NeonProtocol/Art/Materials/Kit";
        const string TexDir = "Assets/NeonProtocol/Art/Textures";
        public const float FaceYaw = 0f;   // kit pieces face Blender -Y; corrected here if the FBX axis mapping differs
        static readonly Vector3 C = new Vector3(0, 0, 23);   // vault centre (Core pedestal)

        static readonly string[] Superseded = {
            "Pedestal", "Pedestal_Ring", "Vault_FloorRing_N", "Vault_FloorRing_S", "Vault_FloorRing_E", "Vault_FloorRing_W",
            "Balcony_Rail", "Balcony_Rail_1_0", "Balcony_Rail_2_0", "Energy_Link", "CageTop" };

        static Dictionary<string, Material> mats;
        static Dictionary<string, GameObject> pieces;
        static Transform root;

        [MenuItem("Neon Protocol/Art/Build Core Vault Art")]
        public static string Build()
        {
            ConfigureImports();
            mats = EnsureMaterials();
            pieces = new Dictionary<string, GameObject>();
            foreach (var t in AssetDatabase.LoadAssetAtPath<GameObject>(Kit).GetComponentsInChildren<MeshFilter>(true)) pieces[t.name] = t.gameObject;

            var old = GameObject.Find("ArtPass_Vault");
            var prevRec = old ? old.GetComponent<ArtPassRecord>() : null;
            string[] prevHidden = prevRec ? prevRec.hidden : new string[0];
            Mesh prevCore = prevRec ? prevRec.coreMesh : null; Material[] prevCoreMats = prevRec ? prevRec.coreMats : null; Material prevBar = prevRec ? prevRec.cageBarMat : null;
            if (old) Object.DestroyImmediate(old);
            root = new GameObject("ArtPass_Vault").transform;
            var log = new List<string>();

            // ---- centrepiece: dais, cradle, floor seal, crown, ceiling frame
            var dais = Put("Dais", C, 0); dais.AddComponent<MeshCollider>().sharedMesh = dais.GetComponent<MeshFilter>().sharedMesh;
            Put("Cradle", C, 0);
            Put("FloorSeal", C + Vector3.up * 0.004f, 0);
            Put("Crown_Outer", C + Vector3.up * 11f, 0);
            Put("Crown_Struts", C + Vector3.up * 11f, 0);
            var inner = Put("Crown_Inner", C + Vector3.up * 13.2f, 0);
            var sp = inner.AddComponent<Spin>(); sp.degreesPerSecond = -9f; sp.bobHeight = 0.15f;
            var outerSpin = Put("Crown_Inner", C + Vector3.up * 8.9f, 0); outerSpin.name = "Crown_Low"; outerSpin.transform.localScale = new Vector3(0.62f, 1f, 0.62f);
            outerSpin.AddComponent<Spin>().degreesPerSecond = 14f;
            Put("CeilingFrame", C + Vector3.up * 15.98f, 22.5f);

            // ---- energy: core beam + soft halo, four light shafts from the ceiling ring
            Beam("CoreBeam", C + Vector3.up * 5.7f, new Vector3(0.5f, 2.05f, 0.5f), mats["Beam_Core"]);
            Beam("CoreHalo", C + Vector3.up * 5.7f, new Vector3(1.6f, 2.05f, 1.6f), mats["Beam_Halo"]);
            Beam("SpindleBeam", C + Vector3.up * 12f, new Vector3(0.35f, 4f, 0.35f), mats["Beam_Core"]);
            var cone = ShaftCone();
            for (int k = 0; k < 4; k++)
            {
                float a = (45 + 90 * k) * Mathf.Deg2Rad;
                var go = new GameObject("LightShaft_" + k, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root); go.transform.position = C + new Vector3(Mathf.Cos(a) * 7.2f, 15.4f, Mathf.Sin(a) * 7.2f);
                go.GetComponent<MeshFilter>().sharedMesh = cone; go.GetComponent<MeshRenderer>().sharedMaterial = mats["Beam_Shaft"];
                Shadowless(go);
            }

            // ---- pillars (pivot at centre; half depth 0.79 from the wall face)
            Put("Pillar", new Vector3(-14.21f, 0, 12.8f), -90); Put("Pillar", new Vector3(14.21f, 0, 12.6f), 90);
            Put("Pillar", new Vector3(-14.21f, 0, 31.5f), -90); Put("Pillar", new Vector3(14.21f, 0, 31.5f), 90);
            Put("Pillar", new Vector3(-7f, 0, 33.21f), 0); Put("Pillar", new Vector3(7f, 0, 33.21f), 0);
            // ribs flanking the vault door
            Put("WallRib", new Vector3(-6.2f, 0, 12.01f), 180); Put("WallRib", new Vector3(6.2f, 0, 12.01f), 180);

            // ---- wall panels (pivot at base centre on the wall plane)
            foreach (float x in new[] { -11f, -3f, 3f, 11f }) Put("WallPanel", new Vector3(x, 0, 33.99f), 0, new Vector3(1, 0.72f, 1));
            foreach (float x in new[] { -11f, 11f }) Put("WallPanel", new Vector3(x, 6f, 33.99f), 0);
            Put("WallPanel", new Vector3(-14.99f, 0, 16.6f), -90);
            foreach (float z in new[] { 16.4f, 20.4f, 24.4f, 28.4f }) { Put("WallPanel", new Vector3(-14.99f, 6.5f, z), -90); Put("WallPanel", new Vector3(14.99f, 6.5f, z), 90); }
            foreach (float x in new[] { -9.5f, 9.5f }) { Put("WallPanel", new Vector3(x, 0, 12.01f), 180); Put("WallPanel", new Vector3(x, 6.5f, 12.01f), 180); }
            foreach (float x in new[] { -2f, 2f }) Put("WallPanel", new Vector3(x, 9.4f, 12.01f), 180);

            // ---- pipes, rails, machinery, cables
            foreach (float z in new[] { 19.5f, 27.5f }) { Put("PipeRun", new Vector3(-14.99f, 14.2f, z), -90); Put("PipeRun", new Vector3(14.99f, 14.2f, z), 90); }
            foreach (float x in new[] { -4f, 4f }) Put("PipeRun", new Vector3(x, 14.6f, 33.99f), 0);
            foreach (float x in new[] { -10f, -6f, -2f, 2f, 6f, 10f }) Put("Rail", new Vector3(x, 5f, 29.15f), 0);
            foreach (float x in new[] { -12.6f, 12.6f }) Put("Capacitor", new Vector3(x, 0, 32.4f), 0, Vector3.one * 0.72f);
            foreach (float deg in new[] { 30f, 150f, 210f, 330f })
            {
                float a = deg * Mathf.Deg2Rad;
                Put("CableDrop", C + new Vector3(Mathf.Cos(a) * 9f, 16f, Mathf.Sin(a) * 9f), deg);
            }

            // ---- lights: core (magenta), crown (cyan), balcony (amber)
            Light("L_CoreBeam", C + Vector3.up * 4f, new Color(1f, 0.3f, 0.85f), 7f, 14f);
            Light("L_Crown_W", C + new Vector3(-6.5f, 10f, 0), new Color(0.25f, 0.9f, 1f), 4f, 13f);
            Light("L_Crown_E", C + new Vector3(6.5f, 10f, 0), new Color(0.25f, 0.9f, 1f), 4f, 13f);
            Light("L_Balcony", new Vector3(0, 6.6f, 28.2f), new Color(1f, 0.68f, 0.3f), 3f, 11f);
            Light("L_CoreWarm", C + new Vector3(0, 1.6f, -2.5f), new Color(1f, 0.55f, 0.22f), 4f, 9f);   // warm/cool hierarchy (concept: amber base, violet beam)
            Dust();

            // ---- vault lock re-skin: smooth segmented rings + orb nodes in housings (gameplay objects untouched)
            ReskinLock();

            // ---- floor: dark wet reflective variant without the glowing grid seams
            int floors = 0;
            foreach (var r in GameObject.Find("04_Vault").GetComponentsInChildren<MeshRenderer>())
                if (r.sharedMaterial && r.sharedMaterial.name == "M_Floor") { r.sharedMaterial = mats["M_Floor_Vault"]; floors++; }

            // ---- supersede blockout visuals (renderers off, colliders kept)
            var hidden = new List<string>();
            foreach (var n in Superseded)
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t.name == n))
                {
                    if (t.TryGetComponent<MeshRenderer>(out var mr) && mr.enabled) { mr.enabled = false; hidden.Add(n); }
                    // an invisible plate must not stay a solid wall (it blocked shots at the lowest lock ring)
                    if (n == "CageTop" && t.TryGetComponent<Collider>(out var col)) col.enabled = false;
                }
            var rec = root.gameObject.AddComponent<ArtPassRecord>(); rec.hidden = hidden.Concat(prevHidden).Distinct().ToArray();
            ReskinCore(rec);
            if (prevCore) { rec.coreMesh = prevCore; rec.coreMats = prevCoreMats; }
            if (prevBar) rec.cageBarMat = prevBar;

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            EditorSceneManager.SaveScene(root.gameObject.scene);
            int tris = root.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh ? f.sharedMesh.triangles.Length / 3 : 0);
            return $"vault art: {root.childCount} objects, {tris} tris, floors {floors}, hidden {hidden.Count}";
        }

        [MenuItem("Neon Protocol/Art/Revert Core Vault Art")]
        public static string Revert()
        {
            var r = GameObject.Find("ArtPass_Vault"); if (!r) return "nothing to revert";
            var rec = r.GetComponent<ArtPassRecord>();
            foreach (var n in rec ? rec.hidden : new string[0])
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t.name == n))
                {
                    if (t.TryGetComponent<MeshRenderer>(out var mr)) mr.enabled = true;
                    if (t.TryGetComponent<Collider>(out var col)) col.enabled = true;
                }
            var floor = AssetDatabase.LoadAssetAtPath<Material>("Assets/NeonProtocol/Art/Materials/M_Floor.mat");
            foreach (var mr in GameObject.Find("04_Vault").GetComponentsInChildren<MeshRenderer>())
                if (mr.sharedMaterial && mr.sharedMaterial.name == "M_Floor_Vault") mr.sharedMaterial = floor;
            var cv = GameObject.Find("CoreVisual");
            if (cv && rec && rec.coreMesh) { cv.GetComponent<MeshFilter>().sharedMesh = rec.coreMesh; cv.GetComponent<MeshRenderer>().sharedMaterials = rec.coreMats; cv.transform.localScale = Vector3.one; }
            var cage = GameObject.Find("Cage");
            if (cage && rec && rec.cageBarMat) foreach (var bar in cage.GetComponentsInChildren<MeshRenderer>(true)) if (bar.name.StartsWith("Bar")) { bar.sharedMaterial = rec.cageBarMat; bar.transform.localScale = Vector3.one; }
            Object.DestroyImmediate(r);
            UnskinLock();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return "reverted";
        }

        static void ReskinLock()
        {
            var lockGo = GameObject.Find("VaultLock"); if (!lockGo) return;
            var sphere = PrimitiveMesh(PrimitiveType.Sphere);   // runtime built-in mesh (NOT the editor-only "New-Sphere.fbx")
            string[] skins = { "LockRing_A", "LockRing_B", "LockRing_C" };
            for (int k = 0; k < 3; k++)
            {
                var ring = lockGo.transform.Find("Ring" + (k + 1)); if (!ring) continue;
                foreach (Transform t in ring.Cast<Transform>().ToArray())
                {
                    if (t.name == "ArtSkin") { Object.DestroyImmediate(t.gameObject); continue; }
                    if ((t.name.StartsWith("Seg") || t.name.StartsWith("Strip")) && t.TryGetComponent<MeshRenderer>(out var mr)) mr.enabled = false;
                }
                var skin = Put(skins[k], ring.position, 0); skin.name = "ArtSkin"; skin.transform.SetParent(ring, true);
                GameObjectUtility.SetStaticEditorFlags(skin, 0);
                var node = ring.Find("Node" + (k + 1)); if (!node) continue;
                var old = node.Find("ArtSkin"); if (old) Object.DestroyImmediate(old.gameObject);
                var lamp = node.Find("Lamp");
                if (lamp) { lamp.GetComponent<MeshFilter>().sharedMesh = sphere; lamp.localRotation = Quaternion.identity; lamp.localScale = Vector3.one * 0.72f; }
                var h = Put("NodeHousing", node.position, 0); h.name = "ArtSkin"; h.transform.SetParent(node, true);
                GameObjectUtility.SetStaticEditorFlags(h, 0);
            }
        }

        // The Core: faceted crystal + gyroscope (CoreObjective keeps driving the same transform); cage bars become containment rods.
        static void ReskinCore(ArtPassRecord rec)
        {
            var cv = GameObject.Find("CoreVisual"); if (!cv) return;
            var mf = cv.GetComponent<MeshFilter>(); var mr = cv.GetComponent<MeshRenderer>();
            var prev = GameObject.Find("ArtPass_Vault_CoreBackup");
            rec.coreMesh = mf.sharedMesh.name.StartsWith("CoreGem") ? AssetDatabase.LoadAssetAtPath<Mesh>("Assets/NeonProtocol/Generated/TheFracture/CoreVisual_689519048.asset") ?? mf.sharedMesh : mf.sharedMesh;
            rec.coreMats = mr.sharedMaterials.Any(m => m && m.name.StartsWith("K_")) ? new[] { AssetDatabase.LoadAssetAtPath<Material>("Assets/NeonProtocol/Art/Materials/M_CoreGlow.mat") } : mr.sharedMaterials;
            var src = pieces["CoreGem"];
            mf.sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            mr.sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials.Select(m => mats.TryGetValue(m.name, out var k) ? k : m).ToArray();
            cv.transform.localScale = Vector3.one * 0.8f;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var cage = GameObject.Find("Cage");
            if (cage) foreach (var bar in cage.GetComponentsInChildren<MeshRenderer>(true))
                if (bar.name.StartsWith("Bar")) { if (!rec.cageBarMat && !bar.sharedMaterial.name.StartsWith("G_")) rec.cageBarMat = bar.sharedMaterial; bar.sharedMaterial = mats["G_Magenta"]; bar.transform.localScale = new Vector3(0.5f, 1f, 0.5f); }
        }

        static Mesh PrimitiveMesh(PrimitiveType t)
        {
            var g = GameObject.CreatePrimitive(t); var m = g.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(g); return m;
        }

        static void UnskinLock()
        {
            var lockGo = GameObject.Find("VaultLock"); if (!lockGo) return;
            foreach (var t in lockGo.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (!t) continue;
                if (t.name == "ArtSkin") { Object.DestroyImmediate(t.gameObject); continue; }
                if ((t.name.StartsWith("Seg") || t.name.StartsWith("Strip")) && t.TryGetComponent<MeshRenderer>(out var mr)) mr.enabled = true;
                if (t.name == "Lamp") { t.GetComponent<MeshFilter>().sharedMesh = PrimitiveMesh(PrimitiveType.Cube); t.localRotation = Quaternion.Euler(45, 0, 45); t.localScale = Vector3.one * 0.75f; }
            }
        }

        // ------------------------------------------------------------------ helpers
        static GameObject Put(string piece, Vector3 pos, float yaw, Vector3? scale = null)
        {
            var src = pieces[piece];
            var go = new GameObject(piece, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw + FaceYaw, 0) * src.transform.localRotation);
            go.transform.localScale = Vector3.Scale(src.transform.localScale, scale ?? Vector3.one);
            go.GetComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials.Select(m => mats.TryGetValue(m.name, out var k) ? k : m).ToArray();
            GameObjectUtility.SetStaticEditorFlags(go, piece.StartsWith("Crown_Inner") ? 0 : StaticEditorFlags.BatchingStatic);
            return go;
        }

        static void Beam(string name, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root); go.transform.position = pos; go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m; Shadowless(go);
        }

        static void Shadowless(GameObject go)
        {
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        static void Light(string name, Vector3 pos, Color c, float intensity, float range)
        {
            var go = new GameObject(name); go.transform.SetParent(root); go.transform.position = pos;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        }

        // Slow rising energy dust in the reactor volume (cheap: one particle system, additive, no lighting).
        static void Dust()
        {
            var go = new GameObject("EnergyDust"); go.transform.SetParent(root); go.transform.position = C + Vector3.up * 6f;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.startLifetime = 9f; main.startSpeed = 0.25f; main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.maxParticles = 350; main.simulationSpace = ParticleSystemSimulationSpace.World; main.prewarm = true;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 1f, 0.9f), new Color(0.3f, 0.95f, 1f, 0.9f));
            var em = ps.emission; em.rateOverTime = 38f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(24f, 11f, 18f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f); vel.y = new ParticleSystem.MinMaxCurve(0.08f, 0.35f); vel.z = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mats["FX_Dust"]; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Mesh ShaftCone()
        {
            string path = "Assets/NeonProtocol/Art/Models/VaultKit/ShaftCone.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m) return m;
            m = new Mesh { name = "ShaftCone" };
            const int n = 20; float h = 15.2f, r0 = 0.55f, r1 = 2.4f;
            var v = new List<Vector3>(); var nr = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float a = 2 * Mathf.PI * i / n; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * r0); v.Add(d * r1 + Vector3.down * h); nr.Add(d); nr.Add(d);
                if (i < n) { int b = i * 2; t.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 }); }
            }
            m.SetVertices(v); m.SetNormals(nr); m.SetTriangles(t, 0); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void ConfigureImports()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(Kit);
            if (mi && (mi.addCollider || mi.importAnimation || mi.importCameras))
            { mi.addCollider = false; mi.importAnimation = false; mi.importCameras = false; mi.importLights = false; mi.SaveAndReimport(); }
            foreach (var (suffix, normal, srgb) in new[] { ("_A", false, true), ("_N", true, false), ("_MS", false, false) })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath($"{TexDir}/T_Panel{suffix}.png");
                if (!ti) continue;
                var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != want || ti.sRGBTexture != srgb || ti.maxTextureSize != 1024)
                { ti.textureType = want; ti.sRGBTexture = srgb; ti.maxTextureSize = 1024; ti.mipmapEnabled = true; ti.anisoLevel = 4; ti.SaveAndReimport(); }
            }
        }

        static Dictionary<string, Material> EnsureMaterials()
        {
            System.IO.Directory.CreateDirectory(MatDir);
            var d = new Dictionary<string, Material>();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var beam = Shader.Find("NeonProtocol/Beam");
            Material Get(string n, Shader s)
            {
                string p = $"{MatDir}/{n}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (!m) { m = new Material(s) { name = n }; AssetDatabase.CreateAsset(m, p); }
                m.shader = s; d[n] = m; return m;
            }
            var plate = Get("K_Plate", lit);
            plate.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_Panel_A.png"));
            plate.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_Panel_N.png"));
            plate.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_Panel_MS.png"));
            plate.EnableKeyword("_NORMALMAP"); plate.EnableKeyword("_METALLICSPECGLOSSMAP");
            plate.SetColor("_BaseColor", new Color(1.05f, 1.0f, 1.15f)); plate.SetFloat("_Smoothness", 1f); plate.SetFloat("_BumpScale", 1f);
            var dark = Get("K_Dark", lit); dark.SetColor("_BaseColor", new Color(0.018f, 0.017f, 0.024f)); dark.SetFloat("_Metallic", 0.3f); dark.SetFloat("_Smoothness", 0.55f);
            var trim = Get("K_Trim", lit); trim.SetColor("_BaseColor", new Color(0.32f, 0.31f, 0.36f)); trim.SetFloat("_Metallic", 0.95f); trim.SetFloat("_Smoothness", 0.72f);
            foreach (var (n, c) in new[] { ("G_Cyan", new Color(0.2f, 2.6f, 3.0f)), ("G_Magenta", new Color(3.4f, 0.5f, 2.6f)), ("G_Amber", new Color(5.5f, 2.6f, 0.6f)), ("G_Violet", new Color(1.5f, 0.75f, 3.6f)) })
            { var m = Get(n, unlit); m.SetColor("_BaseColor", c); }
            void B(string n, Color c, float core, float bottom, float top, float soft, float band) {
                var m = Get(n, beam); m.SetColor("_Color", c); m.SetFloat("_Core", core); m.SetFloat("_FadeBottom", bottom); m.SetFloat("_FadeTop", top);
                m.SetFloat("_FadeSoft", soft); m.SetFloat("_BandAmount", band); }
            B("K_Glass", new Color(0.45f, 0.1f, 1.3f), 1.6f, -10f, 10f, 0.01f, 0.3f);
            B("Beam_Core", new Color(3.0f, 0.9f, 2.6f), 2.2f, -1f, 1f, 0.35f, 0.35f);
            B("Beam_Halo", new Color(0.5f, 0.12f, 0.55f), 3.5f, -1f, 1f, 0.6f, 0.2f);
            B("Beam_Shaft", new Color(0.035f, 0.025f, 0.085f), 3.0f, -15.2f, 0f, 6f, 0.15f);
            var dust = Get("FX_Dust", Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            dust.SetFloat("_Surface", 1f); dust.SetFloat("_Blend", 2f); dust.SetColor("_BaseColor", new Color(2.2f, 2.2f, 2.2f, 1f));
            dust.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            dust.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); dust.EnableKeyword("_BLENDMODE_ADD"); dust.renderQueue = 3000;
            dust.SetOverrideTag("RenderType", "Transparent"); dust.SetFloat("_ZWrite", 0f); dust.SetFloat("_SrcBlend", 1f); dust.SetFloat("_DstBlend", 1f);
            var floorSrc = AssetDatabase.LoadAssetAtPath<Material>("Assets/NeonProtocol/Art/Materials/M_Floor.mat");
            var fv = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Floor_Vault.mat");
            if (!fv) { fv = new Material(floorSrc) { name = "M_Floor_Vault" }; AssetDatabase.CreateAsset(fv, $"{MatDir}/M_Floor_Vault.mat"); }
            fv.CopyPropertiesFromMaterial(floorSrc); fv.shaderKeywords = floorSrc.shaderKeywords;
            fv.SetFloat("_SeamGlow", 0.08f); fv.SetFloat("_Smoothness", 0.78f); fv.SetColor("_BaseColor", new Color(0.03f, 0.028f, 0.04f));
            fv.SetVector("_PanelFloor", new Vector4(1.5f, 3f, 0, 0)); fv.SetFloat("_Grime", 0.5f);
            d["M_Floor_Vault"] = fv;
            foreach (var m in d.Values) EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return d;
        }
    }
}
