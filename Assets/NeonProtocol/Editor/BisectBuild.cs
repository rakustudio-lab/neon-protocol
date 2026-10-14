using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonProtocol.EditorTools
{
    // Diagnosis helper: builds a Windows dev player from a TEMP COPY of THE FRACTURE with the named objects (or
    // components, "Type@Object") removed, so a corrupt-serialisation culprit can be bisected. Original scene untouched.
    public static class BisectBuild
    {
        const string Src = "Assets/NeonProtocol/Scenes/TheFracture.unity", Tmp = "Assets/_BisectTemp.unity";

        public static string Run(string remove)
        {
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.CopyAsset(Src, Tmp);
            var scene = EditorSceneManager.OpenScene(Tmp);
            int n = 0;
            foreach (var item in remove.Split(','))
            {
                if (item.StartsWith("all:"))
                {
                    string nm = item.Substring(4).Trim();
                    foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        if (t && (t.name == nm || (nm.EndsWith("*") && t.name.StartsWith(nm.TrimEnd((char)42))))) { Object.DestroyImmediate(t.gameObject); n++; }
                    continue;
                }
                var parts = item.Split('@');
                var go = GameObject.Find(parts[parts.Length - 1].Trim()); if (!go) continue;
                if (parts.Length == 2)
                {
                    foreach (var c in go.GetComponentsInChildren<Component>(true))
                        if (c && c.GetType().Name == parts[0].Trim()) { Object.DestroyImmediate(c); n++; }
                }
                else { Object.DestroyImmediate(go); n++; }
            }
            EditorSceneManager.SaveScene(scene);
            var o = new BuildPlayerOptions { scenes = new[] { Tmp }, locationPathName = "Builds/WinTest/NP.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development };
            var r = BuildPipeline.BuildPlayer(o);
            EditorSceneManager.OpenScene(Src);
            AssetDatabase.DeleteAsset(Tmp);
            return $"removed {n}; build {r.summary.result} {r.summary.totalTime.TotalSeconds:F0}s";
        }
    }
}
