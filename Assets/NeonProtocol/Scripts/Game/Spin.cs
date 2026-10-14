using UnityEngine;

namespace NeonProtocol
{
    // Ambient machinery motion: constant rotation around a local axis plus an optional slow vertical bob.
    // Purely visual (no colliders are moved), so it cannot disturb navigation or physics.
    public class Spin : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 10f;
        public float bobHeight = 0f;
        public float bobPeriod = 6f;
        Vector3 basePos;

        void Awake() { basePos = transform.localPosition; }

        void Update()
        {
            transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
            if (bobHeight > 0f) transform.localPosition = basePos + Vector3.up * (Mathf.Sin(Time.time * 6.2831853f / bobPeriod) * bobHeight);
        }
    }
}
