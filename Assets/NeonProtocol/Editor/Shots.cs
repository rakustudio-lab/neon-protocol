using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NeonProtocol.EditorTools
{
    // Renders any viewpoint of the open scene (edit or play mode, with post-processing) to Builds/shots/editor/<name>.png.
    // Used for art review and side-by-side comparison with the concept boards.
    public static class Shots
    {
        static bool post = true;

        // Fixed review cameras (player-camera height/framing) for before/after art comparisons: arrival, east catwalk,
        // and the sharpest turn of the escape track. Renders each with post-processing ON and OFF.
        public static string Views(string prefix)
        {
            var r = "";
            var track = Object.FindFirstObjectByType<FinalityTrack>();
            float bestS = 0f, bestTurn = 0f;
            if (track) for (float s0 = 40f; s0 < track.Length - 40f; s0 += 5f)
            {
                Vector3 a = track.World(s0 - 10f, 0f), b = track.World(s0, 0f), c = track.World(s0 + 10f, 0f);
                float turn = Vector3.Angle(b - a, c - b); if (turn > bestTurn) { bestTurn = turn; bestS = s0; }
            }
            var views = new System.Collections.Generic.List<(string, Vector3, Vector3)> {
                ("arrival", new Vector3(0.9f, 3.9f, -35.2f), new Vector3(3f, 3.0f, -22f)),
                ("catwalk", new Vector3(18.9f, 6.5f, -10.5f), new Vector3(20.6f, 5.8f, -1f)) };
            if (track) views.Add(("escapeturn", track.World(bestS - 14f, 0f, 2.6f), track.World(bestS + 6f, 0f, 1.0f)));
            foreach (var on in new[] { true, false })
            {
                post = on;
                foreach (var (n, p, l) in views) r += Shot($"{prefix}_{n}_{(on ? "post" : "nopost")}", p, l, 62f, 1280, 720) + " | ";
            }
            post = true;
            return r + $"escape turn at s={bestS:F0} ({bestTurn:F0} deg per 20 m)";
        }

        public static string Shot(string name, Vector3 pos, Vector3 lookAt, float fov = 60f, int w = 1600, int h = 900)
        {
            var go = new GameObject("_ArtShotCam") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var cam = go.AddComponent<Camera>();
                var main = Camera.main;
                if (main) { cam.CopyFrom(main); }
                cam.enabled = false;
                cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 400f;
                go.transform.position = pos; go.transform.LookAt(lookAt);
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = post; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                string dir = System.IO.Path.GetFullPath("Builds/shots/editor");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, name + ".png");
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
                return path;
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
