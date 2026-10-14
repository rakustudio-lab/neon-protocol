using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NeonProtocol
{
    // CREATE mechanic: records a bounded set of meaningful run events and encodes them into the canonical
    // Neon Signature record (algorithm v1). The same bytes are rendered by the web renderer (web/signature.js)
    // and stored on Monad, so the artwork can be rebuilt from the public record alone.
    //
    // Layout v1 (big-endian):
    //   [0] algo=1  [1] rulesVersion  [2] palette  [3..6] timeMs  [7] flags  [8] N points  [9] M milestones
    //   N x 4 bytes: x(0-255) z(0-255) (level<<4 | kind) t(0-255 fraction of run)
    //   M x 2 bytes: type t
    public class SignatureRecorder : MonoBehaviour
    {
        public enum Kind : byte { Surface = 0, Floor = 1, TargetHit = 2, Tag = 3, Relay = 4, Drone = 5, Path = 6, Ride = 7 }   // 6/7: rescue 2026-10-11 (route + lightcycle line)
        public enum Milestone : byte { RelayA = 1, RelayB = 2, RelayC = 3, CoreAcquired = 4, Extracted = 5, PlayerTagged = 6, CoreDropped = 7, Shard = 8, DroneDown = 9, FinalityStart = 10 }

        public const byte Algo = 1;
        public const int MaxPoints = 96, MaxMilestones = 24, MaxRaw = 2000;

        public Bounds mapBounds = new Bounds(Vector3.zero, new Vector3(80, 20, 80));
        public byte rulesVersion = 1;
        public byte palette = 0;

        struct Raw { public Vector3 p; public Kind k; public float t; public bool norm; }   // norm: p.x/p.z already 0..1
        readonly List<Raw> raw = new List<Raw>(256);
        readonly List<(Milestone m, float t)> milestones = new List<(Milestone, float)>();
        float startTime = -1f;
        public bool Recording => startTime >= 0f;
        public int RawCount => raw.Count;

        void OnEnable() { if (PaintProjectiles.Instance) PaintProjectiles.Instance.Impact += OnImpact; }
        void Start() { if (PaintProjectiles.Instance) { PaintProjectiles.Instance.Impact -= OnImpact; PaintProjectiles.Instance.Impact += OnImpact; } }
        void OnDisable() { if (PaintProjectiles.Instance) PaintProjectiles.Instance.Impact -= OnImpact; }

        public void Begin() { raw.Clear(); milestones.Clear(); startTime = Time.time; }
        public void Stop() { startTime = -1f; }
        float Now => startTime < 0 ? 0 : Time.time - startTime;

        void OnImpact(PaintHit h, bool counted)
        {
            if (!Recording || h.team != 0 || raw.Count >= MaxRaw) return;
            Kind k = counted ? Kind.TargetHit : Mathf.Abs(h.normal.y) > 0.7f ? Kind.Floor : Kind.Surface;
            raw.Add(new Raw { p = h.point, k = k, t = Now });
        }

        public void Mark(Kind k, Vector3 p) { if (Recording && raw.Count < MaxRaw) raw.Add(new Raw { p = p, k = k, t = Now }); }
        public void Mark(Milestone m) { if (Recording && milestones.Count < MaxMilestones) milestones.Add((m, Now)); }
        public void MarkNormalized(Kind k, float x01, float z01) { if (Recording && raw.Count < MaxRaw) raw.Add(new Raw { p = new Vector3(Mathf.Clamp01(x01), 0f, Mathf.Clamp01(z01)), k = k, t = Now, norm = true }); }

        // Route sampling: where CRYPTO actually went (Act I), and the lightcycle line (Act II, track space).
        float sampleT;
        void Update()
        {
            if (!Recording) return;
            sampleT -= Time.deltaTime; if (sampleT > 0f) return;
            var d = HeistDirector.Instance; if (!d) return;
            if (d.Current == HeistDirector.Phase.FinalityRun && FinalityRun.Instance && FinalityRun.Instance.bike)
            {
                sampleT = 0.8f; var b = FinalityRun.Instance.bike; float half = b.track ? b.track.HalfWidth(b.S) : 8f;
                MarkNormalized(Kind.Ride, b.track ? b.S / Mathf.Max(1f, b.track.Length) : 0f, (b.X + half) / (2f * half));
            }
            else if (d.player && !d.player.TaggedOut && (d.Current == HeistDirector.Phase.Infiltrate || d.Current == HeistDirector.Phase.CoreUnlocked || d.Current == HeistDirector.Phase.Pursuit))
            { sampleT = 1.2f; raw.Add(new Raw { p = d.player.transform.position, k = Kind.Path, t = Now }); }   // Act I map only (not the elevator)
        }

        /// <summary>Encode the run. Keeps every tag/relay/drone event, then fills with evenly spaced paint hits.</summary>
        public byte[] Encode(int timeMs, byte flags = 0)
        {
            float dur = Mathf.Max(0.001f, timeMs / 1000f);
            // budget: route (<=34) + ride line (<=22) + every tag/relay/drone event + paint hits to fill MaxPoints
            List<Raw> Even(List<Raw> src, int n) { var o = new List<Raw>(); if (src.Count <= n) { o.AddRange(src); return o; } for (int i = 0; i < n; i++) o.Add(src[Mathf.FloorToInt(i * (src.Count / (float)n))]); return o; }
            var keep = new List<Raw>();
            keep.AddRange(Even(raw.FindAll(r => r.k == Kind.Path), 34));
            keep.AddRange(Even(raw.FindAll(r => r.k == Kind.Ride), 22));
            foreach (var r in raw) if (r.k >= Kind.Tag && r.k <= Kind.Drone) keep.Add(r);
            if (keep.Count > MaxPoints) keep.RemoveRange(MaxPoints, keep.Count - MaxPoints);
            var rest = raw.FindAll(r => r.k < Kind.Tag);
            int slots = MaxPoints - keep.Count;
            keep.AddRange(Even(rest, Mathf.Max(0, slots)));
            keep.Sort((a, b) => a.t.CompareTo(b.t));
            var bytes = new List<byte>(10 + keep.Count * 4 + milestones.Count * 2)
            {
                Algo, rulesVersion, palette,
                (byte)(timeMs >> 24), (byte)(timeMs >> 16), (byte)(timeMs >> 8), (byte)timeMs,
                flags, (byte)keep.Count, (byte)milestones.Count
            };
            Vector3 min = mapBounds.min, size = mapBounds.size;
            bool encounter = keep.Exists(r => !r.norm && r.p.x < -200f);
            foreach (var r in keep)
            {
                float px = r.norm ? r.p.x : (r.p.x - min.x) / size.x;
                float pz = r.norm ? r.p.z : (r.p.z - min.z) / size.z;
                if (encounter && !r.norm)
                {
                    // Two honest diagram panels: encounter on the left, core/escape
                    // approach on the right. This is a run diagram, not a floor plan.
                    px = r.p.x < -200f ? 0.04f + Mathf.Clamp01((r.p.x + 310f) / 20f) * 0.40f : 0.54f + Mathf.Clamp01(px) * 0.42f;
                    if (r.p.x < -200f) pz = Mathf.Clamp01(r.p.z / 30f);
                }
                byte x = (byte)Mathf.Clamp(Mathf.RoundToInt(px * 255f), 0, 255);
                byte z = (byte)Mathf.Clamp(Mathf.RoundToInt(pz * 255f), 0, 255);
                int level = r.norm ? 0 : Mathf.Clamp(Mathf.FloorToInt((r.p.y - min.y) / size.y * 16f), 0, 15);
                bytes.Add(x); bytes.Add(z); bytes.Add((byte)((level << 4) | ((byte)r.k & 0xF)));
                bytes.Add((byte)Mathf.Clamp(Mathf.RoundToInt(r.t / dur * 255f), 0, 255));
            }
            foreach (var (m, t) in milestones) { bytes.Add((byte)m); bytes.Add((byte)Mathf.Clamp(Mathf.RoundToInt(t / dur * 255f), 0, 255)); }
            return bytes.ToArray();
        }

        public static string ToHex(byte[] b)
        {
            var sb = new StringBuilder("0x", 2 + b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }
}
