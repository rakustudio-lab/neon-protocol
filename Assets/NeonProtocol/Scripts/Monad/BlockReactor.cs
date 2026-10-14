using TMPro;
using UnityEngine;

namespace NeonProtocol
{
    // In-world MONAD TRANSIT NETWORK installation. Shows only OBSERVED data from MonadFeed:
    // network, latest block, tx count, gas, age, observed block rate, source, live/stale/offline state.
    // Each new observed block lights the next tile of the block stream (brightness from its tx count).
    // The protocol figures (300 ms blocks / 600 ms finality) are shown as a labelled spec, never as measured latency.
    public class BlockReactor : MonoBehaviour
    {
        public TextMeshPro title, status, blockNo, details, footer;
        public Renderer[] tiles;
        public Renderer frame;
        public Light pulseLight;
        public Color liveColor = new Color(0.2f, 2.4f, 2.1f), staleColor = new Color(2.6f, 1.4f, 0.2f), offColor = new Color(1.8f, 0.2f, 0.3f);

        float[] heat; int head; float pulse; long shownBlock;
        MaterialPropertyBlock _mpb; MaterialPropertyBlock mpb => _mpb ??= new MaterialPropertyBlock();
        static readonly int idEmission = Shader.PropertyToID("_EmissionColor");

        void Start()
        {
            heat = new float[tiles != null ? tiles.Length : 0];
            if (MonadFeed.Instance) MonadFeed.Instance.NewBlock += OnBlock;
        }
        void OnDestroy() { if (MonadFeed.Instance) MonadFeed.Instance.NewBlock -= OnBlock; }

        void OnBlock(MonadFeed.Block b)
        {
            if (heat.Length == 0) return;
            head = (head + 1) % heat.Length;
            heat[head] = 0.35f + Mathf.Clamp01(b.txCount / 60f) * 0.65f;
            pulse = 1f;
        }

        void Update()
        {
            var f = MonadFeed.Instance;
            float dt = Time.deltaTime;
            pulse = Mathf.Max(0f, pulse - dt * 3f);
            for (int i = 0; i < heat.Length; i++)
            {
                heat[i] = Mathf.Max(0.04f, heat[i] - dt * 0.18f);
                if (!tiles[i]) continue;
                bool isHead = i == head && pulse > 0f;
                Color c = Color.Lerp(new Color(0.5f, 0.2f, 1.6f), liveColor, heat[i]) * (heat[i] * 1.4f + (isHead ? pulse * 2f : 0f));
                tiles[i].GetPropertyBlock(mpb); mpb.SetColor(idEmission, c); tiles[i].SetPropertyBlock(mpb);
            }
            if (f == null) { if (status) status.text = "<color=#ff5577>NO FEED</color>"; return; }

            Color sc = f.State == MonadFeed.Status.Live ? liveColor : f.State == MonadFeed.Status.Stale ? staleColor : f.State == MonadFeed.Status.Offline ? offColor : staleColor * 0.6f;
            if (frame) { frame.GetPropertyBlock(mpb); mpb.SetColor(idEmission, sc * (0.6f + pulse * 0.8f)); frame.SetPropertyBlock(mpb); }
            if (pulseLight) { { float mx = Mathf.Max(sc.r, Mathf.Max(sc.g, sc.b), 0.001f); pulseLight.color = new Color(sc.r / mx, sc.g / mx, sc.b / mx); } pulseLight.intensity = 2f + pulse * 6f; }

            string state = f.State == MonadFeed.Status.Live ? "<color=#3dffe0>● LIVE</color>" : f.State == MonadFeed.Status.Stale ? "<color=#ffb347>● STALE</color>"
                         : f.State == MonadFeed.Status.Offline ? "<color=#ff5577>● OFFLINE · FEED PAUSED</color>" : "<color=#bbbbff>● CONNECTING</color>";
            if (status) status.text = $"{f.networkName} · CHAIN {f.chainId} · OBSERVED   {state}";
            var b = f.Latest;
            if (blockNo && b.number != shownBlock) { shownBlock = b.number; blockNo.text = b.number > 0 ? "#" + b.number.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : "—"; }
            if (details)
            {
                float age = f.AgeSeconds;
                string ageS = age < 0 ? "—" : age < 1.5f ? "<1 s" : $"{age:0} s";
                string rate = f.BlocksPerSecond > 0f ? $"{f.BlocksPerSecond.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} blocks/s observed" : "measuring…";
                string h = b.hash != null && b.hash.Length > 14 ? b.hash.Substring(0, 10) + "…" + b.hash.Substring(b.hash.Length - 4) : "—";
                details.text = $"LATEST BLOCK  {b.txCount} tx · {(b.gasUsed / 1e6).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}M gas · age {ageS}\n{h}\n{rate}";
            }
            if (footer) footer.text = $"source: {f.SourceLabel} (public RPC, polled 1/s)\nprotocol spec: ~300 ms blocks · ~600 ms finality (docs.monad.xyz) — not this display's latency";
        }
    }
}
