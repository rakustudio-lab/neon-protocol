using UnityEngine;

namespace NeonProtocol
{
    // Player side of TAG: hit feedback (camera, sound, direction for the HUD), tag-out, checkpoint respawn.
    [RequireComponent(typeof(TagHealth))]
    public class PlayerTagController : MonoBehaviour
    {
        public AudioClip hitClip, tagClip;
        public float respawnDelay = 3.5f;

        TagHealth health; PlayerMotor motor; ShoulderRig rig; AudioSource src; CharacterAnimator anim; PaintRifle rifle;
        public float LastHitTime => health ? health.LastHitTime : -100f;
        public Vector3 LastHitDirection => health ? health.LastHitDirection : Vector3.zero;
        public int Hits => health ? health.Hits : 0;
        public int HitsToTag => health ? health.hitsToTag : 3;

        void Awake()
        {
            health = GetComponent<TagHealth>(); motor = GetComponent<PlayerMotor>();
            rig = motor ? motor.rig : null; anim = GetComponentInChildren<CharacterAnimator>(); rifle = GetComponentInChildren<PaintRifle>(true);
            src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f;
            health.team = 0; health.downTime = respawnDelay + 0.5f; health.hitsToTag = 4; health.hitMemory = 3f;
            health.Hit += OnHit; health.Tagged += OnTagged;
        }

        void OnHit(PaintHit h)
        {
            if (rig) rig.AddShake(0.35f);
            if (anim) anim.PlayHit();
            if (src && hitClip) src.PlayOneShot(hitClip, 0.8f);
        }

        void OnTagged()
        {
            if (src && tagClip) src.PlayOneShot(tagClip, 1f);
            motor.InputLocked = true; motor.TaggedOut = true;
            if (rifle) rifle.CancelReload();   // an unseated canister gives nothing
            HeistDirector.Instance?.OnPlayerTagged(transform.position);
            // fair play: hunters lose track after a tag-out (no spawn camping)
            foreach (var ai in CompetitorAIRegistry.All) if (ai) ai.ForgetPlayer();
            foreach (var dr in CompetitorAIRegistry.Drones) if (dr) dr.ForgetPlayer();
            Invoke(nameof(Respawn), respawnDelay);
        }

        void Respawn()
        {
            var p = HeistDirector.Instance ? HeistDirector.Instance.RespawnPoint() : null;
            if (p) motor.Teleport(p.position, p.eulerAngles.y);
            var pd = GetComponentInChildren<PaintDynamic>(); if (pd) pd.Clear();
            if (rifle) rifle.Refill();
            motor.InputLocked = false; motor.TaggedOut = false;
            health.Revive();
            health.InvulnerableUntil = Time.time + 3f;   // spawn protection
        }
    }
}
