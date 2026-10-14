using UnityEngine;

namespace NeonProtocol
{
    // Distance-driven footsteps (no animation events needed). Sprinting is loud (EXPOSE), crouching is silent.
    public class Footsteps : MonoBehaviour
    {
        public AudioClip[] clips;
        public float strideWalk = 1.1f, strideRun = 1.55f, strideSprint = 1.9f;
        PlayerMotor motor; AudioSource src; float dist; Vector3 last;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0.6f; src.volume = 0.5f;
            last = transform.position;
        }

        void Update()
        {
            Vector3 p = transform.position; Vector3 d = p - last; d.y = 0; last = p;
            if (!motor || !motor.Grounded || clips == null || clips.Length == 0) { dist = 0; return; }
            float speed = new Vector2(motor.Velocity.x, motor.Velocity.z).magnitude;
            if (speed < 0.6f) return;
            dist += d.magnitude;
            float stride = motor.Sprinting ? strideSprint : speed > 3f ? strideRun : strideWalk;
            if (dist < stride) return;
            dist = 0;
            if (motor.Crouched) return;
            src.pitch = Random.Range(0.92f, 1.08f);
            src.PlayOneShot(clips[Random.Range(0, clips.Length)], motor.Sprinting ? 0.9f : 0.5f);
            if (motor.Sprinting) NoiseSystem.Emit(p, 14f, motor);
            else if (speed > 3f) NoiseSystem.Emit(p, 7f, motor);
        }
    }
}
