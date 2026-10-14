using UnityEngine;

namespace NeonProtocol
{
    // Gate 1 test HUD (IMGUI, zero assets): dynamic crosshair, hit marker, ammo, FPS, quality.
    // Replaced by the final UI Toolkit HUD later.
    public class DevHud : MonoBehaviour
    {
        public PaintRifle rifle;
        public static bool PointerOverUI;
        public bool showStats = true;

        float hitMarkT, fpsAccum, fps; int fpsFrames; float fpsTimer;
        Texture2D white;
        GUIStyle big, small;

        void Start()
        {
            // The web build uses the HTML HUD; keep this IMGUI HUD for the editor (or ?dev=1).
            if (Application.platform == RuntimePlatform.WebGLPlayer && !Application.absoluteURL.Contains("dev=1")) { enabled = false; return; }
            white = Texture2D.whiteTexture;
            if (PaintProjectiles.Instance) PaintProjectiles.Instance.Impact += (h, counted) => { if (counted && h.team == 0) hitMarkT = 1f; };
        }

        void Update()
        {
            hitMarkT = Mathf.Max(0f, hitMarkT - Time.unscaledDeltaTime * 5f);
            fpsAccum += Time.unscaledDeltaTime; fpsFrames++; fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 0.5f) { fps = fpsFrames / fpsAccum; fpsAccum = 0; fpsFrames = 0; fpsTimer = 0; }
        }

        void Line(float x, float y, float w, float h, Color c) { GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), white); }

        void OnGUI()
        {
            big ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 13 };
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            var cam = Camera.main;

            if (Cursor.lockState != CursorLockMode.Locked && !NPInput.UseOverride)
            {
                // Editor/desktop title card (the browser build uses the HTML menu instead)
                GUI.color = new Color(0.02f, 0.01f, 0.06f, 0.82f); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
                float u = Screen.height / 1080f, pw = 760 * u, ph = 330 * u, px = cx - pw * 0.5f, py = cy - ph * 0.5f;
                GUI.color = new Color(0.07f, 0.05f, 0.14f, 0.92f); GUI.DrawTexture(new Rect(px, py, pw, ph), white);
                GUI.color = new Color(0.95f, 0.2f, 0.85f, 1f); GUI.DrawTexture(new Rect(px, py, pw, 3 * u), white);
                GUI.color = new Color(0.2f, 0.95f, 0.9f, 1f); GUI.DrawTexture(new Rect(px, py + ph - 3 * u, pw, 3 * u), white);
                GUI.color = Color.white;
                var t1 = new GUIStyle(big) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(64 * u) };
                var t2 = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(22 * u), wordWrap = true };
                var t3 = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(26 * u), fontStyle = FontStyle.Bold };
                GUI.Label(new Rect(px, py + 24 * u, pw, 80 * u), "NEON PROTOCOL", t1);
                GUI.color = new Color(0.8f, 0.85f, 1f, 0.9f);
                GUI.Label(new Rect(px + 40 * u, py + 112 * u, pw - 80 * u, 110 * u), "Paint the 3 security relays, steal the Core from the vault, and escape through the Finality Gate before the timer runs out.", t2);
                GUI.color = new Color(0.2f, 0.95f, 0.9f, 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.5f)));
                GUI.Label(new Rect(px, py + ph - 90 * u, pw, 50 * u), "CLICK TO START", t3);
                return;
            }

            if (rifle && cam)
            {
                // Crosshair gap reflects the real spread cone at the current FOV.
                float spreadPx = Mathf.Tan(rifle.CurrentSpread * Mathf.Deg2Rad) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cy;
                float gap = 4f + spreadPx, len = rifle.AimMode == 2 ? 14f : 9f, th = 2f;
                Color c = new Color(1, 1, 1, 0.92f), sh = new Color(0, 0, 0, 0.5f);
                foreach (var col in new[] { sh, c })
                {
                    float o = col == sh ? 1f : 0f;
                    Line(cx - gap - len + o, cy - th * 0.5f + o, len, th, col);
                    Line(cx + gap + o, cy - th * 0.5f + o, len, th, col);
                    Line(cx - th * 0.5f + o, cy - gap - len + o, th, len, col);
                    Line(cx - th * 0.5f + o, cy + gap + o, th, len, col);
                }
                Line(cx - 1, cy - 1, 2, 2, c);
                if (hitMarkT > 0f)
                {
                    var hc = new Color(1f, 0.25f, 0.85f, hitMarkT);
                    var m = GUI.matrix;
                    GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
                    float hg = 7f + (1f - hitMarkT) * 4f;
                    Line(cx - hg - 9, cy - 1.5f, 9, 3, hc); Line(cx + hg, cy - 1.5f, 9, 3, hc);
                    Line(cx - 1.5f, cy - hg - 9, 3, 9, hc); Line(cx - 1.5f, cy + hg, 3, 9, hc);
                    GUI.matrix = m;
                }
                GUI.color = Color.white;
                string ammo = rifle.Reloading ? $"RELOAD {(int)(rifle.ReloadProgress * 100)}%" : $"{rifle.Ammo} / {rifle.magazine}";
                GUI.Label(new Rect(Screen.width - 260, Screen.height - 70, 230, 40), ammo, big);
            }

            if (showStats)
            {
                GUI.color = new Color(1, 1, 1, 0.75f);
                var ps = PaintSystem.Instance; var pj = PaintProjectiles.Instance;
                if (RuntimeErrorMonitor.Errors + RuntimeErrorMonitor.Exceptions > 0) { GUI.color = new Color(1f, 0.3f, 0.3f); GUI.Label(new Rect(12, 30, 900, 20), "RUNTIME ERRORS: " + RuntimeErrorMonitor.Summary(), small); GUI.color = new Color(1, 1, 1, 0.75f); }
                GUI.Label(new Rect(12, 10, 600, 20), $"{fps:F0} FPS · {QualityPresets.Names[QualityPresets.Current]} · splats {ps?.SplatsThisSession ?? 0} · balls {pj?.ActiveBalls ?? 0} · drops {pj?.ActiveDrops ?? 0} · atlas {(ps && ps.Atlas ? ps.Atlas.width : 0)}", small);
            }
        }
    }
}
