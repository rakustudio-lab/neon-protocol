using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // R2 map sealing with real geometry (bounds probe findings). Re-runnable.
    //  * Finality Gate "beyond" alcove had floor + back wall but no side walls: a diagonal sprint past the threshold
    //    dropped the player off the map (x > 31 / x < 23). Visible side walls with collision close it.
    public static class MapSeal
    {
        [MenuItem("Neon Protocol/R2/Seal Map Gaps")]
        public static string Seal()
        {
            var gate = GameObject.Find("Architecture/05_Gate"); if (!gate) return "gate group missing";
            var old = gate.transform.Find("MapSeal_R2"); if (old) Object.DestroyImmediate(old.gameObject);
            var g = new GameObject("MapSeal_R2").transform; g.SetParent(gate.transform, true);
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/NeonProtocol/Art/Materials/M_Obsidian.mat");
            int n = 0;
            foreach (var (name, c, s) in new[] { ("Beyond_Side_L", new Vector3(22.5f, 3.25f, -14.1f), new Vector3(1.0f, 6.5f, 6.4f)), ("Beyond_Side_R", new Vector3(31.5f, 3.25f, -14.1f), new Vector3(1.0f, 6.5f, 6.4f)) })
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cube); w.name = name; w.transform.SetParent(g, false);
                w.transform.position = c; w.transform.localScale = s; w.GetComponent<MeshRenderer>().sharedMaterial = mat;
                GameObjectUtility.SetStaticEditorFlags(w, StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic); n++;
            }
            EditorSceneManager.MarkSceneDirty(gate.scene); EditorSceneManager.SaveScene(gate.scene);
            return "sealed: " + n + " walls";
        }
    }
}
