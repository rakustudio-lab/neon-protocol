using System.Collections.Generic;
using UnityEngine;

namespace NeonProtocol
{
    // Runtime error monitoring: counts every error/exception (editor and web), keeps the first distinct messages,
    // reports them to the page (web) and to QA scripts. Silent failures must not go unnoticed again.
    public static class RuntimeErrorMonitor
    {
        public static int Errors { get; private set; }
        public static int Exceptions { get; private set; }
        public static readonly List<string> Distinct = new List<string>();
        static bool installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            Errors = Exceptions = 0; Distinct.Clear();
            if (installed) return;
            installed = true;
            Application.logMessageReceived += OnLog;
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (msg.StartsWith("ERROR: Shader")) return;   // URP debug shaders unsupported on WebGL2: expected, harmless
            if (type == LogType.Exception) Exceptions++; else Errors++;
            string first = msg.Split('\n')[0];
            string where = "";
            foreach (var line in stack.Split('\n')) if (line.Contains("NeonProtocol")) { where = " @ " + line.Trim(); break; }
            string key = first + where;
            if (Distinct.Count < 20 && !Distinct.Contains(key))
            {
                Distinct.Add(key);
                WebBridge.Send("{\"type\":\"runtime_error\",\"msg\":\"" + key.Replace("\\", "/").Replace("\"", "'") + "\"}");
            }
        }

        public static string Summary() => $"errors={Errors} exceptions={Exceptions}" + (Distinct.Count > 0 ? " | " + string.Join(" || ", Distinct) : "");
    }
}
