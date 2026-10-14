using UnityEngine;

namespace NeonProtocol
{
    // One paint node on a ring: needs `hits` paint hits within `window` seconds (sustained tracking of a moving target).
    public class VaultNode : MonoBehaviour, IPaintHittable
    {
        public int hits = 3; public float window = 2f;
        public Renderer lamp;
        [HideInInspector] public VaultRings owner;
        int count; float first; bool locked;
        Vector3 lastPos;
        public bool Locked => locked;
        public Vector3 Velocity { get; private set; }      // for lead (QA autopilot) and readability
        static readonly int idEmission = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock _mpb; MaterialPropertyBlock mpb => _mpb ??= new MaterialPropertyBlock();

        public bool OnPaintHit(in PaintHit hit)
        {
            if (locked || hit.team != 0) return false;
            if (count == 0 || Time.time - first > window) { count = 0; first = Time.time; }
            count++;
            if (count >= hits) { locked = true; owner?.NodeLocked(this); }
            return true;
        }

        public void ResetNode() { locked = false; count = 0; }

        void Update()
        {
            if (Time.deltaTime > 0f) Velocity = (transform.position - lastPos) / Time.deltaTime;
            lastPos = transform.position;
            if (!lamp) return;
            bool charging = count > 0 && Time.time - first <= window;
            Color c = locked ? new Color(0.3f, 4f, 3.4f) : charging ? new Color(4f, 3f, 0.5f) * (0.7f + 0.3f * Mathf.Sin(Time.time * 30f)) : new Color(4f, 0.3f, 1.6f) * (0.6f + 0.4f * Mathf.Sin(Time.time * 4f));
            lamp.GetPropertyBlock(mpb); mpb.SetColor(idEmission, c); lamp.SetPropertyBlock(mpb);
        }
    }
}
