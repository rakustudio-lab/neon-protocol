using System.Collections.Generic;
using UnityEngine;

namespace NeonProtocol
{
    public interface IPaintHittable
    {
        // Called by a paintball that hit this object. Returns true if the hit counted (for hit markers).
        bool OnPaintHit(in PaintHit hit);
    }

    public struct PaintHit
    {
        public Vector3 point, normal, direction;
        public int team;
        public Component shooter;
        public Collider collider;
    }

    // Pooled paintballs + droplets. Simulated on the CPU (cheap raycasts), drawn with GPU instancing.
    // Hard caps on everything: nothing accumulates.
    [DefaultExecutionOrder(-20)]
    public class PaintProjectiles : MonoBehaviour
    {
        public static PaintProjectiles Instance { get; private set; }

        public LayerMask hitMask = ~0;
        public float gravity = 6f;
        public float splatRadius = 0.34f;
        public int maxBalls = 96, maxDrops = 256;

        public event System.Action<PaintHit, bool> Impact;   // (hit, countedOnTarget)

        struct Ball { public Vector3 pos, vel; public float life; public int team; public Component shooter; public int ignoreLayer; }
        struct Drop { public Vector3 pos, vel; public float life, radius; public int team; }

        readonly List<Ball> balls = new List<Ball>(128);
        readonly List<Drop> drops = new List<Drop>(256);
        readonly RaycastHit[] hits = new RaycastHit[8];

        void Awake() => Instance = this;
        void OnEnable() => Instance = this;

        public void Fire(Vector3 origin, Vector3 velocity, int team, Component shooter, int ignoreLayer)
        {
            if (balls.Count >= maxBalls) balls.RemoveAt(0);
            balls.Add(new Ball { pos = origin, vel = velocity, life = 2.5f, team = team, shooter = shooter, ignoreLayer = ignoreLayer });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = balls.Count - 1; i >= 0; i--)
            {
                var b = balls[i];
                Vector3 next = b.pos + b.vel * dt + 0.5f * gravity * dt * dt * Vector3.down;
                Vector3 seg = next - b.pos;
                float len = seg.magnitude;
                int mask = hitMask & ~(1 << b.ignoreLayer);
                if (len > 1e-5f && Physics.Raycast(b.pos, seg / len, out var hit, len, mask, QueryTriggerInteraction.Collide))
                {
                    var own = b.shooter ? b.shooter.GetComponentInParent<TagHealth>() : null;
                    if (hit.collider.isTrigger && hit.collider.GetComponentInParent<IPaintHittable>() == null)
                    {
                        // pass through decorative triggers
                    }
                    else if (own && hit.collider.transform.IsChildOf(own.transform))
                    {
                        // never paint yourself (rival muzzles start close to their own capsule)
                    }
                    else { OnBallHit(b, hit, seg / len); balls.RemoveAt(i); continue; }
                }
                b.vel += Vector3.down * gravity * dt;
                b.pos = next;
                b.life -= dt;
                if (b.life <= 0f) { balls.RemoveAt(i); continue; }
                balls[i] = b;
            }

            for (int i = drops.Count - 1; i >= 0; i--)
            {
                var d = drops[i];
                Vector3 next = d.pos + d.vel * dt;
                Vector3 seg = next - d.pos; float len = seg.magnitude;
                if (len > 1e-5f && Physics.Raycast(d.pos, seg / len, out var hit, len, hitMask, QueryTriggerInteraction.Ignore))
                {
                    if (PaintSystem.Instance) PaintSystem.Instance.Splat(hit.point, hit.normal, seg / len, d.radius, d.team, 0.6f, false, 0.06f);
                    drops.RemoveAt(i); continue;
                }
                d.vel += Vector3.down * 14f * dt;
                d.pos = next; d.life -= dt;
                if (d.life <= 0f) { drops.RemoveAt(i); continue; }
                drops[i] = d;
            }

            Render();
        }

