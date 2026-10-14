using System;
using UnityEngine;

namespace NeonProtocol
{
    // "What other people create becomes part of the world you play in."
    // The page renders the latest registered Neon Signature (from Monad, via the relay) and hands it over as PNG.
    // One bounded texture, loaded asynchronously by the page; the game never blocks on it.
    public class ArtWall : MonoBehaviour
    {
        public Renderer panel;
        public Light glow;
        public string Label { get; private set; } = "";
        public int TraceId { get; private set; }

        Texture2D tex;
        static readonly int idBase = Shader.PropertyToID("_BaseMap"), idColor = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock _mpb;
        MaterialPropertyBlock mpb => _mpb ??= new MaterialPropertyBlock();

        [Serializable] class Payload { public int id; public string name; public string label; public string png; }


        // Called by GameBoot.SetFeaturedArt (page -> Unity).
        public void Apply(string json)
        {
            var p = JsonUtility.FromJson<Payload>(json);
            if (p == null || string.IsNullOrEmpty(p.png)) return;
            var b64 = p.png.Contains(",") ? p.png.Substring(p.png.IndexOf(',') + 1) : p.png;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(b64); } catch { return; }
            if (bytes.Length > 3_000_000) return;   // bounded
            if (!tex) tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "FeaturedTrace", wrapMode = TextureWrapMode.Clamp };
            if (!tex.LoadImage(bytes, true)) return;
            TraceId = p.id; Label = p.label;
            panel.GetPropertyBlock(mpb);
            mpb.SetTexture(idBase, tex);
            mpb.SetColor(idColor, new Color(1.6f, 1.6f, 1.6f, 1f));
            panel.SetPropertyBlock(mpb);
            if (glow) glow.enabled = true;
        }
    }
}
