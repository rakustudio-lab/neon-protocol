using UnityEngine;

namespace NeonProtocol
{
    // Forwards a child trigger volume to its gameplay owner.
    public class TriggerRelay : MonoBehaviour
    {
        public ExtractionGate target;
        void OnTriggerEnter(Collider other) { if (target) target.HandleTrigger(other); }
    }
}
