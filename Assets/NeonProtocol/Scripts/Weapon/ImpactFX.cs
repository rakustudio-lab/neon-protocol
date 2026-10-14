using UnityEngine;

namespace NeonProtocol
{
    // Impact moment: crown spray particles, a short luminous flash, wet splat sound.
    public class ImpactFX : MonoBehaviour
    {
        public static ImpactFX Instance { get; private set; }

        public ParticleSystem sprayA, sprayB;      // world-space burst systems, emitted manually
        public Light[] flashLights;                 // small pool of point lights
        public AudioClip[] splatClips, hitClips;
        public AudioSource sourcePrefab;
        public int audioVoices = 8;

        AudioSource[] voices; int nextVoice, nextLight;
        float[] lightT;

        void Awake()
        {
            Instance = this;
            voices = new AudioSource[audioVoices];
            for (int i = 0; i < audioVoices; i++)
            {
                var go = new GameObject("ImpactVoice" + i);
                go.transform.SetParent(transform);
                var a = go.AddComponent<AudioSource>();
                a.playOnAwake = false; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Logarithmic;
                a.minDistance = 2.5f; a.maxDistance = 45f; a.dopplerLevel = 0f;
                voices[i] = a;
            }
            lightT = new float[flashLights?.Length ?? 0];
            if (flashLights != null) foreach (var l in flashLights) if (l) l.enabled = false;
        }

        public void Play(Vector3 point, Vector3 normal, int team, bool hitTarget)
        {
            var ps = team == 0 ? sprayA : sprayB;
            if (ps)
            {
                var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                int n = Mathf.Min(QualityPresets.MaxParticles / 10, 16);
                for (int i = 0; i < n; i++)
                {
                    Vector3 v = (normal * Random.Range(0.8f, 1.6f) + Random.insideUnitSphere).normalized * Random.Range(1.5f, 6f);
                    ep.position = point + normal * 0.03f;
                    ep.velocity = v;
                    ep.startSize = Random.Range(0.025f, 0.07f);
                    ep.startLifetime = Random.Range(0.25f, 0.55f);
                    ps.Emit(ep, 1);
                }
            }

            if (flashLights != null && flashLights.Length > 0)
            {
                int i = nextLight++ % flashLights.Length;
                var l = flashLights[i];
                if (l)
                {
                    l.transform.position = point + normal * 0.25f;
                    l.color = TeamColorLdr(team);
                    l.enabled = true; lightT[i] = 1f;
                }
            }

            var clips = hitTarget && hitClips != null && hitClips.Length > 0 ? hitClips : splatClips;
            if (clips != null && clips.Length > 0)
            {
                var a = voices[nextVoice++ % voices.Length];
                a.transform.position = point;
                a.pitch = Random.Range(0.9f, 1.12f);
                a.volume = hitTarget ? 0.9f : Random.Range(0.55f, 0.75f);
                a.clip = clips[Random.Range(0, clips.Length)];
                a.Play();
            }
        }

        public static Color TeamColorLdr(int team)
        {
            var ps = PaintSystem.Instance;
            Color c = ps ? (team == 0 ? ps.colorA : ps.colorB) : (team == 0 ? Color.cyan : Color.magenta);
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return m > 1f ? new Color(c.r / m, c.g / m, c.b / m, 1f) : c;
        }

        void Update()
        {
            if (flashLights == null) return;
            for (int i = 0; i < flashLights.Length; i++)
            {
                if (lightT[i] <= 0f || !flashLights[i]) continue;
                lightT[i] -= Time.deltaTime / 0.14f;
                flashLights[i].intensity = Mathf.Max(0f, lightT[i]) * 3.5f;
                if (lightT[i] <= 0f) flashLights[i].enabled = false;
            }
        }
    }
}
