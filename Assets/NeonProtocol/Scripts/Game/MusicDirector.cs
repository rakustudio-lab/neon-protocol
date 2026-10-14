using UnityEngine;

namespace NeonProtocol
{
    // Exploration needs atmosphere, pursuit needs energy, extraction needs a climax: crossfades by heist phase.
    public class MusicDirector : MonoBehaviour
    {
        public AudioClip ambient, pursuit, extractSting;
        public float ambientVolume = 0.35f, pursuitVolume = 0.55f, fade = 1.6f;
        AudioSource a, p, s;

        void Awake()
        {
            a = Make(ambient, true); p = Make(pursuit, true); s = Make(extractSting, false);
            p.volume = 0f;
        }

        AudioSource Make(AudioClip c, bool loop)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = c; src.loop = loop; src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0f; src.priority = 0;
            return src;
        }

        void Start()
        {
            if (a.clip) a.Play();
            if (HeistDirector.Instance) HeistDirector.Instance.PhaseChanged += OnPhase;
        }

        void OnPhase(HeistDirector.Phase ph)
        {
            if (ph == HeistDirector.Phase.Pursuit && p.clip && !p.isPlaying) p.Play();
            if (ph == HeistDirector.Phase.Extracted && s.clip) { s.volume = 0.9f; s.Play(); }
        }

        void Update()
        {
            var d = HeistDirector.Instance;
            bool pursuitOn = d && d.Current == HeistDirector.Phase.Pursuit;
            bool ended = d && (d.Current == HeistDirector.Phase.Extracted || d.Current == HeistDirector.Phase.Failed);
            float k = Time.unscaledDeltaTime / fade;
            a.volume = Mathf.MoveTowards(a.volume, pursuitOn || ended ? 0.05f : ambientVolume, k * ambientVolume);
            p.volume = Mathf.MoveTowards(p.volume, pursuitOn ? pursuitVolume : 0f, k * pursuitVolume);
            if (!pursuitOn && p.isPlaying && p.volume <= 0.001f) p.Stop();
        }
    }
}
