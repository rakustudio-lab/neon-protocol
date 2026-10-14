using System.Collections.Generic;
using UnityEngine;

namespace NeonProtocol
{
    // Editable architectural block (box with chamfered edges, staircase, or ramp).
    // Change the size in the Inspector and the mesh regenerates; layout stays hand-authored.
    // Every face is its own unshared polygon so PaintAtlasBaker can give each face an island in the paint atlas.
    [ExecuteAlways, DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class NeonBlock : MonoBehaviour
    {
        public enum Shape { Box, Stairs, Ramp }
        public Shape shape = Shape.Box;
        public Vector3 size = new Vector3(2, 2, 2);
        [Range(0f, 0.25f)] public float chamfer = 0.04f;
        [Tooltip("Stairs: number of steps")] public int steps = 10;
        public bool paintable = true;
        public bool addCollider = true;

        [SerializeField, HideInInspector] Vector3 builtSize;
        [SerializeField, HideInInspector] Shape builtShape;
        [SerializeField, HideInInspector] float builtChamfer;
        [SerializeField, HideInInspector] int builtSteps;

        public bool NeedsRebuild => builtSize != size || builtShape != shape || !Mathf.Approximately(builtChamfer, chamfer) || builtSteps != steps || GetComponent<MeshFilter>().sharedMesh == null;

        public void MarkBuilt() { builtSize = size; builtShape = shape; builtChamfer = chamfer; builtSteps = steps; }

        // --- mesh generation (also used at runtime for tests) ---
        public Mesh Generate()
        {
            var b = new MeshBuilder();
            Vector3 h = size * 0.5f;
            switch (shape)
            {
                case Shape.Box: BuildBox(b, Vector3.zero, h, Mathf.Min(chamfer, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.45f)); break;
                case Shape.Stairs: BuildStairs(b); break;
                case Shape.Ramp: BuildRamp(b); break;
            }
            return b.ToMesh(name + "_" + shape);
        }

        public static void BuildBox(MeshBuilder b, Vector3 c, Vector3 h, float k)
        {
            if (k <= 0.0005f)
            {
                Vector3[] p = new Vector3[8];
                for (int i = 0; i < 8; i++) p[i] = c + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                b.Quad(c, p[1], p[3], p[7], p[5]); b.Quad(c, p[0], p[4], p[6], p[2]);
                b.Quad(c, p[2], p[6], p[7], p[3]); b.Quad(c, p[0], p[1], p[5], p[4]);
                b.Quad(c, p[4], p[5], p[7], p[6]); b.Quad(c, p[0], p[2], p[3], p[1]);
                return;
            }
            // 6 inset faces
            for (int axis = 0; axis < 3; axis++)
                for (int s = -1; s <= 1; s += 2)
                {
                    int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                    Vector3 P(float u, float v) { var r = Vector3.zero; r[axis] = s * h[axis]; r[a1] = u; r[a2] = v; return c + r; }
                    float u0 = -h[a1] + k, u1 = h[a1] - k, v0 = -h[a2] + k, v1 = h[a2] - k;
                    b.Quad(c, P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1));
                }
            // 12 edge strips
            for (int axis = 0; axis < 3; axis++)
            {
                int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                for (int s1 = -1; s1 <= 1; s1 += 2)
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        Vector3 Q(float t, bool onA1)
                        {
                            var r = Vector3.zero; r[axis] = t;
                            r[a1] = s1 * (onA1 ? h[a1] : h[a1] - k);
                            r[a2] = s2 * (onA1 ? h[a2] - k : h[a2]);
                            return c + r;
                        }
                        float t0 = -h[axis] + k, t1 = h[axis] - k;
                        b.Quad(c, Q(t0, true), Q(t1, true), Q(t1, false), Q(t0, false));
                    }
            }
            // 8 corner triangles
            for (int i = 0; i < 8; i++)
            {
                Vector3 s = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                Vector3 A = c + Vector3.Scale(s, new Vector3(h.x, h.y - k, h.z - k));
                Vector3 B = c + Vector3.Scale(s, new Vector3(h.x - k, h.y, h.z - k));
                Vector3 C = c + Vector3.Scale(s, new Vector3(h.x - k, h.y - k, h.z));
                b.Tri(c, A, B, C);
            }
        }

