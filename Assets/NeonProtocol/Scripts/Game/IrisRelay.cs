using UnityEngine;

namespace NeonProtocol
{
    // RELAY A — "the Iris" (first playable minute). Four intentional hits instead of holding fire into a cube:
    //   pads 1 / 2 / 3 must be painted in order (only the next pad is lit strongly, numbered so it reads without colour);
    //   a wrong pad gives a quiet rejection tick; no decay, no delay; one hit latches a pad, repeats do nothing;
    //   after pad 3 the petals retract and the core rises: its collider exists only then; one core hit = relay online.
    // The existing Relay stays the mission-state owner: ForceOnline() once, plus the spatial Kind.Relay mark once.
    // Rival paint never advances progress. Resets at the start of every run (HeistDirector.PhaseChanged -> Infiltrate).
    // Motion is computed in world space: the FBX carries a x100 inner scale, so local offsets would be wrong.
    public class IrisRelay : MonoBehaviour
    {
        public Relay relay;
        public Renderer[] pads = new Renderer[3];
        public Transform[] petals = new Transform[3];
        public Transform core;
        public Collider coreCollider;
        public Renderer coreRenderer;
        public TMPro.TextMeshPro[] padNumbers = new TMPro.TextMeshPro[3];
        public AudioSource audioSource;
        public AudioClip padClip, rejectClip, openClip, onlineClip;
        public Transform[] pulsePath;     // world points: relay -> catwalk -> reactor
        public Light pulseLight;
        public Renderer pulseOrb;
        public Transform visual;          // IrisVisual: its +Y is the relay face normal

        public int Next { get; private set; }        // 0..2 pads, 3 = core exposed, 4 = done
        public bool Done => Next >= 4;
        public float DoneAt { get; private set; } = float.MaxValue;   // Time.time of the core hit (rivals give a breather after it)

        static readonly Color[] PadColors = { new Color(0.15f, 2.6f, 2.8f), new Color(1.6f, 0.5f, 3.0f), new Color(3.0f, 1.6f, 0.25f) };
        static readonly int idEm = Shader.PropertyToID("_EmissionColor"), idBase = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock mpb;
        Vector3[] petalHome; Quaternion[] petalRot; Vector3 coreHome, center;
        float open, openVel, rejectT, pulseT = -1f;
        bool cached;

        void Cache()
        {
            if (cached) return; cached = true;
            mpb = new MaterialPropertyBlock();
            petalHome = new Vector3[petals.Length]; petalRot = new Quaternion[petals.Length];
            for (int i = 0; i < petals.Length; i++) if (petals[i]) { petalHome[i] = petals[i].position; petalRot[i] = petals[i].rotation; }
            if (core) coreHome = core.position;
            center = core ? core.position : transform.position;
        }

        void Awake() { Cache(); }
        void OnEnable()
        {
            Cache();
            if (HeistDirector.Instance) HeistDirector.Instance.PhaseChanged += OnPhase;
            ResetIris();
        }
        void Start() { if (HeistDirector.Instance) { HeistDirector.Instance.PhaseChanged -= OnPhase; HeistDirector.Instance.PhaseChanged += OnPhase; } }
        void OnDisable() { if (HeistDirector.Instance) HeistDirector.Instance.PhaseChanged -= OnPhase; }
        void OnPhase(HeistDirector.Phase p) { if (p == HeistDirector.Phase.Infiltrate || p == HeistDirector.Phase.Briefing) ResetIris(); }

        public void ResetIris()
        {
            Next = 0; open = 0f; openVel = 0f; pulseT = -1f; DoneAt = float.MaxValue;
            if (coreCollider) coreCollider.enabled = false;
            if (pulseLight) pulseLight.enabled = false;
            if (pulseOrb) pulseOrb.enabled = false;
            if (relay) relay.acceptsPaint = false;   // the Iris owns progress; the old aggregate collider is disabled at build time
        }

        public bool Hit(int index, in PaintHit hit)
        {
            if (hit.team != 0) return true;                       // rival paint: splat, no progress
            if (Done || (relay && relay.Online)) return true;
            if (index == Next && index < 3)
            {
                Next++;
                Play(padClip, 0.9f + Next * 0.12f, 0.8f);
                if (Next == 3) Play(openClip, 1f, 0.9f);
                HeistDirector.Instance?.AnnounceText(Next < 3 ? $"RELAY A · PAD {Next}/3 LATCHED" : "RELAY A · IRIS OPEN — PAINT THE CORE");
            }
            else if (index == 3 && Next == 3)
            {
                Next = 4; DoneAt = Time.time;
                // the spatial relay event exactly once (ForceOnline marks only the milestone)
                HeistDirector.Instance?.Recorder?.Mark(SignatureRecorder.Kind.Relay, transform.position);
                relay?.ForceOnline();
                Play(onlineClip, 1f, 1f);
                pulseT = 0f;
            }
            else if (index < 3 && index > Next) { rejectT = 1f; Play(rejectClip, 1.4f, 0.35f); }   // not the next pad: quiet tick
            return true;
        }

