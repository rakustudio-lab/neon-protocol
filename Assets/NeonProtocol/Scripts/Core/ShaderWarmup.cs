using UnityEngine;

namespace NeonProtocol
{
    // Browser-friendly startup: compile the recorded shader variants a few per frame (progress reported to the page)
    // instead of letting the first rendered frame compile everything at once (a multi-second main-thread freeze).
    [DefaultExecutionOrder(-90)]
    public class ShaderWarmup : MonoBehaviour
    {
        public ShaderVariantCollection variants;
        public int variantsPerFrame = 3;
        public Camera[] holdCameras;      // disabled until warm

        public static bool Done { get; private set; }
        public static float Progress { get; private set; }
        float started;

        void Awake()
        {
            Done = variants == null;
            if (Done) return;
            foreach (var c in holdCameras) if (c) c.enabled = false;
            started = Time.realtimeSinceStartup;
        }

        float lastFrameStart; int lastWarmed;
        readonly System.Text.StringBuilder slow = new System.Text.StringBuilder();

        void Update()
        {
            if (Done) return;
            float now = Time.realtimeSinceStartup;
            if (lastFrameStart > 0f && now - lastFrameStart > 0.15f) slow.Append($"[{lastWarmed}:{(now - lastFrameStart) * 1000f:F0}ms]");
            lastFrameStart = now; lastWarmed = variants.warmedUpVariantCount;
            bool finished = variants.WarmUpProgressively(variantsPerFrame);
            Progress = variants.variantCount > 0 ? Mathf.Clamp01((float)variants.warmedUpVariantCount / variants.variantCount) : 1f;
            WebBridge.Send("{\"type\":\"warmup\",\"progress\":" + Progress.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "}");
            if (finished)
            {
                Done = true; Progress = 1f;
                foreach (var c in holdCameras) if (c) c.enabled = true;
                Debug.Log($"[NP] shader warmup {variants.warmedUpVariantCount} variants in {(Time.realtimeSinceStartup - started) * 1000f:F0} ms slow={slow}");
            }
        }
    }
}
