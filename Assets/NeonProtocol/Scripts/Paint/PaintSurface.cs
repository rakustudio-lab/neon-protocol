using UnityEngine;

namespace NeonProtocol
{
    // Marks static architecture as paintable. Its mesh carries paint-atlas UVs in channel 3 and island rects in
    // channel 4 (written by PaintAtlasBaker). Needs a collider so splats can find it.
    [RequireComponent(typeof(MeshRenderer))]
    public class PaintSurface : MonoBehaviour
    {
        public Renderer Renderer { get; private set; }
        public Collider[] Colliders { get; private set; }
        public int SubmeshCount { get; private set; } = 1;

        void Awake()
        {
            Renderer = GetComponent<MeshRenderer>();
            Colliders = GetComponents<Collider>();
            var mf = GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh) SubmeshCount = mf.sharedMesh.subMeshCount;
        }

        void OnEnable() => PaintSystem.Register(this);
        void OnDisable() => PaintSystem.Unregister(this);
    }
}
