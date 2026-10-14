using System.Collections.Generic;
using UnityEngine;

namespace NeonProtocol
{
    // A moving paintable (targets, competitors, drones). Each renderer material slot owns a small local paint
    // texture sampled through UV0 (slots can share UV space, so they can't share a texture).
    // Materials must sample _NP_PaintLocal (NeonProtocol/Surface with Local Paint, or NeonProtocol/Character).
    public class PaintDynamic : MonoBehaviour
    {
        public int textureSize = 256;
        public Renderer[] renderers;
        public int team = -1;   // splats from this team don't stick (no painting yourself); -1 = any team paints it

        public struct Slot { public Renderer renderer; public int submesh; public RenderTexture texture; }
        public readonly List<Slot> Slots = new List<Slot>();
        public Collider[] Colliders { get; private set; }
        public float LastPaintTime { get; set; } = -100f;

        static readonly int idLocal = Shader.PropertyToID("_NP_PaintLocal");

        void Awake()
        {
            if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();
            var th = GetComponent<TagHealth>(); if (th && team < 0) team = th.team;
            Colliders = GetComponentsInChildren<Collider>();
            var mpb = new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (!r) continue;
                int subs = r.sharedMaterials.Length;
                for (int i = 0; i < subs; i++)
                {
                    var rt = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    { name = $"NP_PaintLocal_{name}_{i}", useMipMap = false, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    rt.Create();
                    Slots.Add(new Slot { renderer = r, submesh = i, texture = rt });
                    r.GetPropertyBlock(mpb, i);
                    mpb.SetTexture(idLocal, rt);
                    r.SetPropertyBlock(mpb, i);
                }
            }
            Clear();
        }

        public void Clear()
        {
            var prev = RenderTexture.active;
            foreach (var s in Slots) { RenderTexture.active = s.texture; GL.Clear(false, true, Color.clear); }
            RenderTexture.active = prev;
        }

        void OnEnable() { if (Colliders != null) PaintSystem.Register(this); }
        void Start() => PaintSystem.Register(this);
        void OnDisable() => PaintSystem.Unregister(this);
        void OnDestroy() { foreach (var s in Slots) if (s.texture) { s.texture.Release(); Destroy(s.texture); } }
    }
}
