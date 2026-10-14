using UnityEngine;

namespace NeonProtocol
{
    // The Finality Gate: monumental doors that part when the Core is taken; stepping through with the Core extracts.
    public class ExtractionGate : MonoBehaviour
    {
        public Transform leftDoor, rightDoor;
        public float openDistance = 3.2f, openTime = 2.4f;
        public Renderer[] energyRenderers;      // emissive strips that ignite when open
        public Light gateLight;
        public Transform beam;            // light column that rises when the gate opens
        public float beamHeight = 30f;
        public Collider extractTrigger;
        public AudioSource audioSource;
        public AudioClip openClip, extractClip;

        public bool IsOpen { get; private set; }
        public event System.Action Extracted;

        Vector3 lHome, rHome; float t;
        static readonly int idEmission = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock _mpb;
        MaterialPropertyBlock mpb => _mpb ??= new MaterialPropertyBlock();

        void Awake()
        {
            if (leftDoor) lHome = leftDoor.localPosition;
            if (rightDoor) rHome = rightDoor.localPosition;
            Apply();
        }

        public void ResetGate() { IsOpen = false; t = 0f; Apply(); }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            if (audioSource && openClip) audioSource.PlayOneShot(openClip);
        }

        void Update()
        {
            float target = IsOpen ? 1f : 0f;
            if (!Mathf.Approximately(t, target)) { t = Mathf.MoveTowards(t, target, Time.deltaTime / openTime); Apply(); }
            else if (IsOpen && gateLight) gateLight.intensity = 7f + Mathf.Sin(Time.time * 3f) * 1.5f;
        }

        void Apply()
        {
            float e = t * t * (3f - 2f * t);
            if (leftDoor) leftDoor.localPosition = lHome + Vector3.left * openDistance * e;
            if (rightDoor) rightDoor.localPosition = rHome + Vector3.right * openDistance * e;
            Color c = Color.Lerp(new Color(0.3f, 0.05f, 0.8f), new Color(0.3f, 3.5f, 3f), e);
            if (energyRenderers != null) foreach (var r in energyRenderers) if (r) { r.GetPropertyBlock(mpb); mpb.SetColor(idEmission, c); r.SetPropertyBlock(mpb); }
            if (beam) { beam.localScale = new Vector3(1f, Mathf.Max(0.001f, e) * beamHeight, 1f); beam.gameObject.SetActive(e > 0.01f); }
            if (gateLight) { gateLight.color = Color.Lerp(new Color(0.5f, 0.2f, 1f), new Color(0.2f, 1f, 0.9f), e); gateLight.intensity = 2f + 5f * e; }
        }

        void OnTriggerEnter(Collider other) => HandleTrigger(other);

        public void HandleTrigger(Collider other)
        {
            if (!IsOpen) return;
            var motor = other.GetComponentInParent<PlayerMotor>();
            var core = HeistDirector.Instance ? HeistDirector.Instance.core : null;
            if (motor && core && core.Carried)
            {
                if (audioSource && extractClip) audioSource.PlayOneShot(extractClip);
                Extracted?.Invoke();
            }
        }
    }
}
