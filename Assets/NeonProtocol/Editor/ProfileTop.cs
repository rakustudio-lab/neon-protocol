using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace NeonProtocol.EditorTools
{
    // Perf diagnosis: record the Unity profiler while a QA routine runs, then rank main-thread markers by SELF time.
    // Editor numbers are not browser numbers (faster CPU, separate render thread), but they show WHICH scripts/systems
    // dominate the gameplay frame, which the browser toggles could not isolate.
    public static class ProfileTop
    {
        public static string Start() { ProfilerDriver.ClearAllFrames(); ProfilerDriver.enabled = true; Profiler.enabled = true; return "recording"; }

        public static string Stop(int top = 30)
        {
            ProfilerDriver.enabled = false;
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            var self = new Dictionary<string, double>(); var calls = new Dictionary<string, int>(); int frames = 0; double total = 0;
            for (int f = first; f <= last; f++)
            {
                using (var v = ProfilerDriver.GetRawFrameDataView(f, 0))
                {
                    if (v == null || !v.valid) continue;
                    frames++; total += v.frameTimeMs;
                    int n = v.sampleCount;
                    var childSum = new double[n]; var parent = new int[n]; var stack = new Stack<(int idx, int remaining)>();
                    for (int i = 0; i < n; i++)
                    {
                        parent[i] = -1;
                        while (stack.Count > 0 && stack.Peek().remaining == 0) stack.Pop();
                        if (stack.Count > 0) { var top2 = stack.Pop(); parent[i] = top2.idx; stack.Push((top2.idx, top2.remaining - 1)); }
                        int kids = v.GetSampleChildrenCount(i);
                        if (kids > 0) stack.Push((i, kids));
                    }
                    for (int i = 0; i < n; i++) if (parent[i] >= 0) childSum[parent[i]] += v.GetSampleTimeMs(i);
                    for (int i = 0; i < n; i++)
                    {
                        string name = v.GetSampleName(i);
                        double s = v.GetSampleTimeMs(i) - childSum[i];
                        self[name] = self.TryGetValue(name, out var x) ? x + s : s;
                        calls[name] = calls.TryGetValue(name, out var c) ? c + 1 : 1;
                    }
                }
            }
            var sb = new StringBuilder($"frames {frames}, avg frame {total / Mathf.Max(1, frames):F2} ms (editor). Top main-thread SELF time per frame:\n");
            foreach (var kv in self.OrderByDescending(k => k.Value).Take(top))
                sb.AppendLine($"{kv.Value / Mathf.Max(1, frames),7:F3} ms  x{calls[kv.Key] / (float)Mathf.Max(1, frames),6:F1}/frame  {kv.Key}");
            System.IO.File.WriteAllText("Builds/profile_top.txt", sb.ToString());
            return sb.ToString();
        }
    }
}
