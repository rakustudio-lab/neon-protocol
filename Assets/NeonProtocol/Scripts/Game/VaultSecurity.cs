using UnityEngine;

namespace NeonProtocol
{
    // Detection with consequences: sweeping scanner beams in the vault. Crossing one standing up (or being seen
    // by the drone) triggers a LOCKDOWN: alarm, exit shutters close for a while, hunters are called to you,
    // and unfinished ring progress resets. Crouch under the beams, time them, or take the balcony route.
    public class VaultSecurity : MonoBehaviour
    {
        public static VaultSecurity Instance { get; private set; }

        [System.Serializable] public class Beam { public Transform beam; public Vector3 from, to; public float period = 6f; public float phase; public Vector3 halfExtents = new Vector3(13f, 0.08f, 0.15f); }
        public Beam[] beams;
        public Transform[] shutters;             // move down to close
        public float lockdownTime = 8f, cooldown = 3f;   // shutters sit closed in the scene; they hide below the floor by their own height
        public AudioSource audioSource;
        public AudioClip alarmClip, scanClip;
        public Renderer[] beamRenderers;           // tint red during lockdown

        public bool Locked { get; private set; }
        public int LockdownCount { get; private set; }
        public float LockRemaining => Locked ? Mathf.Max(0f, lockUntil - Time.time) : 0f;
        public event System.Action Lockdown;

        float lockUntil, cooldownUntil, shutterT;
        Vector3[] shutterHome; float[] shutterDrop;
        PlayerMotor player;

        static readonly int idEmission = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock _mpb; MaterialPropertyBlock mpb => _mpb ??= new MaterialPropertyBlock();

        void Awake() { Instance = this; }
        void OnEnable() { Instance = this; }

        void Start()
        {
            player = HeistDirector.Instance ? HeistDirector.Instance.player : FindFirstObjectByType<PlayerMotor>();
            shutterHome = new Vector3[shutters != null ? shutters.Length : 0];
            shutterDrop = new float[shutterHome.Length];
            for (int i = 0; i < shutterHome.Length; i++) if (shutters[i])
            {
                shutterHome[i] = shutters[i].position;
                var r = shutters[i].GetComponent<Renderer>();
                shutterDrop[i] = (r ? r.bounds.size.y : 6f) + 0.4f;
                shutters[i].position = shutterHome[i] + Vector3.down * shutterDrop[i];
            }
        }

        public void Trigger(Vector3 at)
        {
            if (!HeistDirector.FullRoute || Locked || Time.time < cooldownUntil) return;
            var d = HeistDirector.Instance;
            if (d == null || !(d.Current == HeistDirector.Phase.Infiltrate || d.Current == HeistDirector.Phase.CoreUnlocked)) return;
            Locked = true; LockdownCount++; lockUntil = Time.time + lockdownTime;
            if (audioSource && alarmClip) audioSource.PlayOneShot(alarmClip);
            NoiseSystem.Emit(at, 45f, this);                                   // every hunter hears it
            foreach (var dr in CompetitorAIRegistry.Drones) if (dr) dr.Investigate(at);
            d.AnnounceText("DETECTED — VAULT LOCKDOWN");
            Lockdown?.Invoke();
        }

        void Update()
        {
            float t = Time.time;
            bool active = HeistDirector.FullRoute;
            // Featured route: no scanner wall, forced wait, alarm, or reset punishment.
            if (beams != null) foreach (var b in beams)
                if (b != null && b.beam && b.beam.gameObject.activeSelf != active)
                    b.beam.gameObject.SetActive(active);
            if (beamRenderers != null) foreach (var r in beamRenderers)
                if (r) r.enabled = active;
            if (!active) { Locked = false; lockUntil = cooldownUntil = 0f; shutterT = 0f; }
            // sweep beams + detect the player crossing them while standing
            if (active && beams != null && player)
                foreach (var b in beams)
                {
                    if (!b.beam) continue;
                    float k = 0.5f - 0.5f * Mathf.Cos((t + b.phase) / b.period * Mathf.PI * 2f);
                    b.beam.position = Vector3.Lerp(b.from, b.to, k);
                    if (Locked) continue;
                    var hits = Physics.OverlapBox(b.beam.position, b.halfExtents, b.beam.rotation, 1 << 8, QueryTriggerInteraction.Ignore);
                    if (hits.Length > 0 && !player.TaggedOut) Trigger(player.transform.position);
                }
            if (beamRenderers != null)
            {
                var ph = HeistDirector.Instance ? HeistDirector.Instance.Current : HeistDirector.Phase.Briefing;
                bool armed = ph == HeistDirector.Phase.Infiltrate || ph == HeistDirector.Phase.CoreUnlocked;
                Color c = Locked ? new Color(6f, 0.3f, 0.25f) * (0.6f + 0.4f * Mathf.Sin(t * 18f)) : armed ? new Color(4.5f, 0.6f, 0.3f) : new Color(0.4f, 0.1f, 0.05f);
                foreach (var br in beamRenderers) if (br) { br.GetPropertyBlock(mpb); mpb.SetColor(idEmission, c); br.SetPropertyBlock(mpb); }
            }
            if (Locked && t >= lockUntil) { Locked = false; cooldownUntil = t + cooldown; }
            shutterT = Mathf.MoveTowards(shutterT, Locked ? 1f : 0f, Time.deltaTime * (Locked ? 3f : 1.2f));
            for (int i = 0; i < shutterHome.Length; i++) if (shutters[i])
            {
                shutters[i].position = shutterHome[i] + Vector3.down * shutterDrop[i] * (1f - shutterT);
                var ob = shutters[i].GetComponent<UnityEngine.AI.NavMeshObstacle>();
                if (ob) ob.enabled = shutterT > 0.5f;      // hunters path around a closed shutter, ignore it when it is down
            }
        }
    }
}
