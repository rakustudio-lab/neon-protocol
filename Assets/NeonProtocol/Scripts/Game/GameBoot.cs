using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonProtocol
{
    // Loads settings, manages mouse lock (browser pointer lock needs a user click), signals readiness to the page.
    [DefaultExecutionOrder(-100)]
    public class GameBoot : MonoBehaviour
    {
        public static bool Paused { get; private set; } = true;
        public static bool ControllerSession;
        static bool controllerRunning;

        void Awake()
        {
            Settings.Load();
            Application.runInBackground = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLInput.captureAllKeyboardInput = false;   // keys reach the game only when the canvas has focus: HTML menus keep Tab / Enter
#endif
        }

        void Start()
        {
            WebBridge.NotifyReady();
            Debug.Log("[NP] ready");
        }

        static bool IsWeb => Application.platform == RuntimePlatform.WebGLPlayer;

        void Update()
        {
            var m = Mouse.current; var kb = Keyboard.current;
            var dir = HeistDirector.Instance;
            bool active = dir && (dir.Current == HeistDirector.Phase.Infiltrate || dir.Current == HeistDirector.Phase.CoreUnlocked || dir.Current == HeistDirector.Phase.Pursuit || dir.Current == HeistDirector.Phase.FinalityRun);
            if (Cursor.lockState != CursorLockMode.Locked && !(ControllerSession && controllerRunning && Gamepad.current != null) && !NPInput.UseOverride)
            {
                Paused = true;
                // Editor/desktop: click to play. Web: the HTML menu starts runs.
                if (!IsWeb && m != null && m.leftButton.wasPressedThisFrame && NPInput.Enabled)
                {
                    Lock();
                    if (dir && (dir.Current == HeistDirector.Phase.Briefing || dir.Current == HeistDirector.Phase.Extracted || dir.Current == HeistDirector.Phase.Failed)) dir.BeginRun();
                }
            }
            else Paused = false;
            // The timer must not run while the player is in a menu.
            bool frozen = Paused && (active || (VaultRoom.Instance && VaultRoom.Instance.Active)) && !NPInput.UseOverride;
            Time.timeScale = frozen ? 0f : 1f;
            AudioListener.pause = frozen;   // music, loops and one-shots stop with the game
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Unlock();
            if (kb != null)
            {
                if (kb.f1Key.wasPressedThisFrame) { Settings.Quality = 0; Settings.Save(); }
                if (kb.f2Key.wasPressedThisFrame) { Settings.Quality = 1; Settings.Save(); }
                if (kb.f3Key.wasPressedThisFrame) { Settings.Quality = 2; Settings.Save(); }
            }
        }

        // --- called from the web page (SendMessage) ---
        [System.Serializable] class RunArgs { public int challengeOf; public string route = "featured"; }
        [System.Serializable] class WebSettings { public float sens = 1f, vol = 0.8f, shake = 1f; public string quality = "medium"; }

        public void StartRunFromWeb(string json)
        {
            var a = string.IsNullOrEmpty(json) ? new RunArgs() : JsonUtility.FromJson<RunArgs>(json);
            Lock();
            var dir = HeistDirector.Instance;
            HeistDirector.FullRoute = a.route == "full";
            if (dir) { dir.challengeOf = a.challengeOf; dir.BeginRun(); }
        }

        public void ResumeFromWeb() => Lock();
        public void PauseFromWeb() => Unlock();
        public void SetControllerMode(string mode) { ControllerSession = mode == "1"; if (!ControllerSession) controllerRunning = false; }

        // THE VAULT (walkable exhibition): page -> Unity
        public void EnterVaultFromWeb() { Lock(); VaultRoom.Instance?.Enter(); }
        public void LeaveVaultFromWeb() { VaultRoom.Instance?.Leave(); }
        public void SetVaultExhibit(string json) => VaultRoom.Instance?.SetExhibit(json);
        public void SetVaultGate(string json) => VaultRoom.Instance?.SetGate(json);

        public void SetFeaturedArt(string json)
        {
            var wall = FindFirstObjectByType<ArtWall>();
            if (wall) wall.Apply(json);
        }

        public void QuitToMenuFromWeb()
        {
            Unlock();
            var dir = HeistDirector.Instance;
            if (dir) dir.ToBriefing();
        }

        public void ApplyWebSettings(string json)
        {
            var w = JsonUtility.FromJson<WebSettings>(json);
            NPInput.Sensitivity = Mathf.Clamp(w.sens, 0.2f, 3f);
            Settings.MasterVolume = Mathf.Clamp01(w.vol);
            Settings.CameraShake = Mathf.Clamp01(w.shake);
            Settings.Quality = w.quality == "low" ? 0 : w.quality == "high" ? 2 : 1;
            Settings.Save();
        }

        public void SetQualityFromWeb(string q)
        {
            Settings.Quality = q == "low" ? 0 : q == "high" ? 2 : 1;
            Settings.Save();
        }

        // Camera mode from the page settings: "fp" (first-person trial with weapon view) or "tp" (third person).
        public void SetCameraMode(string m)
        {
            var rig = FindFirstObjectByType<ShoulderRig>(FindObjectsInactive.Include); if (rig) rig.firstPersonTrial = m == "fp";
            PlayerPrefs.SetString("np.cam", m == "fp" ? "fp" : "tp");
        }

        public void RunWebTest(string name) => StartCoroutine(WebTest(name));

        // Perf diagnosis in the real build: switch ONE system off for a whole scripted run and compare run averages.
        // cfg: base | scale50 | noshadow | nopost | nopaint | noanim | nohud | noparticles | noaudio
        public void Diag(string cfg)
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (cfg == "scale50") urp.renderScale = 0.5f;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional && cfg == "noshadow") l.shadows = LightShadows.None;
            if (cfg == "nopost") { var v = FindFirstObjectByType<UnityEngine.Rendering.Volume>(); if (v) v.enabled = false; var cd = Camera.main ? Camera.main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() : null; if (cd) cd.renderPostProcessing = false; }
            if (cfg == "nopaint" && PaintSystem.Instance) PaintSystem.Instance.enabled = false;
            if (cfg == "noanim") foreach (var an in FindObjectsByType<Animator>(FindObjectsSortMode.None)) an.enabled = false;
            if (cfg == "nohud") foreach (var h in FindObjectsByType<WebHud>(FindObjectsSortMode.None)) h.enabled = false;
            if (cfg == "noparticles") foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { var r = ps.GetComponent<ParticleSystemRenderer>(); if (r) r.enabled = false; }
            if (cfg == "noaudio") AudioListener.volume = 0f;
            Debug.Log("[NP] diag " + cfg);
        }

        // Perf scan (rescue order §1): same viewpoints, one factor changed at a time, frame times reported as JSON.
        // Render scale isolates GPU fill cost; the others isolate post, shadows, paint system CPU/GPU work, extra lights.
        System.Collections.IEnumerator PerfScan()
        {
            while (!ShaderWarmup.Done) yield return null;
            var dir = HeistDirector.Instance; NPInput.UseOverride = true; dir.BeginRun(); yield return null;
            foreach (var ai in FindObjectsByType<CompetitorAI>(FindObjectsSortMode.None)) ai.enabled = false;   // same frame every time
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            var vol = FindFirstObjectByType<UnityEngine.Rendering.Volume>(); var cam = Camera.main;
            var camData = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            Light sun = null; foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) sun = l;
            var paint = PaintSystem.Instance;
            float baseScale = urp.renderScale; int baseLights = urp.maxAdditionalLightsCount; var baseShadow = sun ? sun.shadows : LightShadows.None;
            var views = new (string n, Vector3 p, float yaw, float pitch)[] { ("arrival", new Vector3(0f, 2.05f, -31f), 30f, 4f), ("hall", new Vector3(4f, 0f, -20f), 40f, 2f), ("catwalk", new Vector3(19.5f, 4.55f, -10f), 5f, 4f) };
            var cfgs = new[] { "base", "scale50", "nopost", "noshadow", "nopaint", "nolights", "allOff", "rivalsOn", "rivalsOnNoAnim", "noAnim" };
            var rivals = FindObjectsByType<CompetitorAI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var animators = FindObjectsByType<Animator>(FindObjectsSortMode.None);
            var sb = new System.Text.StringBuilder("{\"type\":\"perfscan\",\"quality\":" + QualityPresets.Current + ",\"res\":\"" + Screen.width + "x" + Screen.height + "\",\"results\":[");
            bool first = true; var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var v in views)
            {
                dir.player.Teleport(v.p, v.yaw); dir.player.rig.SetAim(v.yaw, v.pitch);
                foreach (var c in cfgs)
                {
                    bool all = c == "allOff";
                    urp.renderScale = c == "scale50" || all ? 0.5f : baseScale;
                    if (vol) vol.enabled = !(c == "nopost" || all); if (camData) camData.renderPostProcessing = !(c == "nopost" || all);
                    if (sun) sun.shadows = c == "noshadow" || all ? LightShadows.None : baseShadow;
                    if (paint) paint.enabled = !(c == "nopaint" || all);
                    urp.maxAdditionalLightsCount = c == "nolights" || all ? 0 : baseLights;
                    bool rOn = c.StartsWith("rivalsOn"), noAnim = c.EndsWith("NoAnim") || c == "noAnim";
                    foreach (var ai in rivals) if (ai) { ai.enabled = rOn; if (rOn) ai.Wake(); var ag = ai.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (ag && ag.isOnNavMesh) ag.isStopped = !rOn; }
                    foreach (var an in animators) if (an) an.enabled = !noAnim;
                    for (int k = 0; k < 30; k++) yield return null;   // settle
                    var f = new System.Collections.Generic.List<float>(400); float t = 0;
                    while (t < 4f) { f.Add(Time.unscaledDeltaTime * 1000f); t += Time.unscaledDeltaTime; yield return null; }
                    f.Sort(); float avg = 0; foreach (var x in f) avg += x; avg /= f.Count;
                    if (!first) sb.Append(','); first = false;
                    sb.Append("{\"view\":\"").Append(v.n).Append("\",\"cfg\":\"").Append(c).Append("\",\"avgMs\":").Append(avg.ToString("F1", inv)).Append(",\"p95Ms\":").Append(f[(int)(f.Count * 0.95f)].ToString("F1", inv)).Append('}');
                }
            }
            foreach (var an in animators) if (an) an.enabled = true;
            urp.renderScale = baseScale; urp.maxAdditionalLightsCount = baseLights; if (sun) sun.shadows = baseShadow; if (vol) vol.enabled = true; if (camData) camData.renderPostProcessing = true; if (paint) paint.enabled = true;
            sb.Append("]}");
            WebBridge.Send(sb.ToString()); Debug.Log("[NP] perf " + sb);
        }

        System.Collections.IEnumerator WebTest(string name)
        {
            if (name == "perfscan") { yield return PerfScan(); yield break; }
            while (!ShaderWarmup.Done) yield return null;
            yield return new WaitForSeconds(1f);
            var drv = TestDriver.Ensure();
            var frames = new System.Collections.Generic.List<float>(4096);
            float t = 0f, dur = name == "fire" ? 30f : name == "finality" ? 34f : name == "g" ? 52f : 10f;
            if (name == "g") AuditDirector.Begin("G");
            if (name == "j") { AuditDirector.Begin("J"); dur = 230f; }
            if (name == "g3") { AuditDirector.Begin("G3"); dur = 45f; }
            if (name == "vw") { AuditDirector.Begin("VW"); dur = 40f; }   // the Vault walk after the route (scripted QA input)   // the encounter (scripted QA input)   // featured judge route, scripted QA input   // Gate 1 first minute, scripted input through the real input path
            if (name == "finality" && HeistDirector.Instance && FinalityRun.Instance)
            {
                HeistDirector.Instance.PracticeFinality();
                FinalityRun.Instance.gameObject.AddComponent<BikeAutopilot>();
            }
            if (name == "fire")
            {
                var steps = new System.Collections.Generic.List<TestDriver.Step>();
                for (int i = 0; i < 12; i++) steps.AddRange(TestDriver.FireAt(-40f + i * 17f, (i % 3) * 6f - 4f, 1.6f, i % 2 == 0));
                drv.Run(steps);
            }
            while (t < dur) { frames.Add(Time.unscaledDeltaTime * 1000f); t += Time.unscaledDeltaTime; yield return null; }
            frames.Sort();
            float avg = 0; foreach (var f in frames) avg += f; avg /= frames.Count;
            float p95 = frames[(int)(frames.Count * 0.95f)], p99 = frames[(int)(frames.Count * 0.99f)], max = frames[frames.Count - 1];
            var ps = PaintSystem.Instance;
            string json = "{\"type\":\"perf\",\"test\":\"" + name + "\",\"frames\":" + frames.Count + ",\"avgMs\":" + avg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"p95Ms\":" + p95.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ",\"p99Ms\":" + p99.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"maxMs\":" + max.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ",\"splats\":" + (ps ? ps.SplatsThisSession : 0)
                + (name == "finality" && FinalityRun.Instance ? ",\"bikeS\":" + Mathf.RoundToInt(FinalityRun.Instance.bike.S) + ",\"shards\":" + FinalityRun.Instance.Shards + ",\"phase\":\"" + HeistDirector.Instance.Current + "\"" : "")
                + ",\"monoMB\":" + (System.GC.GetTotalMemory(false) / 1048576f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ",\"quality\":" + QualityPresets.Current + "}";
            Debug.Log("[NP] perf " + json);
            WebBridge.Send(json);
            drv.StopTest();
        }

        public static void Lock() { controllerRunning = ControllerSession; if (!ControllerSession) Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        public static void Unlock() { controllerRunning = false; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}