        void OnBallHit(in Ball b, in RaycastHit hit, Vector3 dir)
        {
            var info = new PaintHit { point = hit.point, normal = hit.normal, direction = dir, team = b.team, shooter = b.shooter, collider = hit.collider };
            bool counted = false;
            var target = hit.collider.GetComponentInParent<IPaintHittable>();
            if (target != null) counted = target.OnPaintHit(info);

            float oblique = 1f - Mathf.Abs(Vector3.Dot(dir, hit.normal));
            if (PaintSystem.Instance)
                PaintSystem.Instance.Splat(hit.point, hit.normal, dir, splatRadius * Random.Range(0.85f, 1.15f), b.team, Mathf.Clamp01(oblique * 1.3f));

            // Satellite droplets thrown off the crown; they land a beat later as small splats.
            if (QualityPresets.PaintDroplets)
            {
                int n = Random.Range(5, 9);
                Vector3 refl = Vector3.Reflect(dir, hit.normal);
                for (int k = 0; k < n && drops.Count < maxDrops; k++)
                {
                    Vector3 v = (hit.normal * Random.Range(0.6f, 1.4f) + refl * Random.Range(0.2f, 0.8f) + Random.insideUnitSphere * 0.9f).normalized * Random.Range(2.2f, 5.5f);
                    drops.Add(new Drop { pos = hit.point + hit.normal * 0.04f, vel = v, life = 1.2f, radius = Random.Range(0.045f, 0.1f), team = b.team });
                }
            }

            ImpactFX.Instance?.Play(hit.point, hit.normal, b.team, counted);
            NoiseSystem.Emit(hit.point, 9f, b.shooter);
            Impact?.Invoke(info, counted);
        }

        // One dynamic mesh for every ball and droplet (stretched octahedra, team colour in vertex colours): one draw call.
        public Material ballMaterial;   // NeonProtocol/Ball
        Mesh dyn; readonly List<Vector3> dv = new List<Vector3>(4096); readonly List<Color32> dc = new List<Color32>(4096); readonly List<int> di = new List<int>(8192);
        static readonly Vector3[] Oct = { new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1) };
        static readonly int[] OctTri = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };

        void AddShape(Vector3 pos, Quaternion rot, Vector3 scale, Color32 c)
        {
            int b = dv.Count;
            for (int k = 0; k < 6; k++) { dv.Add(pos + rot * Vector3.Scale(Oct[k], scale)); dc.Add(c); }
            for (int k = 0; k < OctTri.Length; k++) di.Add(b + OctTri[k]);
        }

        void Render()
        {
            if (!ballMaterial) return;
            dv.Clear(); dc.Clear(); di.Clear();
            float dt = Mathf.Max(Time.deltaTime, 1f / 240f);
            Color32 ca = TeamColor(0), cb = TeamColor(1);
            foreach (var b in balls)
            {
                float speed = b.vel.magnitude;
                var rot = speed > 0.01f ? Quaternion.LookRotation(b.vel / speed) : Quaternion.identity;
                float stretch = Mathf.Clamp(speed * dt * 0.45f, 0.05f, 0.45f);   // motion-stretched glowing slug
                AddShape(b.pos - b.vel * dt * 0.45f, rot, new Vector3(0.045f, 0.045f, stretch), b.team == 0 ? ca : cb);
            }
            foreach (var d in drops)
            {
                float s = d.radius * 0.35f;
                AddShape(d.pos, Quaternion.LookRotation(d.vel.sqrMagnitude > 0.01f ? d.vel : Vector3.down), new Vector3(s, s, s * 2.2f), d.team == 0 ? ca : cb);
            }
            if (!dyn) { dyn = new Mesh { name = "NP_Paintballs" }; dyn.MarkDynamic(); }
            dyn.Clear();
            if (dv.Count == 0) return;
            dyn.SetVertices(dv); dyn.SetColors(dc); dyn.SetTriangles(di, 0, false);
            dyn.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            Graphics.RenderMesh(new RenderParams(ballMaterial) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false }, dyn, 0, Matrix4x4.identity);
        }

        static Color32 TeamColor(int team)
        {
            var c = ImpactFX.TeamColorLdr(team);
            return new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 255);
        }

        public int ActiveBalls => balls.Count;
        public int ActiveDrops => drops.Count;
    }
}
