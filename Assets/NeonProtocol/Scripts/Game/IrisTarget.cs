using UnityEngine;

namespace NeonProtocol
{
    // One paintable element of the Iris relay (pads 0..2, core = 3). Forwards hits to its IrisRelay through the
    // standard IPaintHittable path so aim, splats and hit feedback stay identical to every other target.
    public class IrisTarget : MonoBehaviour, IPaintHittable
    {
        public IrisRelay owner;
        public int index;   // 0,1,2 = pads in order, 3 = core

        public bool OnPaintHit(in PaintHit hit) => owner && owner.Hit(index, hit);
    }
}
