using UnityEngine;

namespace NeonProtocol
{
    // QA-only paint target: counts player hits (zero-spread aim/muzzle agreement test).
    public class QAHitCounter : MonoBehaviour, IPaintHittable
    {
        public int hits;
        public bool OnPaintHit(in PaintHit hit) { if (hit.team == 0) hits++; return true; }
    }
}
