using System.Runtime.InteropServices;
using UnityEngine;

namespace NeonProtocol
{
    // Unity <-> page communication. In the editor / desktop every call is a no-op.
    public static class WebBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void NP_Ready();
        [DllImport("__Internal")] static extern void NP_Event(string json);
#else
        static void NP_Ready() { }
        static void NP_Event(string json) { }
#endif
        public static void NotifyReady() { try { NP_Ready(); } catch { } }
        public static void Send(string json) { try { NP_Event(json); } catch { } }
    }
}
