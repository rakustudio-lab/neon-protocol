using UnityEngine;

namespace NeonProtocol
{
    // ACTIVATE mechanic: a security relay that charges from paint hits. Fully charged = online (unlocks the vault).
    // Readable at a glance: dark core -> charging ring fills -> blazing online state. Uses aiming skill, not a button.
    public class Relay : MonoBehaviour, IPaintHittable
    {
        public int hitsRequired = 4;
        public float decayPerSecond = 0.25f;    // partial charge leaks slowly: commit or lose it
        public Renderer coreRenderer;            // emissive core
        public Transform ring;                   // spins faster as it charges
        public Light glow;
        public AudioSource audioSource;
        public AudioClip chargeClip, onlineClip;
        public SignatureRecorder.Milestone milestone = SignatureRecorder.Milestone.RelayA;

        public bool Online { get; private set; }
        public float Charge { get; private set; }
        public event System.Action<Relay> WentOnline;

        static readonly int idEmission = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock _mpb;
        MaterialPropertyBlock mpb => _mpb ??= new MaterialPropertyBlock();   // survives editor domain reloads
        Color baseEmission = new Color(0.4f, 0.1f, 1.2f), onEmission = new Color(0.4f, 4f, 3.4f);
        float pulse;

        void Awake() { Apply(); }

        public void ResetRelay() { Online = false; Charge = 0; Apply(); }

        // Puzzle-driven relays (e.g. the vault rings) set acceptsPaint = false and come online through this.
        public bool acceptsPaint = true;
        public void ForceOnline()
        {
            if (Online) return;
            Charge = 1f; Online = true; pulse = 1f;
            if (audioSource && onlineClip) { audioSource.pitch = 1f; audioSource.PlayOneShot(onlineClip, 1f); }
            HeistDirector.Instance?.Recorder?.Mark(milestone);
            WentOnline?.Invoke(this);
        }

        public bool OnPaintHit(in PaintHit hit)
        {
            if (Online || !acceptsPaint || hit.team != 0) return false;
            Charge = Mathf.Min(1f, Charge + 1f / Mathf.Max(1, hitsRequired));
            pulse = 1f;
            if (audioSource && chargeClip) { audioSource.pitch = 0.8f + Charge * 0.6f; audioSource.PlayOneShot(chargeClip, 0.7f); }
            if (Charge >= 0.999f)
            {
                Online = true;
                if (audioSource && onlineClip) { audioSource.pitch = 1f; audioSource.PlayOneShot(onlineClip, 1f); }
                HeistDirector.Instance?.Recorder?.Mark(SignatureRecorder.Kind.Relay, transform.position);
                HeistDirector.Instance?.Recorder?.Mark(milestone);
                WentOnline?.Invoke(this);
            }
            return true;
        }

        void Update()
        {
            if (!Online && Charge > 0f) Charge = Mathf.Max(0f, Charge - decayPerSecond * Time.deltaTime * 0.25f);
            pulse = Mathf.Max(0f, pulse - Time.deltaTime * 4f);
            if (ring) ring.Rotate(0f, (Online ? 220f : 30f + Charge * 260f) * Time.deltaTime, 0f, Space.Self);
            Apply();
        }

        void Apply()
        {
            Color c = Online ? onEmission * (1f + 0.25f * Mathf.Sin(Time.time * 6f)) : Color.Lerp(baseEmission, onEmission * 0.6f, Charge) * (1f + pulse * 2f);
            if (coreRenderer) { coreRenderer.GetPropertyBlock(mpb); mpb.SetColor(idEmission, c); coreRenderer.SetPropertyBlock(mpb); }
            if (glow) { glow.color = Online ? new Color(0.2f, 1f, 0.9f) : Color.Lerp(new Color(0.5f, 0.2f, 1f), new Color(0.2f, 1f, 0.9f), Charge); glow.intensity = Online ? 6f : 1.5f + Charge * 3f + pulse * 3f; }
        }
    }
}