        void Play(AudioClip c, float pitch, float vol) { if (audioSource && c) { audioSource.pitch = pitch; audioSource.PlayOneShot(c, vol); } }

        void Update()
        {
            Cache();
            float dt = Time.deltaTime, t = Time.time;
            rejectT = Mathf.Max(0f, rejectT - dt * 4f);
            // pads: latched = solid, next = strong pulse, later = dim
            for (int i = 0; i < pads.Length; i++)
            {
                if (!pads[i]) continue;
                bool latched = i < Next, isNext = i == Next && Next < 3;
                float k = latched ? 1.0f : isNext ? 0.75f + 0.45f * Mathf.Sin(t * 7f) : 0.06f;
                Color c = PadColors[i] * k; if (isNext && rejectT > 0f) c = Color.Lerp(c, new Color(2.5f, 0.2f, 0.25f), rejectT);
                pads[i].GetPropertyBlock(mpb); mpb.SetColor(idEm, c); mpb.SetColor(idBase, Color.Lerp(new Color(0.08f, 0.09f, 0.12f), PadColors[i] * 0.25f, latched ? 1f : isNext ? 0.6f : 0f)); pads[i].SetPropertyBlock(mpb);
                if (padNumbers != null && i < padNumbers.Length && padNumbers[i]) padNumbers[i].color = latched ? new Color(0.05f, 0.05f, 0.08f, 1f) : isNext ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            }
            // iris: petals spin out and retract once pad 3 latches; the core rises and lights
            open = Springs.Step(open, ref openVel, Next >= 3 ? 1f : 0f, 7f, dt);
            Vector3 face = visual ? visual.up : Vector3.left;
            for (int i = 0; i < petals.Length; i++)
            {
                if (!petals[i]) continue;
                Vector3 radial = Vector3.ProjectOnPlane(petalHome[i] - center, face);
                Vector3 outDir = radial.sqrMagnitude > 1e-6f ? radial.normalized : Vector3.up;
                petals[i].position = petalHome[i] + outDir * 0.2f * open - face * 0.07f * open;
                petals[i].rotation = Quaternion.AngleAxis(55f * open, face) * petalRot[i];
            }
            if (core) core.position = coreHome + face * (0.09f * open - 0.06f * (1f - open));
            if (coreCollider) coreCollider.enabled = Next == 3 && open > 0.6f;
            if (coreRenderer)
            {
                Color cc = Next >= 4 ? new Color(0.3f, 3.2f, 3.0f) * (1f + 0.2f * Mathf.Sin(t * 6f)) : Next == 3 ? new Color(2.8f, 2.8f, 3.2f) * (0.7f + 0.3f * Mathf.Sin(t * 9f)) : new Color(0.05f, 0.05f, 0.1f);
                coreRenderer.GetPropertyBlock(mpb); mpb.SetColor(idEm, cc); coreRenderer.SetPropertyBlock(mpb);
            }
            // power pulse travelling from the relay along the catwalk toward the reactor (local response, not a chain event)
            if (pulseT >= 0f && pulsePath != null && pulsePath.Length > 1)
            {
                pulseT += dt;
                float seg = pulseT / 0.55f; int i0 = Mathf.Min(pulsePath.Length - 2, Mathf.FloorToInt(seg));
                float travel = (pulsePath.Length - 1) * 0.55f;
                bool alive = pulseT < travel;
                Vector3 p = alive ? Vector3.Lerp(pulsePath[i0].position, pulsePath[i0 + 1].position, Mathf.SmoothStep(0, 1, seg - i0)) : pulsePath[pulsePath.Length - 1].position;
                if (pulseLight) { pulseLight.enabled = pulseT < travel + 0.8f; pulseLight.transform.position = p; pulseLight.intensity = alive ? 9f : Mathf.Max(0f, 9f - (pulseT - travel) * 12f); }
                if (pulseOrb) { pulseOrb.enabled = alive; pulseOrb.transform.position = p; }
                if (pulseT > travel + 1f) pulseT = -1f;
            }
        }
    }
}
