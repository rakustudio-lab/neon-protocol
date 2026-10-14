using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonProtocol.EditorTools
{
    // Places the observed-network feed and a BLOCK REACTOR kiosk on the arrival platform (idempotent).
    public static class MonadBuilder
    {
        const string MatDir = "Assets/NeonProtocol/Art/Materials";

        [MenuItem("Neon Protocol/Monad: Block Reactor (arrival)")]
        public static string Build()
        {
            var sys = GameObject.Find("_Systems");
            if (!sys.GetComponent<MonadFeed>()) sys.AddComponent<MonadFeed>();
            if (GameObject.Find("BlockReactor_Arrival")) return "exists";

            var metal = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Metal.mat");
            var obsidian = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Obsidian.mat");
            var glow = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Glow.mat");
            var tileM = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_NodeLamp.mat") ?? glow;
            var title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/NeonProtocol/Art/Fonts/Orbitron-900 SDF.asset");
            var mono = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/NeonProtocol/Art/Fonts/JetBrainsMono-500 SDF.asset");

            var root = new GameObject("BlockReactor_Arrival").transform;
            root.SetParent(GameObject.Find("Architecture").transform);
            root.position = new Vector3(-5.2f, 2f, -27.4f);
            Vector3 toSpawn = new Vector3(0f, 0f, -31f) - root.position; toSpawn.y = 0;
            root.rotation = Quaternion.LookRotation(-toSpawn.normalized);   // screen (local -Z face) looks at the spawn

            NeonBlock B(string n, Vector3 lp, Vector3 size, Material m, bool collider = true)
            {
                var go = new GameObject(n); go.transform.SetParent(root, false); go.transform.localPosition = lp;
                go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>().sharedMaterial = m;
                var b = go.AddComponent<NeonBlock>(); b.size = size; b.chamfer = 0.03f; b.paintable = false; b.addCollider = collider;
                NeonBlockTools.Rebuild(b);
                if (!collider) { var c = go.GetComponent<Collider>(); if (c) Object.DestroyImmediate(c); go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off; }
                return b;
            }
            B("Plinth", new Vector3(0, 0.45f, 0.1f), new Vector3(1.4f, 0.9f, 0.7f), obsidian);
            B("Mast", new Vector3(0, 1.3f, 0.15f), new Vector3(0.25f, 1.2f, 0.25f), metal);
            B("Screen_Back", new Vector3(0, 2.75f, 0.12f), new Vector3(3.2f, 1.9f, 0.18f), obsidian);
            var frame = B("Screen_Frame", new Vector3(0, 2.75f, 0.02f), new Vector3(3.3f, 2.0f, 0.04f), glow, false);
            B("Screen_Glass", new Vector3(0, 2.75f, -0.005f), new Vector3(3.1f, 1.8f, 0.02f), obsidian, false);

            var reactor = root.gameObject.AddComponent<BlockReactor>();
            reactor.frame = frame.GetComponent<Renderer>();
            TextMeshPro T(string n, TMP_FontAsset font, Vector3 lp, Vector2 size, float fs, TextAlignmentOptions al, Color col)
            {
                var go = new GameObject(n); go.transform.SetParent(root, false);
                go.transform.localPosition = lp; go.transform.localRotation = Quaternion.identity;   // TMP reads from -Z (the spawn side)
                var t = go.AddComponent<TextMeshPro>(); t.font = font; t.fontSize = fs; t.alignment = al; t.color = col;
                t.rectTransform.sizeDelta = size; t.enableWordWrapping = false; t.richText = true; t.text = n;
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                return t;
            }
            float z = -0.03f;
            reactor.title = T("Title", title, new Vector3(0, 3.48f, z), new Vector2(2.9f, 0.3f), 2.1f, TextAlignmentOptions.Left, new Color(0.85f, 0.8f, 1f));
            reactor.title.text = "MONAD TRANSIT NETWORK";
            reactor.status = T("Status", mono, new Vector3(0, 3.2f, z), new Vector2(2.9f, 0.2f), 1.05f, TextAlignmentOptions.Left, new Color(0.75f, 0.75f, 0.9f));
            reactor.blockNo = T("BlockNo", title, new Vector3(0, 2.82f, z), new Vector2(2.9f, 0.5f), 4.2f, TextAlignmentOptions.Left, Color.white);
            reactor.details = T("Details", mono, new Vector3(0, 2.33f, z), new Vector2(2.9f, 0.5f), 1.05f, TextAlignmentOptions.TopLeft, new Color(0.7f, 0.95f, 0.95f));
            reactor.footer = T("Footer", mono, new Vector3(0, 1.93f, z), new Vector2(2.9f, 0.2f), 0.72f, TextAlignmentOptions.TopLeft, new Color(0.6f, 0.58f, 0.75f));

            // block stream: 18 tiles along the bottom edge
            int n = 18; reactor.tiles = new Renderer[n];
            for (int i = 0; i < n; i++)
                reactor.tiles[i] = B("Tile" + i, new Vector3(-1.45f + i * (2.9f / (n - 1)), 1.86f, -0.02f), new Vector3(0.12f, 0.05f, 0.03f), tileM, false).GetComponent<Renderer>();
            // shift footer above the tiles
            reactor.footer.transform.localPosition = new Vector3(0, 2.03f, z);

            var l = new GameObject("Pulse").AddComponent<Light>(); l.transform.SetParent(root, false); l.transform.localPosition = new Vector3(0, 2.7f, -1.2f);
            l.type = LightType.Point; l.range = 6f; l.intensity = 2f; l.shadows = LightShadows.None; reactor.pulseLight = l;

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene); EditorSceneManager.SaveOpenScenes();
            return "block reactor built at arrival";
        }
    }
}
