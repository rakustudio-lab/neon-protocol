using UnityEngine;

namespace NeonProtocol
{
    // The energy Core: sealed in a cage until the relays are online, then carried on the player's back.
    // While carried it pulses (noise) so competitors converge: pursuit pressure without cheating.
    public class CoreObjective : MonoBehaviour
    {
        public Transform coreVisual;        // floating core mesh
        public Transform cage;              // lowers into the pedestal on unlock
        public Light coreLight;
        public float pickupRadius = 3.1f;   // horizontal reach: the Core floats over a 4.2 m pedestal
        public float pulseInterval = 3f;
        public float pulseRadius = 24f;
        public AudioSource audioSource;
        public AudioClip unlockClip, pickupClip, dropClip;

        public bool Unlocked { get; private set; }
        public bool Carried { get; private set; }
        public event System.Action PickedUp, Dropped;

        Vector3 homePos, cageHome, homeScale; Quaternion homeRot;
        Transform carrier;
        float pulseT, cageT, returnAt = -1f;

        void Awake()
        {
            homePos = coreVisual.position; homeRot = coreVisual.rotation; homeScale = coreVisual.localScale;
            if (cage) cageHome = cage.localPosition;
        }

        public void ResetCore()
        {
            Unlocked = false; Carried = false; carrier = null; returnAt = -1f; cageT = 0f;
            coreVisual.SetParent(transform, true); coreVisual.SetPositionAndRotation(homePos, homeRot); coreVisual.localScale = homeScale;
            if (cage) cage.localPosition = cageHome;
        }

        public void Unlock()
        {
            Unlocked = true;
            if (audioSource && unlockClip) audioSource.PlayOneShot(unlockClip);
        }

        public void Drop(Vector3 at)
        {
            if (!Carried) return;
            Carried = false; carrier = null;
            coreVisual.SetParent(transform, true);
            coreVisual.localScale = homeScale;
            if (Physics.Raycast(at + Vector3.up, Vector3.down, out var hit, 6f, ~(1 << 8), QueryTriggerInteraction.Ignore)) at = hit.point;
            coreVisual.position = at + Vector3.up * 0.9f;
            returnAt = Time.time + 20f;   // unattended core returns to the vault
            if (audioSource && dropClip) audioSource.PlayOneShot(dropClip);
            Dropped?.Invoke();
        }

        void Update()
        {
            var director = HeistDirector.Instance;
            var player = director ? director.player : null;
            if (cage) { cageT = Mathf.MoveTowards(cageT, Unlocked ? 1f : 0f, Time.deltaTime * 0.6f); cage.localPosition = cageHome + Vector3.down * (cageT * 3.2f); }

            if (!Carried)
            {
                coreVisual.Rotate(0f, 60f * Time.deltaTime, 0f, Space.World);
                Vector3 dp = player ? coreVisual.position - player.transform.position : Vector3.one * 999f;
                bool inReach = player && new Vector2(dp.x, dp.z).sqrMagnitude < pickupRadius * pickupRadius && dp.y < 3.2f && dp.y > -1.5f;
                if (Unlocked && player && !player.TaggedOut && inReach)
                {
                    Carried = true; carrier = player.transform; returnAt = -1f;
                    coreVisual.SetParent(carrier, false);
                    coreVisual.localPosition = new Vector3(0f, 1.22f, -0.3f);    // upper back, small: never hides the head/crosshair
                    coreVisual.localRotation = Quaternion.Euler(45f, 0f, 45f);
                    coreVisual.localScale = homeScale * 0.42f;
                    if (audioSource && pickupClip) audioSource.PlayOneShot(pickupClip);
                    PickedUp?.Invoke();
                }
                if (returnAt > 0f && Time.time > returnAt) { returnAt = -1f; coreVisual.SetPositionAndRotation(homePos, homeRot); }
            }
            else
            {
                coreVisual.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
                pulseT += Time.deltaTime;
                if (pulseT >= pulseInterval) { pulseT = 0f; NoiseSystem.Emit(carrier.position, pulseRadius, this); }
            }
            if (coreLight) coreLight.intensity = (Carried ? 3.5f : 6f) * (0.85f + 0.15f * Mathf.Sin(Time.time * 7f));
        }
    }
}
