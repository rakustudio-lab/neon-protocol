using UnityEngine;

namespace NeonProtocol
{
    // Smooth back-and-forth motion for test targets.
    public class TargetMover : MonoBehaviour
    {
        public Vector3 travel = new Vector3(6, 0, 0);
        public float speed = 0.3f;
        Vector3 origin; float phase, pausedUntil;

        void Start() => origin = transform.position;
        public void Pause(float seconds) => pausedUntil = Time.time + seconds;

        void Update()
        {
            if (Time.time < pausedUntil) return;
            phase += Time.deltaTime * speed * Mathf.PI;
            transform.position = origin + travel * (0.5f * Mathf.Sin(phase));
        }
    }
}
