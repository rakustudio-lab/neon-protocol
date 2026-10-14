using UnityEngine;

namespace NeonProtocol
{
    // Something on the Finality Run track, expressed in track space so collisions are exact at any speed.
    //   Barrier  : solid block across [x0, x1] — steer around or dodge through it.
    //   Shutter  : blast shutter across [x0, x1]; closed until its linked paint switch is painted.
    //   Shard    : energy shard to collect (drive through).
    //   Switch   : paint target that opens `opens` (a Shutter) — the reason to fire while riding.
    //   Checkpoint / Gate : respawn point / the Finality Gate (needs shards).
    public class TrackElement : MonoBehaviour, IPaintHittable
    {
        public enum Kind { Barrier, Shutter, Shard, Switch, Checkpoint, Gate }
        public Kind kind;
        public float s, x0, x1;
        public TrackElement opens;           // Switch -> Shutter
        public Transform moving;             // shutter door / shard visual
        public Renderer lamp;                // switch / shutter state light
        [HideInInspector] public bool done;  // shard collected, switch painted, shutter open
        Vector3 closedPos;
        static readonly int idBase = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock mpb;

        public bool Blocks => kind == Kind.Barrier || (kind == Kind.Shutter && !done);

        void Awake() { if (moving) closedPos = moving.localPosition; mpb = new MaterialPropertyBlock(); }

        public void ResetElement()
        {
            done = false;
            if (moving) { moving.localPosition = closedPos; moving.gameObject.SetActive(true); }
            SetLamp();
        }

        public bool OnPaintHit(in PaintHit hit)
        {
            if (kind != Kind.Switch || done || hit.team != 0) return false;
            done = true; SetLamp();
            if (opens) { opens.done = true; opens.SetLamp(); }
            FinalityRun.Instance?.OnSwitch(this);
            return true;
        }

        void SetLamp()
        {
            if (!lamp) return;
            Color c = kind == Kind.Switch ? (done ? new Color(0.3f, 3.5f, 2.6f) : new Color(3.5f, 1.6f, 0.3f)) : (done ? new Color(0.3f, 3.5f, 2.6f) : new Color(3.6f, 0.4f, 0.6f));
            lamp.GetPropertyBlock(mpb); mpb.SetColor(idBase, c); lamp.SetPropertyBlock(mpb);
        }

        void Update()
        {
            if (kind == Kind.Shutter && moving)
                moving.localPosition = Vector3.MoveTowards(moving.localPosition, closedPos + (done ? Vector3.up * 7f : Vector3.zero), Time.deltaTime * 14f);
            if (kind == Kind.Shard && moving && !done) moving.Rotate(0f, 160f * Time.deltaTime, 0f, Space.Self);
            if (kind == Kind.Switch && !done && lamp) lamp.transform.localScale = Vector3.one * (0.9f + 0.15f * Mathf.Sin(Time.time * 8f));
        }
    }
}
