using UnityEngine;

namespace NeonProtocol
{
    // EXTRACTION COMPLETE (rescue 2026-10-11).
    // After the lightcycle crosses the Finality Gate, CRYPTO stands on the reactor dais with the stabilised Core and rides
    // the elevator up a shaft of rings. The ascent lasts a fixed time; every REAL Monad block observed during it lights a
    // ring flash and is counted ("blocks finalised during your ascent"). Then the result screen. The heist timer has
    // already stopped at the gate, so the cinematic never costs the player time.
    public class FinalityElevator : MonoBehaviour
    {
        public static FinalityElevator Instance { get; private set; }
        public Transform platform;          // rises
        public Transform standPoint;        // where CRYPTO stands (child of platform)
        public Renderer[] rings;            // shaft rings, lit as the platform passes and on each observed block
        public float height = 54f, duration = 5.2f;
        public Color ringIdle = new Color(0.25f, 0.1f, 0.5f), ringLit = new Color(0.2f, 2.4f, 2.6f), ringBlock = new Color(2.6f, 0.6f, 2.4f);

        public static bool Active { get; private set; }
        public static float Progress { get; private set; }
        public static int Blocks { get; private set; }

        float t, blockFlash; Vector3 platformHome; bool done;
        Transform playerParent; MaterialPropertyBlock mpb;
        static readonly int idEm = Shader.PropertyToID("_EmissionColor"), idBase = Shader.PropertyToID("_BaseColor");

        void Awake() { Instance = this; if (platform) platformHome = platform.localPosition; mpb = new MaterialPropertyBlock(); }
        void OnEnable() { Instance = this; if (MonadFeed.Instance) MonadFeed.Instance.NewBlock += OnBlock; }
        void OnDisable() { if (MonadFeed.Instance) MonadFeed.Instance.NewBlock -= OnBlock; }
        void OnBlock(MonadFeed.Block b) { if (Active) { Blocks++; blockFlash = 1f; } }

        float pending = -1f;
        public void Begin() { pending = 0.65f; done = false; Active = false; Progress = 0f; Blocks = 0; }   // let the gate finale play first

        void StartRide()
        {
            var dir = HeistDirector.Instance; if (!dir || !dir.player) { Finish(); return; }
            Active = true; done = false; t = 0f; Progress = 0f; Blocks = 0;
            if (FinalityRun.Instance && FinalityRun.Instance.bike) FinalityRun.Instance.bike.gameObject.SetActive(false);
            var p = dir.player; playerParent = p.transform.parent;
            p.gameObject.SetActive(true);
            p.InputLocked = true;
            var cc = p.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
            p.transform.SetParent(standPoint, false); p.transform.localPosition = Vector3.zero; p.transform.localRotation = Quaternion.identity;
            var rig = FindFirstObjectByType<ShoulderRig>(FindObjectsInactive.Include); if (rig) rig.enabled = false;
            dir.AnnounceText("EXTRACTION COMPLETE");
        }

        public void ResetElevator()
        {
            pending = -1f; Active = false; Progress = 0f; done = true;
            if (platform) platform.localPosition = platformHome;
            var dir = HeistDirector.Instance;
            if (dir && dir.player && dir.player.transform.parent == standPoint)
            {
                dir.player.transform.SetParent(playerParent, true);
                var cc = dir.player.GetComponent<CharacterController>(); if (cc) cc.enabled = true;
                dir.player.InputLocked = false;
            }
            var rig = FindFirstObjectByType<ShoulderRig>(FindObjectsInactive.Include); if (rig) rig.enabled = true;   // R4: the ride disabled it
        }

        void Update()
        {
            if (pending > 0f) { pending -= Time.deltaTime; if (pending <= 0f) { pending = -1f; StartRide(); } }
            blockFlash = Mathf.Max(0f, blockFlash - Time.deltaTime * 2.5f);
            if (rings != null)
                for (int i = 0; i < rings.Length; i++)
                {
                    if (!rings[i]) continue;
                    float ringH = (i + 1f) / rings.Length, lit = Active || Progress > 0f ? Mathf.Clamp01((Progress - ringH) * 12f + 1f) : 0f;
                    Color c = Color.Lerp(ringIdle, ringLit, lit) + ringBlock * blockFlash * lit;
                    rings[i].GetPropertyBlock(mpb); mpb.SetColor(idEm, c); mpb.SetColor(idBase, c); rings[i].SetPropertyBlock(mpb);
                }
            if (!Active || done) return;
            t += Time.deltaTime;
            Progress = Mathf.Clamp01(t / duration);
            float k = Progress * Progress * (3f - 2f * Progress);
            if (platform) platform.localPosition = platformHome + Vector3.up * height * k;
            if (t >= duration + 0.6f) Finish();
        }

        void LateUpdate()
        {
            if (!Active || !platform) return;
            var cam = Camera.main; if (!cam) return;
            var dir = HeistDirector.Instance; Vector3 focus = (dir && dir.player ? dir.player.transform.position : platform.position) + Vector3.up * 1.3f;
            float a = (200f + t * 22f) * Mathf.Deg2Rad, r = Mathf.Lerp(6.5f, 10f, Progress);
            Vector3 pos = focus + new Vector3(Mathf.Sin(a) * r, Mathf.Lerp(0.8f, 3.5f, Progress), Mathf.Cos(a) * r);
            cam.transform.position = pos; cam.transform.rotation = Quaternion.LookRotation(focus - pos + Vector3.up * Mathf.Lerp(0f, 1.5f, Progress));
            cam.fieldOfView = Mathf.Lerp(58f, 66f, Progress);
        }

        void Finish()
        {
            if (done) return; done = true;
            HeistDirector.Instance?.CompleteElevator(Blocks);
            Active = false;
        }
    }
}
