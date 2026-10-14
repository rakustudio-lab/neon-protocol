using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Rescue 2026-10-11: builds ACT III, the Finality Elevator, just beyond the end of the Finality Run track:
    // a reactor dais (kit "Dais") that rises up a shaft of lock rings, light columns, a crown at the top, lights.
    public static class ElevatorBuilder
    {
        const string Kit = "Assets/NeonProtocol/Art/Models/VaultKit/vault_kit.fbx", MatDir = "Assets/NeonProtocol/Art/Materials/Kit";
        static Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

        [MenuItem("Neon Protocol/Build Finality Elevator")]
        public static string Build()
        {
            var run = Object.FindFirstObjectByType<FinalityRun>(FindObjectsInactive.Include);
            var track = Object.FindFirstObjectByType<FinalityTrack>(FindObjectsInactive.Include);
            var stage = run.stageRoot ? run.stageRoot.transform : run.transform;
            var old = stage.Find("FinalityElevator"); if (old) Object.DestroyImmediate(old.gameObject);
            float L = track.Length;
            Vector3 end = track.World(L - 0.5f, 0f, 0f), prev = track.World(L - 6f, 0f, 0f), dir = (end - prev); dir.y = 0; dir.Normalize();
            Vector3 c = end + dir * 26f;
            var root = new GameObject("FinalityElevator").transform; root.SetParent(stage, true); root.position = c; root.rotation = Quaternion.LookRotation(dir);
            var pieces = AssetDatabase.LoadAssetAtPath<GameObject>(Kit).GetComponentsInChildren<MeshFilter>(true).ToDictionary(t => t.name, t => t.gameObject);
            GameObject Put(string n, Transform parent, Vector3 local, float yaw, Vector3? s = null)
            {
                var src = pieces[n]; var go = new GameObject(n, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent, false);
                go.transform.localPosition = local; go.transform.localRotation = Quaternion.Euler(0, yaw, 0) * src.transform.localRotation;
                go.transform.localScale = Vector3.Scale(src.transform.localScale, s ?? Vector3.one);
                go.GetComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
                go.GetComponent<MeshRenderer>().sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials.Select(m => M(m.name) ?? m).ToArray();
                return go;
            }
            // base: floor seal, then the rising platform (dais) with the stand point on top
            Put("FloorSeal", root, new Vector3(0, -0.6f, 0), 0f, new Vector3(1.1f, 1f, 1.1f));
            var platform = new GameObject("Platform").transform; platform.SetParent(root, false); platform.localPosition = new Vector3(0, -1.3f, 0);
            Put("Dais", platform, Vector3.zero, 0f);
            var stand = new GameObject("StandPoint").transform; stand.SetParent(platform, false); stand.localPosition = new Vector3(0, 1.3f, 0); stand.localRotation = Quaternion.Euler(0, 180f, 0);
            var pl = new GameObject("PlatformLight").AddComponent<Light>(); pl.transform.SetParent(platform, false); pl.transform.localPosition = new Vector3(0, 4.5f, 0);
            pl.type = LightType.Point; pl.color = new Color(0.6f, 0.9f, 1f); pl.intensity = 9f; pl.range = 14f;
            // shaft: rings every 6 m, four light columns, crown on top
            var rings = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < 9; i++)
            {
                var ring = Put(i % 2 == 0 ? "LockRing_B" : "LockRing_A", root, new Vector3(0, 4f + i * 6f, 0), i * 20f, Vector3.one * (i % 2 == 0 ? 1.15f : 1.6f));
                rings.Add(ring.GetComponent<MeshRenderer>());
            }
            for (int k = 0; k < 4; k++)
            {
                float a = (45f + k * 90f) * Mathf.Deg2Rad; var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder); col.name = "LightColumn"; Object.DestroyImmediate(col.GetComponent<Collider>());
                col.transform.SetParent(root, false); col.transform.localPosition = new Vector3(Mathf.Cos(a) * 7.2f, 30f, Mathf.Sin(a) * 7.2f); col.transform.localScale = new Vector3(0.35f, 31f, 0.35f);
                var r = col.GetComponent<MeshRenderer>(); r.sharedMaterial = M("Beam_Shaft"); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Put("Pillar", root, new Vector3(Mathf.Cos(a) * 9f, -1f, Mathf.Sin(a) * 9f), -k * 90f - 45f, new Vector3(1f, 3.8f, 1f));
            }
            Put("Crown_Outer", root, new Vector3(0, 60f, 0), 0f, Vector3.one * 1.2f);
            Put("Crown_Struts", root, new Vector3(0, 57f, 0), 0f, Vector3.one * 1.2f);
            var top = new GameObject("CrownLight").AddComponent<Light>(); top.transform.SetParent(root, false); top.transform.localPosition = new Vector3(0, 58f, 0);
            top.type = LightType.Point; top.color = new Color(1f, 0.55f, 1f); top.intensity = 14f; top.range = 30f;
            var el = root.gameObject.AddComponent<FinalityElevator>(); el.platform = platform; el.standPoint = stand; el.rings = rings.ToArray(); el.height = 54f;
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene); EditorSceneManager.SaveScene(root.gameObject.scene);
            return $"elevator at {c} (track end {end}, L={L:F0})";
        }
    }
}
