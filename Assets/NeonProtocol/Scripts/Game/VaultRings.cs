using System.Collections.Generic;
using UnityEngine;

namespace NeonProtocol
{
    // The Core Vault lock: three rings orbit the reactor, each carrying one paint node.
    // A node locks when painted 3 times within 2 s (accuracy + timing on a moving target).
    // Lockdown (detection) resets unlocked progress. All nodes locked -> the vault relay goes online.
    public class VaultRings : MonoBehaviour
    {
        [System.Serializable] public class Ring { public Transform ring; public float speed = 30f; public VaultNode node; [HideInInspector] public bool locked; }
        public List<Ring> rings = new List<Ring>();
        public Relay vaultRelay;                 // driven by this lock (does not accept paint itself)
        public AudioSource audioSource;
        public AudioClip lockClip, resetClip, openClip;
        public Light reactorLight;

        public Bounds vaultArea = new Bounds(new Vector3(0, 3, 23), new Vector3(31, 10, 23));   // contextual hint zone

        public int Locked { get; private set; }
        public bool Complete { get; private set; }
        float spin = 1f;
        bool wasInside;

        void Start()
        {
            foreach (var r in rings) if (r.node) { r.node.owner = this; }
            if (VaultSecurity.Instance) VaultSecurity.Instance.Lockdown += ResetProgress;
            if (HeistDirector.Instance) HeistDirector.Instance.PhaseChanged += p => { if (p == HeistDirector.Phase.Infiltrate) ResetAll(); };
        }

        public void NodeLocked(VaultNode n)
        {
            foreach (var r in rings) if (r.node == n && !r.locked)
            {
                r.locked = true; Locked++;
                if (audioSource && lockClip) { audioSource.pitch = 0.9f + Locked * 0.15f; audioSource.PlayOneShot(lockClip); }
                HeistDirector.Instance?.Recorder?.Mark(SignatureRecorder.Kind.Relay, n.transform.position);
                HeistDirector.Instance?.AnnounceText($"VAULT LOCK  {Locked}/{rings.Count}");
            }
            if (!Complete && Locked >= rings.Count)
            {
                Complete = true;
                if (audioSource && openClip) { audioSource.pitch = 1f; audioSource.PlayOneShot(openClip); }
                vaultRelay?.ForceOnline();
                FindFirstObjectByType<ShoulderRig>()?.AddShake(0.6f);
            }
        }

        void ResetProgress()
        {
            if (Complete) return;
            bool any = false;
            foreach (var r in rings) if (r.locked) { r.locked = false; r.node.ResetNode(); any = true; }
            Locked = 0;
            if (any && audioSource && resetClip) audioSource.PlayOneShot(resetClip);
        }

        // Contextual guidance while the player is inside the vault and the lock is still closed.
        void Hint()
        {
            if (!HeistDirector.FullRoute) { wasInside = false; return; }
            var d = HeistDirector.Instance;
            if (d == null || !d.player || d.Current != HeistDirector.Phase.Infiltrate) { wasInside = false; return; }
            bool inside = vaultArea.Contains(d.player.transform.position);
            if (inside && !Complete) d.SetObjective($"VAULT LOCK {Locked}/{rings.Count}: HIT EACH MOVING RING NODE 3 TIMES FAST  ·  CROUCH UNDER THE SCANNERS");
            else if (wasInside && !inside && !Complete) d.SetObjective($"PAINT THE SECURITY RELAYS  {d.RelaysOnline}/{d.relays.Length}");
            wasInside = inside;
        }

        void ResetAll() { Complete = false; Locked = 0; foreach (var r in rings) { r.locked = false; if (r.node) r.node.ResetNode(); } }

        void Update()
        {
            spin = Mathf.MoveTowards(spin, Complete ? 6f : 1f, Time.deltaTime * 2f);
            foreach (var r in rings)
                if (r.ring && (!r.locked || Complete)) r.ring.Rotate(0f, r.speed * spin * Time.deltaTime, 0f, Space.World);
            Hint();
            if (reactorLight) reactorLight.intensity = (Complete ? 30f : 10f + Locked * 5f) * (0.9f + 0.1f * Mathf.Sin(Time.time * 6f));
        }
    }
}
