using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Builds THE VAULT exhibition room far from the heist (x = -600): bright, calm, unarmed.
    // Hall 22 x 24 m: centrepiece plinth (slot 0) facing the entrance, three frames per side wall (slots 1..6),
    // collectors' wing behind a sliding door at the far end (opens only for a verified NEON MARK owner). Re-runnable.
    public static class VaultRoomBuilder
    {
        static readonly Vector3 O = new Vector3(-600f, 0f, 0f);
        static Transform root;
        static Material M(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);

        static GameObject Box(string n, Vector3 c, Vector3 size, Material m, bool collide = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(root, false);
            g.transform.localPosition = c; g.transform.localScale = size;
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            if (!collide) Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }
        static TextMeshPro Text(string n, Vector3 p, Quaternion r, string t, float size, Color c)
        {
            var g = new GameObject(n); g.transform.SetParent(root, false); g.transform.localPosition = p; g.transform.localRotation = r;
            var tmp = g.AddComponent<TextMeshPro>(); tmp.text = t; tmp.fontSize = size; tmp.alignment = TextAlignmentOptions.Center; tmp.color = c;
            tmp.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/NeonProtocol/Art/Fonts/Orbitron-900 SDF.asset");
            tmp.rectTransform.sizeDelta = new Vector2(6f, 1.2f);
            return tmp;
        }
        static Light Lamp(string n, Vector3 p, Color c, float i, float range)
        {
            var g = new GameObject(n); g.transform.SetParent(root, false); g.transform.localPosition = p;
            var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = i; l.range = range; l.shadows = LightShadows.None; return l;
        }

        [MenuItem("Neon Protocol/Build The Vault Room")]
        public static string Build()
        {
            var old = GameObject.Find("TheVault"); if (old) Object.DestroyImmediate(old);
            root = new GameObject("TheVault").transform; root.position = O;
            var wall = M("Assets/NeonProtocol/Art/Materials/M_Obsidian.mat");        // warm off-white (R1)
            var floor = M("Assets/NeonProtocol/Art/Materials/M_Floor.mat");
            var deep = M("Assets/NeonProtocol/Art/Materials/Kit/K_Dark.mat");          // deep blue
            var amber = M("Assets/NeonProtocol/Art/Materials/Kit/G_Amber.mat");
            var cyan = M("Assets/NeonProtocol/Art/Materials/Kit/G_Cyan.mat");
            System.IO.Directory.CreateDirectory("Assets/NeonProtocol/Art/Materials/R1");
            var exP = "Assets/NeonProtocol/Art/Materials/R1/M_VaultExhibit.mat";
            var exhibit = M(exP); if (!exhibit) { exhibit = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(exhibit, exP); }
            exhibit.SetColor("_BaseColor", new Color(0.08f, 0.09f, 0.16f)); EditorUtility.SetDirty(exhibit);

            // shell: hall z 0..24, wing z 24..32
            Box("Floor", new Vector3(0, -0.25f, 16f), new Vector3(24f, 0.5f, 34f), floor);
            Box("Ceiling", new Vector3(0, 7.25f, 16f), new Vector3(24f, 0.5f, 34f), wall);
            Box("Wall_W", new Vector3(-11.25f, 3.5f, 16f), new Vector3(0.5f, 7f, 34f), wall);
            Box("Wall_E", new Vector3(11.25f, 3.5f, 16f), new Vector3(0.5f, 7f, 34f), wall);
            Box("Wall_S", new Vector3(0, 3.5f, -0.75f), new Vector3(24f, 7f, 0.5f), wall);
            Box("Wall_N", new Vector3(0, 3.5f, 32.75f), new Vector3(24f, 7f, 0.5f), wall);
            Box("Gate_Wall_L", new Vector3(-6.5f, 3.5f, 24f), new Vector3(9f, 7f, 0.6f), wall);
            Box("Gate_Wall_R", new Vector3(6.5f, 3.5f, 24f), new Vector3(9f, 7f, 0.6f), wall);
            Box("Gate_Lintel", new Vector3(0, 5.5f, 24f), new Vector3(4f, 3f, 0.6f), wall);
            Box("Base_W", new Vector3(-10.9f, 0.15f, 12f), new Vector3(0.2f, 0.3f, 24f), deep, false);   // deep-blue skirting = structure, not glow
            Box("Base_E", new Vector3(10.9f, 0.15f, 12f), new Vector3(0.2f, 0.3f, 24f), deep, false);
            var door = Box("Gate_Door", new Vector3(0, 1.8f, 24f), new Vector3(4f, 3.6f, 0.4f), deep);
            Box("Gate_Trim", new Vector3(0, 3.95f, 23.68f), new Vector3(4.2f, 0.08f, 0.05f), amber, false);
            var gateFront = new GameObject("Gate_Front").transform; gateFront.SetParent(root, false); gateFront.localPosition = new Vector3(0, 0, 22.4f);
            var gateLabel = Text("Gate_Label", new Vector3(0, 4.5f, 23.65f), Quaternion.identity, "COLLECTORS WING\nNEON MARK HOLDERS", 3.2f, new Color(1f, 0.82f, 0.5f));

            // centrepiece: plinth + large frame facing the entrance
            Box("Plinth", new Vector3(0, 0.5f, 12f), new Vector3(3.6f, 1f, 1.2f), deep);
            Box("Centre_Back", new Vector3(0, 2.9f, 12.12f), new Vector3(3.6f, 3.6f, 0.15f), deep, false);
            var slots = new Renderer[7]; var labels = new TextMeshPro[7];
            slots[0] = Quad("Exhibit_0", new Vector3(0, 2.9f, 12.03f), Quaternion.identity, 3.2f, exhibit);
            labels[0] = Text("Label_0", new Vector3(0, 0.75f, 11.38f), Quaternion.identity, "YOUR RUN", 2.4f, Color.white);
            Box("Centre_Line", new Vector3(0, 1.02f, 11.38f), new Vector3(3.2f, 0.04f, 0.02f), cyan, false);
            // side frames: west wall faces +x, east wall faces -x
            float[] zs = { 6f, 12f, 18f };
            for (int i = 0; i < 6; i++)
            {
                bool west = i < 3; float z = zs[i % 3]; float x = west ? -10.95f : 10.95f;
                var rot = Quaternion.Euler(0, west ? -90f : 90f, 0);
                Box($"Frame_{i + 1}", new Vector3(west ? -10.97f : 10.97f, 3f, z), new Vector3(0.08f, 2.9f, 2.9f), deep, false);
                slots[i + 1] = Quad($"Exhibit_{i + 1}", new Vector3(x + (west ? 0.03f : -0.03f), 3f, z), rot, 2.5f, exhibit);
                labels[i + 1] = Text($"Label_{i + 1}", new Vector3(x + (west ? 0.05f : -0.05f), 1.25f, z), rot, "EMPTY SLOT", 1.6f, new Color(0.85f, 0.87f, 0.95f));
                Lamp($"Lamp_{i + 1}", new Vector3(west ? -8.6f : 8.6f, 5.6f, z), new Color(1f, 0.93f, 0.84f), 2.2f, 6.5f);
            }
            Lamp("Lamp_Centre", new Vector3(0, 5.8f, 9.5f), new Color(1f, 0.95f, 0.88f), 3.2f, 8f);
            Lamp("Lamp_Hall_S", new Vector3(0, 6f, 3f), new Color(0.85f, 0.9f, 1f), 2f, 12f);
            Lamp("Lamp_Wing", new Vector3(0, 5.5f, 28.5f), new Color(1f, 0.8f, 0.5f), 3f, 9f);
            // the wing: a single lit holders' plinth and wall title
            Box("Wing_Plinth", new Vector3(0, 0.6f, 29.5f), new Vector3(2.4f, 1.2f, 2.4f), deep);
            Box("Wing_Mark", new Vector3(0, 1.9f, 29.5f), new Vector3(1.2f, 1.2f, 1.2f), amber, false).transform.localRotation = Quaternion.Euler(45f, 45f, 0);
            Text("Wing_Title", new Vector3(0, 4.6f, 32.45f), Quaternion.identity, "THANK YOU, COLLECTOR", 3f, new Color(1f, 0.85f, 0.55f));
            Text("Hall_Title", new Vector3(0, 5.6f, -0.45f), Quaternion.Euler(0, 180f, 0), "THE VAULT", 4f, new Color(0.15f, 0.2f, 0.4f));
            var spawn = new GameObject("Spawn").transform; spawn.SetParent(root, false); spawn.localPosition = new Vector3(0, 0.05f, 2f); spawn.localRotation = Quaternion.identity;

            var vr = root.gameObject.AddComponent<VaultRoom>();
            vr.spawn = spawn; vr.slots = slots; vr.slotLabels = labels; vr.gateDoor = door.transform; vr.gateLabel = gateLabel; vr.gateFront = gateFront;
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene); EditorSceneManager.SaveScene(root.gameObject.scene);
            return "vault room built at " + O + " with " + root.childCount + " objects";
        }

        static Renderer Quad(string n, Vector3 p, Quaternion r, float size, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad); g.name = n; g.transform.SetParent(root, false);
            g.transform.localPosition = p; g.transform.localRotation = r; g.transform.localScale = new Vector3(size, size, 1f);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mr;
        }
    }
}
