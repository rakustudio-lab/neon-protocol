using UnityEngine;

namespace NeonProtocol
{
    // The Relay Room's visible destination: a double door that stays sealed (amber, "locked") until Relay A is online,
    // then slides open (cyan) and carries the runner through to the vault approach. Featured and full routes both
    // start in the Relay Room; the full route continues at the old arrival to do relays B and C.
    public class RoomExit : MonoBehaviour
    {
        public Relay relay;
        public Transform leafL, leafR;
        public Light doorLight;
        public Collider block;                     // physical door while sealed
        public Renderer[] signals;                 // small lamps beside the door: amber locked, cyan open
        public Material lockedMat, openMat;
        public Transform featuredTarget, fullTarget;
        public float openWidth = 1.25f;

        Vector3 homeL, homeR, openingAxis; float openT; bool used;   // leaf homes in WORLD space (the kit door model is scaled x100)
        public bool Open => relay && relay.Online && (!PaintballEncounter.Instance || PaintballEncounter.Instance.Complete);

        void Awake()
        {
            if (leafL) homeL = leafL.position;
            if (leafR) homeR = leafR.position;
            // Both imported leaf pivots are at the origin; use visible centres, not
            // pivot sorting or root.right (the exit root faces back into the room).
            var l = leafL ? leafL.GetComponentInChildren<Renderer>() : null;
            var r = leafR ? leafR.GetComponentInChildren<Renderer>() : null;
            openingAxis = l && r ? Vector3.ProjectOnPlane(r.bounds.center - l.bounds.center, Vector3.up).normalized : transform.right;
            if (openingAxis.sqrMagnitude < 0.5f) openingAxis = transform.right;
        }
        void OnEnable() { if (HeistDirector.Instance) HeistDirector.Instance.PhaseChanged += OnPhase; }
        void OnDisable() { if (HeistDirector.Instance) HeistDirector.Instance.PhaseChanged -= OnPhase; }
        void OnPhase(HeistDirector.Phase p) { if (p == HeistDirector.Phase.Infiltrate || p == HeistDirector.Phase.Briefing) { used = false; openT = 0f; } }

        void Update()
        {
            openT = Mathf.MoveTowards(openT, Open ? 1f : 0f, Time.deltaTime * 0.9f);
            float k = Mathf.SmoothStep(0f, 1f, openT);
            if (leafL) leafL.position = homeL - openingAxis * openWidth * k;
            if (leafR) leafR.position = homeR + openingAxis * openWidth * k;
            if (block) block.enabled = openT < 0.98f;
            if (doorLight) doorLight.color = Color.Lerp(new Color(1f, 0.62f, 0.25f), new Color(0.35f, 0.95f, 1f), k);
            if (signals != null) foreach (var s in signals) if (s) s.sharedMaterial = Open ? openMat : lockedMat;
        }

        // R4: the trigger sits inside the vestibule behind the door (the runner has physically crossed the opening).
        // Stay as well as Enter: a runner already standing in it when the door finishes opening still goes through.
        void OnTriggerEnter(Collider other) => TryPass(other);
        void OnTriggerStay(Collider other) => TryPass(other);
        void TryPass(Collider other)
        {
            if (used || !Open || openT < 0.98f) return;
            var pm = other.GetComponentInParent<PlayerMotor>(); if (!pm) return;
            var t = HeistDirector.FullRoute ? fullTarget : featuredTarget; if (!t) return;
            used = true; Passed++; fadeUntil = Time.unscaledTime + FadeSec;
            pm.Teleport(t.position, t.eulerAngles.y);
            if (pm.rig) pm.rig.SetAim(t.eulerAngles.y, 0f);
            HeistDirector.Instance?.AnnounceText(HeistDirector.FullRoute ? "THE FRACTURE" : "THE VAULT");
        }

        public static int Passed { get; private set; }   // QA: door crossings this session
        const float FadeSec = 0.5f; static float fadeUntil;
        void OnGUI()
        {
            float a = (fadeUntil - Time.unscaledTime) / FadeSec; if (a <= 0f) return;
            GUI.color = new Color(0, 0, 0, Mathf.Clamp01(a)); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = Color.white;
        }
    }
}
