using UnityEngine;

namespace NeonProtocol
{
    // Test-chamber target: counts qualifying hits, "tags out" at 3, then resets clean.
    public class PaintTarget : MonoBehaviour, IPaintHittable
    {
        public int hitsToTag = 3;
        public float resetDelay = 2.2f;
        public int Hits { get; private set; }
        public int Tags { get; private set; }
        public bool TaggedOut { get; private set; }
        float resetAt;
        PaintDynamic paint;
        TargetMover mover;

        void Awake() { paint = GetComponent<PaintDynamic>(); mover = GetComponent<TargetMover>(); }

        public bool OnPaintHit(in PaintHit hit)
        {
            if (TaggedOut || hit.team != 0) return false;
            Hits++;
            if (Hits >= hitsToTag)
            {
                TaggedOut = true; Tags++; resetAt = Time.time + resetDelay;
                if (mover) mover.Pause(resetDelay);
            }
            return true;
        }

        void Update()
        {
            if (TaggedOut && Time.time >= resetAt) { TaggedOut = false; Hits = 0; if (paint) paint.Clear(); }
        }
    }
}
