using UnityEngine;

namespace NeonProtocol
{
    // EXPOSE mechanic: shots, impacts, sprinting and landings make noise that AI can investigate.
    public static class NoiseSystem
    {
        public struct Noise { public Vector3 position; public float radius; public Component source; public float time; }
        public static event System.Action<Noise> Heard;

        public static void Emit(Vector3 position, float radius, Component source)
        {
            Heard?.Invoke(new Noise { position = position, radius = radius, source = source, time = Time.time });
        }
    }
}
