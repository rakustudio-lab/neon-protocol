using UnityEngine;

namespace NeonProtocol
{
    // Finality Run light gates flash once per REAL observed Monad block (MonadFeed). Pure visualisation: it never
    // touches steering, timing or score. With no feed (offline) the gates just glow steadily.
    public class GatePulse : MonoBehaviour
    {
        public Renderer[] gates;
        public Color baseColor = new Color(0.2f, 2.2f, 2.6f);
        public Color pulseColor = new Color(2.2f, 0.7f, 3.6f);
        float pulse;
        static readonly int idBase = Shader.PropertyToID("_BaseColor");
        MaterialPropertyBlock mpb;

        void OnEnable() { if (MonadFeed.Instance) MonadFeed.Instance.NewBlock += OnBlock; }
        void OnDisable() { if (MonadFeed.Instance) MonadFeed.Instance.NewBlock -= OnBlock; }
        void OnBlock(MonadFeed.Block b) { pulse = 1f; }

        void Update()
        {
            pulse = Mathf.Max(0f, pulse - Time.deltaTime * 2.2f);
            mpb ??= new MaterialPropertyBlock();
            var c = Color.Lerp(baseColor, pulseColor, pulse);
            foreach (var g in gates) { if (!g) continue; g.GetPropertyBlock(mpb); mpb.SetColor(idBase, c); g.SetPropertyBlock(mpb); }
        }
    }
}
