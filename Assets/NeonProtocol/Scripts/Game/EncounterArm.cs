using UnityEngine;

namespace NeonProtocol
{
    // Arms the encounter guard when the player crosses the breach line (not before: no inert visible target, no
    // shots across the hall from the spawn). The guard's own telegraph (laser + lock tone) still precedes any burst.
    public class EncounterArm : MonoBehaviour
    {
        public CompetitorAI guard;
        void OnTriggerEnter(Collider other)
        {
            if (!guard || !other.GetComponentInParent<PlayerMotor>()) return;
            var d = HeistDirector.Instance; if (!d || d.Current != HeistDirector.Phase.Infiltrate) return;
            guard.Wake();
        }
    }
}
