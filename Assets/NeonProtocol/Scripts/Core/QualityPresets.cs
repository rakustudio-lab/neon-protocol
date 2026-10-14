using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonProtocol
{
    // LOW / MEDIUM / HIGH. Runtime knobs only (no shader variant explosions).
    public static class QualityPresets
    {
        public static readonly string[] Names = { "LOW", "MEDIUM", "HIGH" };
        public static int Current { get; private set; } = 1;

        // Paint system reads these.
        public static int PaintAtlasSize => 4096;   // same on every preset: 64 MB GPU, fine on desktop browsers
        public static bool PaintDroplets => Current > 0;
        public static int MaxParticles => Current == 0 ? 60 : Current == 1 ? 160 : 320;

        public static event System.Action Changed;

        public static void Apply(int level)
        {
            Current = Mathf.Clamp(level, 0, 2);
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp)
            {
                urp.renderScale = Current == 0 ? 0.7f : Current == 1 ? 0.9f : 1f;
                urp.shadowDistance = Current == 0 ? 18f : Current == 1 ? 32f : 50f;
                urp.msaaSampleCount = Current == 2 ? 4 : 1;
                urp.shadowCascadeCount = 1;
                urp.maxAdditionalLightsCount = Current == 0 ? 2 : Current == 1 ? 4 : 6;   // one cascade on every preset: avoids extra shader variants (slow browser compiles)
            }
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;
            Changed?.Invoke();
        }
    }
}
