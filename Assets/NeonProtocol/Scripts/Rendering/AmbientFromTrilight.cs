using UnityEngine;
using UnityEngine.Rendering;

namespace NeonProtocol
{
    // The scene's ambient probe lives in a stale LightingData asset (generated with the old, darker trilight), so
    // editing RenderSettings' sky/equator/ground colours never reached the renderer. This owns the ambient instead:
    // a sky/equator/ground gradient projected onto L2 spherical harmonics, applied at load in the Editor and in
    // player builds. No baking, no extra lights.
    [ExecuteAlways]
    public class AmbientFromTrilight : MonoBehaviour
    {
        public Color sky = new Color(0.42f, 0.38f, 0.62f), equator = new Color(0.30f, 0.26f, 0.45f), ground = new Color(0.14f, 0.11f, 0.2f);
        [Tooltip("1 = the same brightness Unity's own Trilight mode gives these colours.")]
        public float intensity = 1f;
        const float UnityTrilightScale = 0.2f;   // calibrated: Build(old colours) * 0.2 == the probe Unity generated for them

        void OnEnable() => Apply();
#if UNITY_EDITOR
        void OnValidate() => Apply();
#endif

        // Custom mode: the script-assigned probe is authoritative (in Trilight mode Unity keeps serving the stale one).
        public void Apply()
        {
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = Build(sky.linear, equator.linear, ground.linear, UnityTrilightScale * intensity);
        }

        // Radiance f(n): equator at the horizon, blending to sky (up) / ground (down); projected to SH via
        // AddDirectionalLight over a uniform sphere sampling (each sample a small light of solid angle 4pi/N).
        public static SphericalHarmonicsL2 Build(Color sky, Color eq, Color ground, float scale)
        {
            var sh = new SphericalHarmonicsL2();
            const int N = 512; float w = 4f * Mathf.PI / N * scale;
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < N; i++)
            {
                float y = 1f - (i + 0.5f) * 2f / N, r = Mathf.Sqrt(1f - y * y), th = golden * i;
                var d = new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r);
                Color c = y >= 0f ? Color.Lerp(eq, sky, y) : Color.Lerp(eq, ground, -y);
                sh.AddDirectionalLight(d, c, w);
            }
            return sh;
        }
    }
}
