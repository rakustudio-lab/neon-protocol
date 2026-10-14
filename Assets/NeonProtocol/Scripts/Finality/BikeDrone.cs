using UnityEngine;

namespace NeonProtocol
{
    // Pursuit drone for the Finality Run. Wakes up when the bike reaches `wakeS`, flies ahead of it, picks a lane,
    // TELEGRAPHS it (a red line on the road + rising tone) for `telegraph` seconds, then fires a paint sweep on that lane.
    // Being in the lane when it fires costs a shield. 3 paint hits shut it down (it spins and falls away).
    public class BikeDrone : MonoBehaviour, IPaintHittable
    {
        public float wakeS, sleepS, lead = 26f, height = 4.2f, telegraph = 1.1f, cooldown = 1.6f;
        public int hitsToDisable = 3;
        public LineRenderer warnLine;
        public Renderer eye;
        public AudioSource audioSource;
        public AudioClip warnClip, fireClip, downClip;

        public bool Disabled { get; private set; }
        int hits; float laneX, t, fallV, wob; enum Phase { Idle, Track, Warn, Fire, Down } Phase phase;
        public bool Warning => phase == Phase.Warn && isActiveAndEnabled;   // QA autopilot reads the telegraph like a player
        public float LaneX => laneX;
        static readonly int idBase = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock mpb;

        public void ResetDrone() { Disabled = false; hits = 0; phase = Phase.Idle; gameObject.SetActive(true); if (warnLine) warnLine.enabled = false; transform.localScale = Vector3.one; }

        public bool OnPaintHit(in PaintHit hit)
        {
            if (Disabled || hit.team != 0) return false;
            hits++;
            if (hits >= hitsToDisable)
            {
                Disabled = true; phase = Phase.Down; fallV = 0f; if (warnLine) warnLine.enabled = false;
                if (audioSource && downClip) audioSource.PlayOneShot(downClip);
                FinalityRun.Instance?.OnDroneDown(this);
            }
            return true;
        }

        void Update()
        {
            var run = FinalityRun.Instance; if (!run || !run.bike || !run.bike.Active) return;
            var bike = run.bike; var track = bike.track;
            if (phase == Phase.Down)
            {
                fallV += 12f * Time.deltaTime; transform.position += Vector3.down * fallV * Time.deltaTime; transform.Rotate(300f * Time.deltaTime, 500f * Time.deltaTime, 0f);
                if (fallV > 30f) gameObject.SetActive(false);
                return;
            }
            if (bike.S < wakeS || bike.S > sleepS) { if (warnLine) warnLine.enabled = false; phase = Phase.Idle; return; }
            if (phase == Phase.Idle) { phase = Phase.Track; t = 0.8f; }
            wob += Time.deltaTime;
            float s = bike.S + lead;
            float hover = Mathf.Sin(wob * 1.7f) * 2.2f;
            Vector3 want = track.World(s, phase == Phase.Track ? hover : laneX, height + Mathf.Sin(wob * 2.3f) * 0.4f);
            transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(bike.transform.position - transform.position);
            t -= Time.deltaTime;
            Color eyeC = new Color(3f, 0.3f, 0.6f);
            if (phase == Phase.Track && t <= 0f)
            {
                phase = Phase.Warn; t = telegraph;
                laneX = Mathf.Clamp(bike.X + Random.Range(-1.5f, 1.5f), -track.HalfWidth(s) + 1.5f, track.HalfWidth(s) - 1.5f);   // aims at you: dodge or steer out
                if (audioSource && warnClip) audioSource.PlayOneShot(warnClip);
            }
            else if (phase == Phase.Warn)
            {
                eyeC = Color.Lerp(new Color(3f, 2f, 0.3f), new Color(4f, 0.2f, 0.2f), 1f - t / telegraph) * (0.6f + 0.4f * Mathf.Sin(Time.time * 30f));
                if (warnLine)
                {
                    warnLine.enabled = true;
                    for (int i = 0; i < warnLine.positionCount; i++) warnLine.SetPosition(i, track.World(bike.S + 4f + i * 6f, laneX, 0.06f));
                    warnLine.widthMultiplier = 0.25f + 0.5f * (1f - t / telegraph);
                }
                if (t <= 0f)
                {
                    phase = Phase.Fire; t = 0.25f;
                    if (audioSource && fireClip) audioSource.PlayOneShot(fireClip);
                    if (Mathf.Abs(bike.X - laneX) < 1.6f) bike.Damage("DRONE SWEEP", false);
                }
            }
            else if (phase == Phase.Fire && t <= 0f) { phase = Phase.Track; t = cooldown; if (warnLine) warnLine.enabled = false; }
            if (eye) { mpb ??= new MaterialPropertyBlock(); eye.GetPropertyBlock(mpb); mpb.SetColor(idBase, eyeC); eye.SetPropertyBlock(mpb); }
        }
    }
}