        void BuildStairs(MeshBuilder b)
        {
            // Runs along +Z, rises along +Y, origin at the bottom-centre of the first step's front edge.
            // Real stair geometry (tread + riser + two sides per step, back, bottom): no hidden overlapping faces,
            // so the paint atlas only spends texels on surfaces you can actually see.
            int n = Mathf.Max(1, steps);
            float w = size.x * 0.5f, rise = size.y / n, run = size.z / n, H = size.y, L = size.z;
            Vector3 c = new Vector3(0, H * 0.35f, L * 0.55f);   // inside point for outward winding
            for (int i = 0; i < n; i++)
            {
                float z0 = run * i, z1 = run * (i + 1), y0 = rise * i, y1 = rise * (i + 1);
                Vector3 ci = new Vector3(0, y1 * 0.5f, (z0 + z1) * 0.5f);
                b.Quad(ci, new Vector3(-w, y1, z0), new Vector3(w, y1, z0), new Vector3(w, y1, z1), new Vector3(-w, y1, z1));   // tread
                b.Quad(ci, new Vector3(-w, y0, z0), new Vector3(w, y0, z0), new Vector3(w, y1, z0), new Vector3(-w, y1, z0));   // riser
                b.Quad(ci, new Vector3(-w, 0, z0), new Vector3(-w, 0, z1), new Vector3(-w, y1, z1), new Vector3(-w, y1, z0));   // left side
                b.Quad(ci, new Vector3(w, 0, z0), new Vector3(w, 0, z1), new Vector3(w, y1, z1), new Vector3(w, y1, z0));       // right side
            }
            b.Quad(c, new Vector3(-w, 0, L), new Vector3(w, 0, L), new Vector3(w, H, L), new Vector3(-w, H, L));               // back
            b.Quad(c, new Vector3(-w, 0, 0), new Vector3(w, 0, 0), new Vector3(w, 0, L), new Vector3(-w, 0, L));               // bottom
        }

        void BuildRamp(MeshBuilder b)
        {
            float w = size.x * 0.5f, H = size.y, L = size.z;
            Vector3 c = new Vector3(0, H * 0.33f, L * 0.66f);
            Vector3 a0 = new Vector3(-w, 0, 0), a1 = new Vector3(w, 0, 0);
            Vector3 t0 = new Vector3(-w, H, L), t1 = new Vector3(w, H, L);
            Vector3 f0 = new Vector3(-w, 0, L), f1 = new Vector3(w, 0, L);
            b.Quad(c, a0, a1, t1, t0);          // slope
            b.Quad(c, f0, t0, t1, f1);          // back
            b.Quad(c, a0, f0, f1, a1);          // bottom
            b.Tri(c, a0, t0, f0); b.Tri(c, a1, f1, t1);
        }

        // Collider for stairs: a smooth ramp so the CharacterController glides (visual steps, smooth feet).
        public Mesh GenerateWalkCollider()
        {
            if (shape != Shape.Stairs) return null;
            var b = new MeshBuilder();
            float w = size.x * 0.5f, H = size.y, L = size.z, run = L / Mathf.Max(1, steps);
            Vector3 c = new Vector3(0, H * 0.3f, L * 0.6f);
            Vector3 a0 = new Vector3(-w, 0, 0), a1 = new Vector3(w, 0, 0);
            Vector3 t0 = new Vector3(-w, H, L - run), t1 = new Vector3(w, H, L - run);
            Vector3 e0 = new Vector3(-w, H, L), e1 = new Vector3(w, H, L);
            Vector3 f0 = new Vector3(-w, 0, L), f1 = new Vector3(w, 0, L);
            b.Quad(c, a0, a1, t1, t0); b.Quad(c, t0, t1, e1, e0);
            b.Quad(c, f0, e0, e1, f1); b.Quad(c, a0, f0, f1, a1);
            b.Quad(c, a0, t0, e0, f0); b.Quad(c, a1, f1, e1, t1);
            return b.ToMesh(name + "_WalkCollider");
        }
    }

    // Minimal mesh builder: every polygon gets its own vertices (flat normals, clean paint islands).
    public class MeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        public void Quad(Vector3 center, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 nn = Vector3.Cross(b - a, c - a).normalized;
            Vector3 mid = (a + b + c + d) * 0.25f;
            if (Vector3.Dot(nn, mid - center) < 0f) { (b, d) = (d, b); nn = -nn; }
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int k = 0; k < 4; k++) n.Add(nn);
            AddPlanarUV(nn, a, b, c, d);
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        public void Tri(Vector3 center, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 nn = Vector3.Cross(b - a, c - a).normalized;
            Vector3 mid = (a + b + c) / 3f;
            if (Vector3.Dot(nn, mid - center) < 0f) { (b, c) = (c, b); nn = -nn; }
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            for (int k = 0; k < 3; k++) n.Add(nn);
            AddPlanarUV(nn, a, b, c);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        void AddPlanarUV(Vector3 nn, params Vector3[] p)
        {
            Vector3 u = Mathf.Abs(nn.y) > 0.9f ? Vector3.right : Vector3.Cross(Vector3.up, nn).normalized;
            Vector3 w = Vector3.Cross(nn, u);
            foreach (var q in p) uv.Add(new Vector2(Vector3.Dot(q, u), Vector3.Dot(q, w)));   // metres
        }

        public Mesh ToMesh(string name)
        {
            // Unity front faces: triangle (a,b,c) faces along cross(b-a, c-a), which Quad/Tri orient outward.
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
