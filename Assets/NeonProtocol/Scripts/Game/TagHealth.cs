using UnityEngine;

namespace NeonProtocol
{
    // TAG mechanic: N qualifying paint hits inside a short window tag a competitor out (nonlethal).
    // Hits fade after a quiet period, so landing a fast, accurate burst matters.
    public class TagHealth : MonoBehaviour, IPaintHittable
    {
        public int team = 1;                 // the team this body belongs to (hits from the other team count)
        public int hitsToTag = 3;
        public float hitMemory = 4f;          // seconds before accumulated hits fade
        public float downTime = 5f;

        public int Hits { get; private set; }
        public bool TaggedOut { get; private set; }
        public float LastHitTime { get; private set; } = -100f;
        public Vector3 LastHitDirection { get; private set; }
        public event System.Action<PaintHit> Hit;
        public event System.Action Tagged, Recovered;

        float downUntil;
        public float InvulnerableUntil { get; set; }

        public bool OnPaintHit(in PaintHit hit)
        {
            if (TaggedOut || hit.team == team || Time.time < InvulnerableUntil) return false;
            if (Time.time - LastHitTime > hitMemory) Hits = 0;
            Hits++; LastHitTime = Time.time; LastHitDirection = hit.direction;
            Hit?.Invoke(hit);
            if (Hits >= hitsToTag)
            {
                TaggedOut = true; Hits = 0; downUntil = Time.time + downTime;
                Tagged?.Invoke();
            }
            return true;
        }

        public void Revive() { TaggedOut = false; Hits = 0; Recovered?.Invoke(); }

        void Update()
        {
            if (TaggedOut && Time.time >= downUntil) Revive();
            if (!TaggedOut && Hits > 0 && Time.time - LastHitTime > hitMemory) Hits = 0;
        }
    }
}
