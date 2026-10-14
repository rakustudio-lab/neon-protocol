using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Gate 1 route readability: arrival -> hall -> east stair -> catwalk -> Iris relay A.
    // Targeted guidance only (no uniform neon): warm light where the route turns, cool light where it arrives, a tall
    // beacon over the relay that reads from the hall as the distant anchor, sparse low-glow floor chevrons on the path.
    public static class RouteArt
    {
        const string MatDir = "Assets/NeonProtocol/Art/Materials/Kit";
        static Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
        static Transform root;

        [MenuItem("Neon Protocol/Art/Route to Relay A")]
        public static string Build()
        {
            var old = GameObject.Find("ArtPass_RouteA"); if (old) Object.DestroyImmediate(old);
            root = new GameObject("ArtPass_RouteA").transform;
            // lights at the decision points
            Pt("L_Route_StairBase", new Vector3(18.6f, 2.2f, -21.5f), new Color(1f, 0.62f, 0.32f), 7f, 11f);
            Pt("L_Route_ArrivalExit", new Vector3(4.5f, 3.6f, -22.5f), new Color(0.75f, 0.8f, 1f), 8f, 10f);
            Pt("L_Route_HallTurn", new Vector3(9f, 3.2f, -19f), new Color(1f, 0.7f, 0.42f), 4f, 9f);
            Pt("L_Route_StairTop", new Vector3(19.5f, 7f, -10.5f), new Color(0.45f, 0.95f, 1f), 5f, 10f);
            Pt("L_Route_Catwalk", new Vector3(19.0f, 7.2f, -4.5f), new Color(0.7f, 0.95f, 1f), 6f, 9f);
            // soft pools over the main hall floor so it reads from the catwalk and stair (alternating cool / warm-violet)
            var pools = new[] { new Vector3(-10f, 6.5f, -15f), new Vector3(1f, 6.5f, -17f), new Vector3(11f, 6.5f, -13f), new Vector3(-9f, 6.5f, -1f), new Vector3(9f, 6.5f, 1f) };
            for (int i = 0; i < pools.Length; i++) Pt("L_HallPool_" + i, pools[i], i % 2 == 0 ? new Color(0.55f, 0.8f, 1f) : new Color(0.8f, 0.6f, 1f), 12f, 14f);
            // beacon over the relay: a slim vertical light column that reads from the hall floor
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder); beam.name = "RelayA_Beacon"; Object.DestroyImmediate(beam.GetComponent<Collider>());
            beam.transform.SetParent(root); beam.transform.position = new Vector3(21.6f, 14.5f, -2f); beam.transform.localScale = new Vector3(0.5f, 7f, 0.5f);
            var br = beam.GetComponent<MeshRenderer>(); br.sharedMaterial = M("Beam_Shaft"); br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // sparse floor chevrons along the path (every ~3.5 m), low glow
            var pts = new[] { new Vector3(2f, 0.03f, -16f), new Vector3(5.5f, 0.03f, -17f), new Vector3(9f, 0.03f, -18.2f), new Vector3(12.5f, 0.03f, -19.4f), new Vector3(16f, 0.03f, -20.6f) };
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 dir = (i < pts.Length - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]); dir.y = 0; dir.Normalize();
                Chevron(pts[i], dir);
            }
            foreach (var z in new[] { -12.5f, -8.5f, -5f }) Chevron(new Vector3(19.5f, 4.53f, z), Vector3.forward);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene); EditorSceneManager.SaveScene(root.gameObject.scene);
            return "route A lit: " + root.childCount + " objects";
        }

        static void Chevron(Vector3 p, Vector3 dir)
        {
            var c = new GameObject("Chevron").transform; c.SetParent(root); c.position = p; c.rotation = Quaternion.LookRotation(dir);
            foreach (float s in new[] { -1f, 1f })
            {
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube); arm.name = "Arm"; Object.DestroyImmediate(arm.GetComponent<Collider>());
                arm.transform.SetParent(c, false); arm.transform.localPosition = new Vector3(s * 0.22f, 0f, -0.1f); arm.transform.localRotation = Quaternion.Euler(0, -s * 40f, 0); arm.transform.localScale = new Vector3(0.08f, 0.01f, 0.62f);
                var r = arm.GetComponent<MeshRenderer>(); r.sharedMaterial = M("G_Amber"); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static void Pt(string n, Vector3 p, Color c, float i, float range)
        {
            var go = new GameObject(n); go.transform.SetParent(root); go.transform.position = p;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = i; l.range = range; l.shadows = LightShadows.None;
        }
    }
}
