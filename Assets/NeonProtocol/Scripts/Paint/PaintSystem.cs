using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonProtocol
{
    // Texture-space paint.
    //  * Static architecture: every PaintSurface owns islands in ONE global atlas (mesh UV channel 3, baked by
    //    PaintAtlasBaker). A splat is drawn by rasterizing the nearby meshes at their paint UVs, so it wraps corners.
    //  * Dynamic objects (targets, competitors) own a small local paint texture (UV0) via PaintDynamic.
    //  * Visual paint only. Gameplay events (hits, tags, relays, signature) are recorded separately.
    // Memory is fixed: one atlas (16 MB at 2048, 64 MB at 4096) + a few 256-512 px local textures. No accumulation.
    [ExecuteAlways]
    [DefaultExecutionOrder(-80)]
    public class PaintSystem : MonoBehaviour
    {
        public static PaintSystem Instance { get; private set; }

        public Shader splatShader, dryShader;
        [ColorUsage(false, true)] public Color colorA = new Color(0.1f, 2.6f, 2.2f);   // player: neon cyan-mint
        [ColorUsage(false, true)] public Color colorB = new Color(2.8f, 0.25f, 2.1f);  // rivals: hot magenta
        public float glow = 1.0f;
        public float dryTime = 7f;
        [Tooltip("Seconds for untouched paint coverage to reach its visible edge threshold. Visual only.")]
        [Min(1f)] public float fadeSeconds = 8f;
        public LayerMask paintableMask = ~0;
        public int maxActiveSplats = 48;

        public RenderTexture Atlas { get; private set; }
        public int SplatsThisSession { get; private set; }

        struct Active
        {
            public Vector3 pos, normal, dir; public float radius, seed, team, tone, start, grow, drip, stretch;
            public PaintSurface[] surfaces; public PaintDynamic[] dynamics;
        }

        readonly List<Active> active = new List<Active>(64);
        readonly Collider[] overlap = new Collider[24];
        readonly List<PaintSurface> tmpS = new List<PaintSurface>(8);
        readonly List<PaintDynamic> tmpD = new List<PaintDynamic>(4);
        static readonly Dictionary<Collider, PaintSurface> surfaceByCollider = new Dictionary<Collider, PaintSurface>();
        static readonly Dictionary<Collider, PaintDynamic> dynamicByCollider = new Dictionary<Collider, PaintDynamic>();

        Material splatMat, dryMat;
        CommandBuffer cb;
        Mesh fullscreenTri;
        float lastSplatTime = -100f, dryAccum, fadeAccum;
        readonly HashSet<PaintDynamic> visitedDynamic = new HashSet<PaintDynamic>();

        static readonly int idAtlas = Shader.PropertyToID("_NP_PaintAtlas");
        static readonly int idColA = Shader.PropertyToID("_NP_PaintColorA");
        static readonly int idColB = Shader.PropertyToID("_NP_PaintColorB");
        static readonly int idGlow = Shader.PropertyToID("_NP_PaintGlow");
        static readonly int idPos = Shader.PropertyToID("_SplatPos");
        static readonly int idNrm = Shader.PropertyToID("_SplatNormal");
        static readonly int idDir = Shader.PropertyToID("_SplatDir");
        static readonly int idPar = Shader.PropertyToID("_SplatParams");
        static readonly int idDry = Shader.PropertyToID("_DryAmount");
        static readonly int idFade = Shader.PropertyToID("_FadeFactor");

        public static void Register(PaintSurface s) { foreach (var c in s.Colliders) if (c) surfaceByCollider[c] = s; }
        public static void Unregister(PaintSurface s) { foreach (var c in s.Colliders) if (c) surfaceByCollider.Remove(c); }
        public static void Register(PaintDynamic d) { foreach (var c in d.Colliders) if (c) dynamicByCollider[c] = d; }
        public static void Unregister(PaintDynamic d) { foreach (var c in d.Colliders) if (c) dynamicByCollider.Remove(c); }

        void OnEnable()
        {
            Instance = this;
            PushGlobals();
            if (!Application.isPlaying) { Shader.SetGlobalTexture(idAtlas, Texture2D.blackTexture); return; }
            CreateAtlas(QualityPresets.PaintAtlasSize);
            QualityPresets.Changed += OnQualityChanged;
        }

        void OnDisable()
        {
            QualityPresets.Changed -= OnQualityChanged;
            if (Instance == this) Instance = null;
            if (Atlas) { Atlas.Release(); Destroy(Atlas); Atlas = null; }
            Shader.SetGlobalTexture(idAtlas, Texture2D.blackTexture);
            cb?.Release(); cb = null;
        }

        void OnValidate() => PushGlobals();

        void PushGlobals()
        {
            Shader.SetGlobalColor(idColA, colorA);
            Shader.SetGlobalColor(idColB, colorB);
            Shader.SetGlobalFloat(idGlow, glow);
        }

        void OnQualityChanged()
        {
            // Changing the atlas size clears existing paint; only do it between challenges.
            if (Atlas && Atlas.width != QualityPresets.PaintAtlasSize && active.Count == 0) CreateAtlas(QualityPresets.PaintAtlasSize);
        }

        void CreateAtlas(int size)
        {
            if (Atlas) { Atlas.Release(); Destroy(Atlas); }
            Atlas = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "NP_PaintAtlas", useMipMap = false, autoGenerateMips = false,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0
            };
            Atlas.Create();
            ClearAll();
            Shader.SetGlobalTexture(idAtlas, Atlas);
            if (!splatMat) splatMat = new Material(splatShader) { hideFlags = HideFlags.HideAndDontSave };
            if (!dryMat) dryMat = new Material(dryShader) { hideFlags = HideFlags.HideAndDontSave };
            cb ??= new CommandBuffer { name = "NeonPaint" };
            if (!fullscreenTri)
            {
                fullscreenTri = new Mesh { name = "NP_FullscreenTri" };
                fullscreenTri.SetVertices(new[] { new Vector3(-1, -1, 0), new Vector3(-1, 3, 0), new Vector3(3, -1, 0) });
                fullscreenTri.SetTriangles(new[] { 0, 1, 2 }, 0);
                fullscreenTri.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            }
        }

        public void ClearAll()
        {
            active.Clear();
            dryAccum = fadeAccum = 0f;
            if (Atlas)
            {
                var prev = RenderTexture.active;
                RenderTexture.active = Atlas; GL.Clear(false, true, Color.clear); RenderTexture.active = prev;
            }
            foreach (var d in dynamicByCollider.Values) d.Clear();
            SplatsThisSession = 0;
        }

        /// <summary>Queue a splat. Returns false if nothing paintable was in reach.</summary>
        public bool Splat(Vector3 pos, Vector3 normal, Vector3 dir, float radius, int team, float stretch = 0.4f, bool drips = true, float growTime = 0.11f)
        {
            if (!Atlas) return false;
            tmpS.Clear(); tmpD.Clear();
            int n = Physics.OverlapSphereNonAlloc(pos, radius * 2.6f, overlap, paintableMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = overlap[i];
                if (surfaceByCollider.TryGetValue(c, out var s) && !tmpS.Contains(s)) tmpS.Add(s);
                else if (dynamicByCollider.TryGetValue(c, out var d) && !tmpD.Contains(d) && d.team != team) tmpD.Add(d);
            }
            if (tmpS.Count == 0 && tmpD.Count == 0) return false;

            if (active.Count >= maxActiveSplats) FinishOldest();
            float seed = Random.value;
            active.Add(new Active
            {
                pos = pos, normal = normal.normalized, dir = dir.normalized, radius = radius, seed = seed,
                team = team, tone = Random.Range(0.25f, 1f), start = Time.time, grow = Mathf.Max(0.01f, growTime),
                drip = drips && Mathf.Abs(normal.y) < 0.6f ? Random.Range(1.2f, 2.6f) : 0f, stretch = stretch,
                surfaces = tmpS.ToArray(), dynamics = tmpD.ToArray()
            });
            lastSplatTime = Time.time;
            SplatsThisSession++;
            return true;
        }

        void FinishOldest()
        {
            var a = active[0];
            cb.Clear();
            Draw(a, 1f, a.drip > 0 ? 1f : 0f);
            Graphics.ExecuteCommandBuffer(cb);
            active.RemoveAt(0);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || !Atlas) return;
            cb.Clear();
            float now = Time.time;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var a = active[i];
                float t = now - a.start;
                float g = Mathf.Clamp01(t / a.grow);
                g = 1f - (1f - g) * (1f - g) * (1f - g);   // strong ease-out: floods out, then settles
                float dr = a.drip > 0 ? Mathf.Clamp01((t - 0.08f) / a.drip) : 0f;
                dr = 1f - (1f - dr) * (1f - dr);
                Draw(a, g, dr);
                if (g >= 1f && (a.drip <= 0f || dr >= 1f)) active.RemoveAt(i);
            }

            // Drying: a cheap channel-masked subtract, only while something is wet.
            dryAccum += Time.deltaTime;
            if (now - lastSplatTime < dryTime + 1f && dryAccum >= 0.1f)
            {
                cb.SetRenderTarget(Atlas);
                cb.SetGlobalFloat(idDry, dryAccum / dryTime);
                cb.DrawMesh(fullscreenTri, Matrix4x4.identity, dryMat, 0, 0);
                visitedDynamic.Clear();
                foreach (var d in dynamicByCollider.Values)
                    if (d && visitedDynamic.Add(d) && now - d.LastPaintTime < dryTime + 1f)
                        foreach (var sl in d.Slots) { cb.SetRenderTarget(sl.texture); cb.DrawMesh(fullscreenTri, Matrix4x4.identity, dryMat, 0, 0); }
                dryAccum = 0f;
            }
            else if (dryAccum > 0.1f) dryAccum = 0.1f;

            // Independent visual lifetime. Scale premultiplied RGBA together to preserve team/tone.
            // The event recorder and relay progress are deliberately unaffected.
            fadeAccum += Time.deltaTime;
            float lifetime = Mathf.Max(1f, fadeSeconds);
            if (now - lastSplatTime < lifetime * 8f + 6f && fadeAccum >= 0.1f)
            {
                cb.SetGlobalFloat(idFade, Mathf.Pow(0.4f, fadeAccum / lifetime));
                cb.SetRenderTarget(Atlas);
                cb.DrawMesh(fullscreenTri, Matrix4x4.identity, dryMat, 0, 1);
                visitedDynamic.Clear();
                foreach (var d in dynamicByCollider.Values)
                    if (d && visitedDynamic.Add(d) && now - d.LastPaintTime < lifetime * 8f + 6f)
                        foreach (var sl in d.Slots)
                        {
                            cb.SetRenderTarget(sl.texture);
                            cb.DrawMesh(fullscreenTri, Matrix4x4.identity, dryMat, 0, 1);
                        }
                fadeAccum = 0f;
            }
            else if (fadeAccum > 0.1f) fadeAccum = 0.1f;

            Graphics.ExecuteCommandBuffer(cb);
        }

        void Draw(in Active a, float grow, float drip)
        {
            cb.SetGlobalVector(idPos, new Vector4(a.pos.x, a.pos.y, a.pos.z, a.radius));
            cb.SetGlobalVector(idNrm, new Vector4(a.normal.x, a.normal.y, a.normal.z, grow));
            cb.SetGlobalVector(idDir, new Vector4(a.dir.x, a.dir.y, a.dir.z, a.stretch));
            cb.SetGlobalVector(idPar, new Vector4(a.seed, a.team, a.tone, drip));
            if (a.surfaces.Length > 0)
            {
                cb.SetRenderTarget(Atlas);
                foreach (var s in a.surfaces)
                    if (s && s.Renderer) for (int m = 0; m < s.SubmeshCount; m++) cb.DrawRenderer(s.Renderer, splatMat, m, 0);
            }
            foreach (var d in a.dynamics)
            {
                if (!d) continue;
                foreach (var sl in d.Slots)
                {
                    if (!sl.renderer || !sl.texture) continue;
                    cb.SetRenderTarget(sl.texture);
                    cb.DrawRenderer(sl.renderer, splatMat, sl.submesh, 1);
                }
                d.LastPaintTime = Time.time;
            }
        }
    }
}
