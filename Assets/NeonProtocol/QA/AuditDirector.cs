using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

namespace NeonProtocol
{
    // AUDIT ONLY (2026-10-11 independent review). Drives the REAL player through NPInput (same path as keyboard/mouse)
    // and adds a spectator camera so the body can be seen from the front/side. Changes no game rules.
    // Every segment is labelled on screen. Nothing here makes the game look better than it is.
    public class AuditDirector : MonoBehaviour
    {
        public static AuditDirector Instance;
        public static string Status = "";
        public static bool Done;
        public const string Version = "r4-v30";
        string label = "", sub = "";
        Camera spec; PlayerMotor pm; ShoulderRig rig; HeistDirector hd;
        System.Func<(Vector3 pos, Vector3 look)> camFn;
        float slow = 1f;
        public static readonly List<string> Marks = new List<string>();
        float t0;
        CompetitorAI[] ais; SentinelDrone[] drones;

        public static AuditDirector Begin(string which)
        {
            if (Instance) Destroy(Instance.gameObject);
            var go = new GameObject("_AuditDirector"); Instance = go.AddComponent<AuditDirector>();
            Done = false; Marks.Clear(); Status = "start " + which;
            Instance.StartCoroutine(which == "A" ? Instance.VideoA() : which == "B" ? Instance.VideoB() : which == "C" ? Instance.VideoC() : which == "C2" ? Instance.VideoC2() : which == "M" ? Instance.Metrics() : which == "T" ? Instance.TurnCompare() : which == "M1" ? Instance.Milestone1() : which == "F" ? Instance.Factions() : which == "P" ? Instance.PathProbe() : which == "I" ? Instance.IrisTest() : which == "K" ? Instance.StrideCal() : which == "G" ? Instance.FirstMinute() : which == "J" ? Instance.JudgeRoute() : which == "R" ? Instance.RigDiag() : which == "G2" ? Instance.RoomMinute() : which == "G2FP" ? Instance.RoomMinuteFP() : which == "BP" ? Instance.BoundsProbe() : which == "EV" ? Instance.EncounterVerify() : which == "G3" ? Instance.EncounterMinute() : which == "FR" ? Instance.FrameRates() : which == "DR" ? Instance.DoorTest() : which == "BK" ? Instance.BikeTest() : which == "SW" ? Instance.SwitchProbe() : which == "VW" ? Instance.VaultWalk() : which == "JV" ? Instance.RouteThenVault() : which == "GW" ? Instance.GhostWalls() : Instance.VideoD());
            return Instance;
        }

        void Setup()
        {
            hd = HeistDirector.Instance; pm = hd.player; rig = pm.rig;
            var main = Camera.main;
            var go = new GameObject("_AuditSpecCam"); spec = go.AddComponent<Camera>();
            spec.CopyFrom(main); spec.depth = main.depth + 20; spec.enabled = false;
            var ud = spec.GetUniversalAdditionalCameraData(); ud.renderPostProcessing = true; ud.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            spec.fieldOfView = 45f;
            t0 = Time.time;
            // cache: a disabled AI unregisters itself from CompetitorAIRegistry, so the registry can't be used to re-enable it
            ais = FindObjectsByType<CompetitorAI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            drones = FindObjectsByType<SentinelDrone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        void OnDestroy() { if (spec) Destroy(spec.gameObject); if (Instance == this || !Instance) { NPInput.UseOverride = false; NPInput.Override = default; } }   // R4: a replaced routine must not switch off its successor's input (Destroy is deferred)

        void LateUpdate()
        {
            if (slow != 1f) Time.timeScale = slow;   // GameBoot resets timeScale in Update; re-apply after it
            if (spec && camFn != null)
            {
                var c = camFn(); spec.enabled = true;
                // keep the spectator lens out of walls: pull it toward the subject if geometry is in between
                Vector3 d = c.pos - c.look; float len = d.magnitude;
                if (len > 0.01f && Physics.SphereCast(c.look, 0.2f, d / len, out var hit, len, ~(1 << 8), QueryTriggerInteraction.Ignore)) c.pos = c.look + d / len * Mathf.Max(0.6f, hit.distance - 0.1f);
                spec.transform.position = c.pos; spec.transform.rotation = Quaternion.LookRotation(c.look - c.pos);
            }
            else if (spec) spec.enabled = false;
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(label)) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height / 30f), fontStyle = FontStyle.Bold };
            var s2 = new GUIStyle(st) { fontSize = Mathf.RoundToInt(Screen.height / 48f), fontStyle = FontStyle.Normal };
            GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(new Rect(0, Screen.height - Screen.height * 0.13f, Screen.width, Screen.height * 0.13f), Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(new Rect(20, Screen.height * 0.875f, Screen.width - 40, Screen.height * 0.06f), label, st);
            GUI.Label(new Rect(20, Screen.height * 0.935f, Screen.width - 40, Screen.height * 0.06f), sub + (slow != 1f ? $"   [SLOW MOTION x{slow:F2}]" : "") + $"   t={Time.time - t0:F1}s", s2);
        }

        void Seg(string l, string s = "") { label = l; sub = s; Marks.Add($"{Time.time - t0:F1}s {l}"); Status = l; }

        // ---------- camera helpers ----------
        Vector3 Chest => pm.transform.position + Vector3.up * 1.2f;
        void Chase() => camFn = null;
        void Rel(Vector3 localOffset, float lookH = 1.15f) => camFn = () => (pm.transform.position + pm.transform.rotation * localOffset, pm.transform.position + Vector3.up * lookH);
        void WorldOff(Vector3 off, float lookH = 1.0f) => camFn = () => (pm.transform.position + off, pm.transform.position + Vector3.up * lookH);
        void Fixed(Vector3 pos, Transform look, float lookH = 1.1f) => camFn = () => (pos, look.position + Vector3.up * lookH);
        void Orbit(Transform tgt, float r, float h, float degPerSec, float startDeg = 0f, float lookH = 1.1f)
        {
            float s = Time.time;
            camFn = () => { float a = (startDeg + (Time.time - s) * degPerSec) * Mathf.Deg2Rad; return (tgt.position + new Vector3(Mathf.Sin(a) * r, h, Mathf.Cos(a) * r), tgt.position + Vector3.up * lookH); };
        }

        // ---------- input helpers ----------
        IEnumerator Hold(float sec, NPInput.State s, float? yawRate = null)
        {
            NPInput.UseOverride = true; float t = 0; bool first = true;
            while (t < sec)
            {
                var x = s; if (!first) { x.crouchPressed = x.jumpPressed = x.reloadPressed = x.zoomPressed = x.shoulderPressed = x.interactPressed = false; }
                NPInput.Override = x; first = false;
                if (yawRate.HasValue) rig.SetAim(rig.Yaw + yawRate.Value * Time.deltaTime, rig.Pitch);
                yield return null; t += Time.deltaTime;
            }
            NPInput.Override = default;
        }
        IEnumerator Idle(float sec) => Hold(sec, default);
        static NPInput.State Mv(float x, float y, bool sprint = false, bool aim = false, bool fire = false) => new NPInput.State { move = new Vector2(x, y), sprint = sprint, aim = aim, fire = fire };
        void Place(Vector3 p, float yaw, float pitch = 4f) { pm.Teleport(p, yaw); rig.SetAim(yaw, pitch); }
        IEnumerator EnsureStanding() { if (pm.Crouched) { NPInput.Override = new NPInput.State { crouchPressed = true }; yield return null; NPInput.Override = default; yield return null; } }

        void FreezeHostiles(bool frozen)
        {
            foreach (var ai in ais) { if (!ai) continue; var ag = ai.GetComponent<NavMeshAgent>(); ag.isStopped = frozen; ai.enabled = !frozen; }
            foreach (var dr in drones) if (dr) dr.enabled = !frozen;
        }

        // =====================================================================================================
        IEnumerator VideoA()
        {
            Setup();
            hd.BeginRun();
            yield return null;
            FreezeHostiles(true);
            // move frozen rivals out of the test lane so they don't block
            foreach (var ai in ais) if (ai && ai.name.Contains("VANTA")) ai.GetComponent<NavMeshAgent>().Warp(new Vector3(6f, 0f, 4f));
            var lane = new Vector3(-8f, 2.05f, -34f);      // arrival platform: open 18 x 14 m, run east
            var open = new Vector3(0f, 2.05f, -30f);

            Place(open, 0f); yield return Idle(0.5f);
            Seg("A01 IDLE — gameplay camera (behind)", "Scripted QA input through the real NPInput path. Rivals and drone frozen for this clip.");
            Chase(); yield return Idle(3f);
            Seg("A02 IDLE — spectator orbit (front / side / back)");
            Orbit(pm.transform, 3.2f, 0.3f, 60f, 180f); yield return Idle(6f);

            Seg("A03 WALK — analog half-stick (keyboard has NO walk key: WASD always runs)", "move = 0.4");
            Place(lane, 90f); Rel(new Vector3(-3.4f, 0.3f, 0.6f)); yield return Hold(4f, Mv(0, 0.4f));
            Seg("A04 RUN — side view", "move = 1.0 (WASD)");
            Place(lane, 90f); Rel(new Vector3(-3.6f, 0.3f, 1.0f)); yield return Hold(3.2f, Mv(0, 1f));
            Seg("A05 RUN — front view (camera ahead of the character)");
            Place(lane, 90f); Rel(new Vector3(0.5f, 0.3f, 4.2f)); yield return Hold(3.2f, Mv(0, 1f));
            Seg("A06 RUN — gameplay camera");
            Place(lane, 90f); Chase(); yield return Hold(3.2f, Mv(0, 1f));
            Seg("A07 SPRINT then STOP — side view", "Shift + W, then release");
            Place(lane, 90f); Rel(new Vector3(-3.8f, 0.3f, 1.5f)); yield return Hold(2.1f, Mv(0, 1f, sprint: true)); yield return Idle(2f);
            Seg("A08 SPRINT then STOP — gameplay camera");
            Place(lane, 90f); Chase(); yield return Hold(2.1f, Mv(0, 1f, sprint: true)); yield return Idle(1.5f);

            Seg("A09 CAMERA TURN SLOW while standing (45 deg/s) — spectator above-side, watch the FEET", "Body yaw follows the camera; there is no turn-in-place animation");
            Place(open, 0f); WorldOff(new Vector3(2.4f, 1.6f, 2.4f), 0.3f); yield return Hold(5f, default, 45f);
            Seg("A10 CAMERA TURN FAST while standing (180 deg/s) — spectator, watch the FEET");
            yield return Hold(4f, default, 180f);
            Seg("A11 FULL CAMERA ORBIT while standing — gameplay camera (what the player sees)");
            Chase(); yield return Hold(5f, default, 72f);
            Seg("A12 90 deg TURNS while running — spectator high");
            Place(lane + new Vector3(0, 0, 1f), 90f); WorldOff(new Vector3(-2f, 6f, -5f), 0.6f);
            yield return Hold(1.3f, Mv(0, 1f)); rig.SetAim(rig.Yaw - 90f, rig.Pitch); yield return Hold(1.2f, Mv(0, 1f)); rig.SetAim(rig.Yaw + 90f, rig.Pitch); yield return Hold(1.3f, Mv(0, 1f)); yield return Idle(0.6f);
            Seg("A13 180 deg TURN while running — spectator high");
            Place(lane, 90f); WorldOff(new Vector3(0f, 6f, -6f), 0.6f);
            yield return Hold(1.6f, Mv(0, 1f)); rig.SetAim(rig.Yaw + 180f, rig.Pitch); yield return Hold(1.6f, Mv(0, 1f)); yield return Idle(0.8f);
            Seg("A14 180 deg TURN while running — gameplay camera");
            Place(lane, 90f); Chase(); yield return Hold(1.6f, Mv(0, 1f)); rig.SetAim(rig.Yaw + 180f, rig.Pitch); yield return Hold(1.6f, Mv(0, 1f)); yield return Idle(0.6f);

            Seg("A15 STRAFE LEFT / RIGHT — front view", "A then D");
            Place(open, 0f); Rel(new Vector3(0f, 0.3f, 4.5f)); yield return Hold(1.8f, Mv(-1, 0)); yield return Hold(1.8f, Mv(1, 0)); yield return Idle(0.6f);
            Seg("A16 STRAFE + BACKPEDAL — gameplay camera"); Place(open, 0f); Chase(); yield return Hold(1.6f, Mv(-1, 0)); yield return Hold(1.6f, Mv(1, 0)); yield return Hold(1.2f, Mv(0, -1)); yield return Idle(0.5f);
            Seg("A17 DIAGONALS (W+A, W+D, S+A, S+D) — spectator high front");
            Place(open + new Vector3(0, 0, -4f), 0f); WorldOff(new Vector3(0f, 3.5f, 7f), 0.8f);
            yield return Hold(1.3f, Mv(-0.7f, 0.7f)); yield return Hold(1.3f, Mv(0.7f, 0.7f)); yield return Hold(1.3f, Mv(-0.7f, -0.7f)); yield return Hold(1.3f, Mv(0.7f, -0.7f)); yield return Idle(0.5f);

            Seg("A18 CROUCH idle + crouch walk — side view", "C toggles crouch");
            Place(lane, 90f); Rel(new Vector3(-3f, 0.1f, 0.8f), 0.7f);
            yield return Hold(1.5f, new NPInput.State { crouchPressed = true }); yield return Hold(3f, Mv(0, 1f)); yield return Hold(1.5f, Mv(1f, 0f));
            yield return Hold(0.8f, new NPInput.State { crouchPressed = true }); yield return Idle(0.6f);
            Seg("A19 JUMP and LAND standing, then running JUMP — side view");
            Place(lane, 90f); yield return EnsureStanding(); Rel(new Vector3(-3.8f, 0.3f, 1.2f));
            yield return Idle(0.5f); yield return Hold(1.6f, new NPInput.State { jumpPressed = true });
            yield return Hold(0.6f, Mv(0, 1f)); var j = Mv(0, 1f); j.jumpPressed = true; yield return Hold(1.2f, j); yield return Hold(0.6f, Mv(0, 1f)); yield return Idle(1f);
            Seg("A20 JUMP — gameplay camera"); Place(lane, 90f); Chase(); yield return Hold(0.6f, Mv(0, 1f)); yield return Hold(1.2f, j); yield return Idle(1f);

            // shooting range: the hall's centre block is ~5 m east of this spot
            var range = new Vector3(-9f, 0.05f, -2f);
            Seg("A21 ADS (right mouse) standing — gameplay camera");
            Place(range, 90f, 2f); Chase(); yield return Hold(1f, default); yield return Hold(2.5f, new NPInput.State { aim = true });
            Seg("A22 ADS — spectator front-left (rifle alignment, both hands)");
            Rel(new Vector3(-1.2f, 0.45f, 1.9f), 1.35f); yield return Hold(3f, new NPInput.State { aim = true });
            Seg("A23 HIP FIRE standing — gameplay camera (crosshair vs impact)");
            Place(range, 90f, 2f); Chase(); yield return Hold(2.5f, new NPInput.State { fire = true }); yield return Idle(0.5f);
            Seg("A24 ADS FIRE standing — gameplay camera"); yield return Hold(2.5f, new NPInput.State { aim = true, fire = true }); yield return Idle(0.5f);
            Seg("A25 FIRE while strafing — spectator side");
            Rel(new Vector3(0.5f, 0.6f, -3.6f), 1.2f); yield return Hold(1.6f, Mv(0, 0, fire: true, aim: true)); yield return Hold(2.2f, new NPInput.State { move = new Vector2(1, 0), aim = true, fire = true }); yield return Hold(2.2f, new NPInput.State { move = new Vector2(-1, 0), aim = true, fire = true });
            Seg("A26 FIRE while strafing — gameplay camera");
            Place(range, 90f, 2f); Chase(); yield return Hold(2.2f, new NPInput.State { move = new Vector2(1, 0), aim = true, fire = true }); yield return Hold(2.2f, new NPInput.State { move = new Vector2(-1, 0), aim = true, fire = true }); yield return Idle(0.6f);

            Seg("A27 RELOAD (R) — gameplay camera");
            Place(range, 90f, 2f); Chase(); yield return Hold(1.2f, new NPInput.State { fire = true }); yield return Hold(2.2f, new NPInput.State { reloadPressed = true });
            Seg("A28 RELOAD — close-up front-left (hands vs chest)");
            Rel(new Vector3(-0.9f, 0.35f, 1.3f), 1.25f); yield return Hold(1f, new NPInput.State { fire = true }); yield return Hold(2.2f, new NPInput.State { reloadPressed = true });
            Seg("A29 RELOAD — close-up front-left, SLOW MOTION"); slow = 0.3f;
            yield return Hold(0.25f, new NPInput.State { fire = true }); yield return Hold(1.9f, new NPInput.State { reloadPressed = true });
            slow = 1f; Time.timeScale = 1f;
            Seg("A30 RELOAD — close-up right side, SLOW MOTION");
            Rel(new Vector3(1.3f, 0.3f, 0.6f), 1.25f); slow = 0.3f;
            yield return Hold(0.25f, new NPInput.State { fire = true }); yield return Hold(1.9f, new NPInput.State { reloadPressed = true });
            slow = 1f; Time.timeScale = 1f;
            Seg("A31 RELOAD while running — side view");
            Place(lane, 90f); Rel(new Vector3(-3.2f, 0.3f, 0.8f), 1.2f); yield return Hold(0.6f, new NPInput.State { fire = true }); var rr = Mv(0, 1f); rr.reloadPressed = true; yield return Hold(2.4f, rr); yield return Idle(0.5f);

            Seg("A32 WALL: run into a wall with ADS, rifle vs wall — spectator side");
            Place(new Vector3(-6.3f, 0.05f, -2f), 90f, 2f); Rel(new Vector3(-0.4f, 0.4f, -2.6f), 1.2f);
            yield return Hold(2f, new NPInput.State { move = new Vector2(0, 1), aim = true }); yield return Hold(2f, new NPInput.State { aim = true });
            Seg("A33 WALL: same, gameplay camera; then camera orbit with the back to the wall");
            Chase(); yield return Hold(2f, new NPInput.State { move = new Vector2(0, 1), aim = true });
            rig.SetAim(rig.Yaw + 180f, 4f); yield return Hold(1f, Mv(0, -1f)); yield return Hold(5f, default, 72f);
            Seg("A34 WALL: aim along the wall (rifle sideways into it) — spectator");
            Place(new Vector3(-6.3f, 0.05f, -2f), 0f, 2f); Rel(new Vector3(1.4f, 0.6f, 1.6f), 1.2f); yield return Hold(3f, new NPInput.State { aim = true }); rig.SetAim(70f, 2f); yield return Hold(2.5f, new NPInput.State { aim = true });

            Seg("A35 END — tag-out/recovery is shown with REAL rival fire in VIDEO B");
            Chase(); yield return Idle(1.5f);
            label = ""; Status = "A done"; Done = true;
            FreezeHostiles(false);
        }

        // =====================================================================================================
        IEnumerator VideoB()
        {
            Setup();
            hd.BeginRun();
            yield return null;
            CompetitorAI kes = null, van = null, halo = null;
            foreach (var ai in ais) { if (!ai) continue; if (ai.name.Contains("KESTREL")) kes = ai; if (ai.name.Contains("VANTA")) van = ai; if (ai.name.Contains("HALO")) halo = ai; }
            var ph = pm.GetComponent<TagHealth>();

            // B01 appearance: player next to a rival
            FreezeHostiles(true);
            Place(new Vector3(-8f, 0.05f, -12f), 0f);
            van.GetComponent<NavMeshAgent>().Warp(new Vector3(-6.8f, 0f, -12f)); van.transform.rotation = Quaternion.identity;
            Seg("B01 PLAYER (left, CRYPTO / Ch45) vs RIVAL VANTA (right) — what the enemy model actually is", "Rivals use Mixamo Ch15 + SWAT avatar/animations, one shared material; NOT the CRYPTO model");
            Fixed(new Vector3(-7.4f, 1.4f, -7.8f), pm.transform, 1.0f); yield return Idle(3f);
            Orbit(pm.transform, 4.2f, 1.2f, 50f, 0f); yield return Idle(7f);

            // B02 patrol
            Seg("B02 PATROL — rivals walk fixed 4-point loops on the NavMesh (spectator high)", "Player made undetectable for this clip (spawn-protection flag), nothing else changed");
            ph.InvulnerableUntil = Time.time + 9999f;
            FreezeHostiles(false);
            Place(new Vector3(0f, 2.05f, -31f), 0f);
            camFn = () => (new Vector3(-14f, 9f, -18f), van.transform.position + Vector3.up);
            yield return Idle(8f);
            Seg("B03 AI vs AI — two rivals placed face to face 7 m apart", "NOT IMPLEMENTED: all rivals are team 1 and only ever perceive the player. They ignore each other.");
            van.GetComponent<NavMeshAgent>().Warp(new Vector3(-4f, 0f, -12f)); kes.GetComponent<NavMeshAgent>().Warp(new Vector3(-4f, 0f, -5f));
            van.transform.rotation = Quaternion.LookRotation(Vector3.forward); kes.transform.rotation = Quaternion.LookRotation(Vector3.back);
            camFn = () => (new Vector3(4f, 3.5f, -8.5f), (van.transform.position + kes.transform.position) * 0.5f + Vector3.up);
            yield return Idle(9f);

            // B04 rival attacks the player -> tag-out -> respawn
            Seg("B04 RIVAL ENGAGES THE PLAYER (player standing still in the open) — spectator", "Real AI perception, burst fire, paint impacts on CRYPTO");
            ph.InvulnerableUntil = 0f;
            kes.GetComponent<NavMeshAgent>().Warp(new Vector3(-6f, 0f, 25f));
            halo.GetComponent<NavMeshAgent>().Warp(new Vector3(19.5f, 4.5f, -10f));
            Place(new Vector3(-6f, 0.05f, -16f), 0f);
            van.GetComponent<NavMeshAgent>().Warp(new Vector3(-4f, 0f, -4f)); van.transform.rotation = Quaternion.LookRotation(Vector3.back);
            camFn = () => (pm.transform.position + new Vector3(4.5f, 2.2f, -3.5f), (pm.transform.position + van.transform.position) * 0.5f + Vector3.up);
            float w = 0; while (!pm.TaggedOut && w < 25f) { w += Time.deltaTime; yield return null; }
            Seg(pm.TaggedOut ? "B05 TAG-OUT (4 hits in 3 s) — fall animation, then respawn at nearest checkpoint" : "B05 (player was NOT tagged within 25 s)");
            yield return Idle(1.2f);
            Chase(); yield return Idle(4.5f);
            Seg("B06 RESPAWN — gameplay camera (3 s spawn protection, paint on CRYPTO cleared)"); yield return Idle(3f);

            // B07 paint accumulation on a rival
            Seg("B07 PLAYER SHOOTS RIVAL VANTA through several tag-out / recovery cycles — paint on the rival is never cleared", "Watch the rival's body accumulate paint across cycles");
            ph.InvulnerableUntil = Time.time + 9999f;   // keep the take on the rival; he still sees nothing, so he will just patrol
            Place(new Vector3(-6f, 0.05f, -16f), 0f);
            van.GetComponent<NavMeshAgent>().Warp(new Vector3(-6f, 0f, -6f));
            float tt = 0; int lastTags = hd.RivalsTagged;
            while (tt < 34f)
            {
                Vector3 to = van.transform.position + Vector3.up * 1.2f - rig.cam.transform.position;
                rig.SetAim(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
                bool fire = !van.TaggedOut && (tt % 9f) < 3.5f;
                NPInput.UseOverride = true; NPInput.Override = new NPInput.State { aim = true, fire = fire, reloadPressed = (tt % 9f) > 4f && (tt % 9f) < 4.05f };
                camFn = tt < 17f ? null : (System.Func<(Vector3, Vector3)>)(() => (van.transform.position + van.transform.forward * 2.6f + Vector3.up * 1.5f, van.transform.position + Vector3.up * 1.1f));
                if (Mathf.Abs(tt - 17f) < 0.02f) Seg("B08 CLOSE-UP of the rival after repeated cycles (spectator)");
                tt += Time.deltaTime; yield return null;
            }
            NPInput.Override = default;
            Marks.Add($"rivals tagged during B07: {hd.RivalsTagged - lastTags}");

            // B09 drone
            Seg("B09 SENTINEL DRONE — patrol, then the player steps into its scan cone", "Drone alerts and fires paint; 6 hits shut it down for 12 s");
            ph.InvulnerableUntil = 0f;
            FreezeHostiles(true); foreach (var dr in drones) if (dr) dr.enabled = true;
            SentinelDrone drone = drones.Length > 0 ? drones[0] : null;
            Place(new Vector3(-2f, 0.05f, -20f), 0f);
            camFn = () => (pm.transform.position + new Vector3(-5f, 3f, -5f), (drone ? drone.transform.position : pm.transform.position) * 0.5f + pm.transform.position * 0.5f + Vector3.up);
            w = 0; while (w < 14f && !pm.TaggedOut) { w += Time.deltaTime; yield return null; }
            Seg("B10 SHOOTING THE DRONE — gameplay camera");
            Chase();
            tt = 0; while (tt < 8f && drone && drone.Current != SentinelDrone.State.Shutdown)
            {
                Vector3 to = drone.transform.position - rig.cam.transform.position;
                rig.SetAim(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
                NPInput.UseOverride = true; NPInput.Override = new NPInput.State { aim = true, fire = !pm.TaggedOut };
                tt += Time.deltaTime; yield return null;
            }
            NPInput.Override = default;
            Marks.Add($"drone state after B10: {(drone ? drone.Current.ToString() : "none")}");
            yield return Idle(2f);

            // B11 lasers / lockdown
            FreezeHostiles(true);
            Seg("B11 VAULT SCANNER (red laser) — player stands in the beam's path", "Contact does no paint/damage. It triggers LOCKDOWN: alarm, shutters close 8 s, hunters called, vault ring progress reset");
            ph.InvulnerableUntil = Time.time + 9999f;
            Place(new Vector3(-4f, 0.05f, 16f), 0f);
            camFn = () => (new Vector3(9f, 4.5f, 8f), pm.transform.position + Vector3.up);
            var vs = VaultSecurity.Instance; int lc = vs ? vs.LockdownCount : 0;
            w = 0; while (w < 12f && vs && vs.LockdownCount == lc) { w += Time.deltaTime; yield return null; }
            Marks.Add($"lockdown triggered: {(vs && vs.LockdownCount > lc)} after {w:F1}s");
            Seg(vs && vs.LockdownCount > lc ? "B12 LOCKDOWN TRIGGERED — what changes on screen" : "B12 no lockdown in 12 s");
            camFn = () => (new Vector3(6f, 3.2f, 4f), new Vector3(0f, 2.5f, 11.5f)); yield return Idle(4f);
            Chase(); rig.SetAim(180f, 4f); yield return Idle(4f);
            Seg("B13 CROUCHING under the HIGH scanner (1.4 m) — crouched capsule is 1.15 m", "The LOW scanner (0.45 m) cannot be crouched under");
            yield return new WaitForSeconds(5f);
            Place(new Vector3(4f, 0.05f, 25f), 180f); yield return Hold(0.3f, new NPInput.State { crouchPressed = true });
            camFn = () => (pm.transform.position + new Vector3(4f, 1.5f, 2f), pm.transform.position + Vector3.up * 0.6f);
            lc = vs.LockdownCount; float tw = 0;
            while (tw < 10f)
            {
                if (vs.LockdownCount != lc)
                {
                    lc = vs.LockdownCount;
                    var hb = vs.beams[0].beam.position; var lb = vs.beams[1].beam.position;
                    Marks.Add($"lockdown while crouched: player z={pm.transform.position.z:F1} x={pm.transform.position.x:F1} | high beam z={hb.z:F1} | low beam x={lb.x:F1}");
                }
                tw += Time.deltaTime; yield return null;
            }
            yield return Hold(0.3f, new NPInput.State { crouchPressed = true });

            Seg("B14 AI NAVIGATION near walls / doors — rivals resume patrol (spectator)");
            ph.InvulnerableUntil = Time.time + 9999f; FreezeHostiles(false);
            camFn = () => (new Vector3(10f, 6f, 6f), kes.transform.position + Vector3.up);
            yield return Idle(10f);
            ph.InvulnerableUntil = 0f;
            label = ""; Status = "B done"; Done = true;
        }

        // =====================================================================================================
        // Full adventure: the existing DemoRun walks the real routes with the real controls (no teleports), rivals at
        // FULL aggression. When the Finality Gate starts Act II, the QA bike autopilot rides it.
        IEnumerator VideoC()
        {
            Setup();
            Seg("C — FULL ADVENTURE (Editor). QA autopilot walks the real routes with the real controls, no teleports", "Rivals at full aggression. Editor shows the dev HUD; the real web HUD is HTML (see Video E)");
            var demo = DemoRun.Begin(peacefulRivals: false);
            float t = 0;
            while (DemoRun.Running && hd.Current != HeistDirector.Phase.FinalityRun && t < 400f) { t += Time.deltaTime; sub = "DemoRun: " + DemoRun.Status + $" | phase {hd.Current} | relays {hd.RelaysOnline}/3 | player tags {hd.PlayerTags}"; yield return null; }
            if (hd.Current == HeistDirector.Phase.FinalityRun)
            {
                demo.StopAllCoroutines();
                Seg("C — ACT II FINALITY RUN started by the Finality Gate (QA bike autopilot steers)");
                var ap = FinalityRun.Instance.gameObject.AddComponent<BikeAutopilot>();
                t = 0; while (hd.Current == HeistDirector.Phase.FinalityRun && t < 90f) { t += Time.deltaTime; yield return null; }
                Destroy(ap);
            }
            Seg($"C — END: phase {hd.Current}. Finality Elevator: NOT IMPLEMENTED. Result screen is HTML (browser only)", RuntimeErrorMonitor.Summary() + $" | time {(hd.Elapsed + hd.Penalty):F1}s | tags {hd.PlayerTags}");
            Marks.Add("C status: " + DemoRun.Status + " | phase " + hd.Current);
            yield return Idle(4f);
            label = ""; Status = "C done"; Done = true;
        }


        // Take 2: same real routes as DemoRun (walked by PlayerAutopilot, no teleports) but Relay C is solved the way the
        // current vault requires: shoot each moving lock-ring node until it locks (DemoRun predates the ring lock).
        IEnumerator VideoC2()
        {
            Setup();
            var h = hd; var ap = PlayerAutopilot.Ensure();
            Seg("C (take 2) — FULL ADVENTURE (Editor). QA autopilot walks the real routes with the real controls, no teleports", "Rivals at full aggression. Editor shows the dev HUD; the real web HUD is HTML (see Video E)");
            NPInput.UseOverride = true;   // claim input first, like DemoRun (otherwise GameBoot pauses time: cursor not locked)
            HeistDirector.FullRoute = true;
            h.BeginRun();
            yield return new WaitForSeconds(0.6f);
            Relay A = null, B = null, C = null; foreach (var r in h.relays) { if (r.name.EndsWith("A")) A = r; if (r.name.EndsWith("B")) B = r; if (r.name.EndsWith("C")) C = r; }
            System.Func<string> st = () => $"phase {h.Current} | relays {h.RelaysOnline}/3 | player tags {h.PlayerTags} | rivals tagged {h.RivalsTagged} | timer {(h.Elapsed + h.Penalty):F0}s";
            sub = st();
            yield return ap.Run(ap.Look(0f, 2f, 1.2f));
            Seg("C01 ARRIVAL -> HALL"); sub = st();
            yield return ap.Run(ap.GoTo(new Vector3(0, 0, -16f), sprint: true));
            Seg("C02 RELAY A — east stairs to the catwalk"); sub = st();
            yield return ap.Run(ap.GoTo(new Vector3(19.5f, 0, -22.5f), sprint: true));
            yield return ap.Run(ap.GoTo(new Vector3(19.5f, 4.5f, -8f)));
            var iris = A.GetComponent<IrisRelay>();
            float t0 = Time.time;
            if (iris) { while (!A.Online && Time.time - t0 < 20f) { int k = iris.Next; yield return ap.Run(ap.AimFire(() => k < 3 ? iris.pads[k].bounds.center : iris.core.position, 0.25f, settle: 0.6f)); if (k == 3 && iris.Next == 3) yield return new WaitForSeconds(0.3f); } }
            else while (!A.Online && Time.time - t0 < 8f) yield return ap.Run(ap.AimFire(() => A.transform.position, 0.8f));
            Marks.Add($"relay A online={A.Online}");
            Seg("C03 RELAY B — bridge, west stairs, trench (crouched)"); sub = st();
            yield return ap.Run(ap.GoTo(new Vector3(-20.5f, 4.5f, 6f), sprint: true));
            yield return ap.Run(ap.GoTo(new Vector3(-20.5f, 0, -23.5f)));
            yield return ap.Run(ap.GoTo(new Vector3(-14f, 0, -23.2f)));
            yield return ap.Run(ap.GoTo(new Vector3(-14f, -3.5f, 3f), crouch: true));
            t0 = Time.time; while (!B.Online && Time.time - t0 < 8f) yield return ap.Run(ap.AimFire(() => B.transform.position, 0.8f));
            yield return ap.Run(ap.Crouch(false));
            Marks.Add($"relay B online={B.Online}");
            Seg("C04 RELAY C — into the Core Vault, shoot the 3 moving lock-ring nodes"); sub = st();
            yield return ap.Run(ap.GoTo(new Vector3(-13.8f, 0, 22f), sprint: true));
            yield return ap.Run(ap.GoTo(new Vector3(-7.5f, 0f, 17.5f)));
            var rings = FindFirstObjectByType<VaultRings>();
            float tr = 0;
            while (!C.Online && tr < 40f)
            {
                foreach (var ring in rings.rings)
                {
                    float tn = 0;
                    while (ring.node && !ring.node.Locked && tn < 10f && !pm.TaggedOut)
                    {
                        Vector3 np = ring.node.transform.position;
                        np += ring.node.Velocity * ((np - rig.cam.transform.position).magnitude / 78f);
                        Vector3 fc = np - rig.cam.transform.position;
                        float y = Mathf.Atan2(fc.x, fc.z) * Mathf.Rad2Deg, pt = -Mathf.Atan2(fc.y, new Vector2(fc.x, fc.z).magnitude) * Mathf.Rad2Deg;
                        rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, y, 200f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, pt, 160f * Time.deltaTime));
                        NPInput.UseOverride = true; NPInput.Override = new NPInput.State { aim = true, fire = Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, y)) < 2.5f, reloadPressed = pm.GetComponentInChildren<PaintRifle>().Ammo == 0 };
                        tn += Time.deltaTime; tr += Time.deltaTime; sub = st(); yield return null;
                    }
                    NPInput.Override = default;
                    if (pm.TaggedOut) { while (pm.TaggedOut) yield return null; yield return new WaitForSeconds(0.5f); yield return ap.Run(ap.GoTo(new Vector3(-7.5f, 0f, 17.5f), sprint: true)); }
                }
                tr += Time.deltaTime; yield return null;
            }
            Marks.Add($"relay C online={C.Online} after {tr:F0}s, lockdowns {(VaultSecurity.Instance ? VaultSecurity.Instance.LockdownCount : -1)}");
            Seg("C05 CORE — cage lowers, walk onto the dais"); sub = st();
            yield return new WaitForSeconds(2.5f);
            yield return ap.Run(ap.GoTo(new Vector3(0f, 0f, 20.4f), sprint: true));
            float w = 0; while (!h.core.Carried && w < 4f) { w += Time.deltaTime; yield return null; }
            Marks.Add($"core carried={h.core.Carried} phase={h.Current}");
            Seg("C06 ESCAPE — east door, corridor, Finality Gate"); sub = st();
            var legs = new[] { new Vector3(12.5f, 0, 18.5f), new Vector3(20f, 0, 16f), new Vector3(27f, 0, 2f), new Vector3(27f, 0, -14f) };
            for (int attempt = 0; attempt < 4 && h.Current == HeistDirector.Phase.Pursuit || (attempt < 4 && h.Current == HeistDirector.Phase.CoreUnlocked); attempt++)
            {
                if (!h.core.Carried)
                {
                    while (pm.TaggedOut) yield return null;
                    yield return ap.Run(ap.GoTo(h.core.coreVisual.position, sprint: true));
                    w = 0; while (!h.core.Carried && w < 3f) { w += Time.deltaTime; yield return null; }
                    if (!h.core.Carried) continue;
                }
                foreach (var leg in legs) { if (!h.core.Carried || h.Current != HeistDirector.Phase.Pursuit) break; sub = st(); yield return ap.Run(ap.GoTo(leg, sprint: true)); }
                w = 0; while (h.Current == HeistDirector.Phase.Pursuit && w < 2f) { w += Time.deltaTime; yield return null; }
            }
            Marks.Add($"after escape phase={h.Current}");
            if (h.Current == HeistDirector.Phase.FinalityRun)
            {
                Seg("C07 ACT II — FINALITY RUN started by the Finality Gate (QA bike autopilot steers)");
                Destroy(ap.gameObject);   // the on-foot autopilot must stop writing input (it would override the bike driver)
                var bap = FinalityRun.Instance.gameObject.AddComponent<BikeAutopilot>();
                float t = 0; while ((h.Current == HeistDirector.Phase.FinalityRun || h.Current == HeistDirector.Phase.Ascension) && t < 140f) { t += Time.deltaTime; if (h.Current == HeistDirector.Phase.Ascension && !label.StartsWith("C08")) { label = "C08 ACT III — THE FINALITY ELEVATOR"; } sub = st(); yield return null; }
                Destroy(bap);
            }
            Seg($"C09 END — phase {h.Current}. Finality Elevator: NOT IMPLEMENTED. Result screen is HTML (browser only)", RuntimeErrorMonitor.Summary() + " | " + st());
            Marks.Add("C2 end: " + st());
            if (h.LastSignature != null) { System.IO.File.WriteAllText("Recordings/rescue/last_signature.json", "{\"name\":\"CRYPTO QA\",\"timeMs\":" + Mathf.RoundToInt((h.Elapsed + h.Penalty) * 1000f) + ",\"art\":\"" + SignatureRecorder.ToHex(h.LastSignature) + "\",\"shards\":" + h.FinalityShards + ",\"tags\":" + h.PlayerTags + ",\"rivalsTagged\":" + h.RivalsTagged + "}"); Marks.Add("signature bytes " + h.LastSignature.Length); }
            yield return Idle(4f);
            HeistDirector.FullRoute = false; label = ""; Status = "C2 done"; Done = true;
        }


        // Foot-slide metric: horizontal travel of the planted (lowest) foot per second, plus body yaw, per scenario.
        IEnumerator Measure(string name, float sec, NPInput.State st, float yawRate)
        {
            var an = pm.GetComponentInChildren<Animator>();
            Transform lf = an.GetBoneTransform(HumanBodyBones.LeftFoot), rf = an.GetBoneTransform(HumanBodyBones.RightFoot);
            Vector3 pl = lf.position, pr = rf.position; float slide = 0f, turnAnim = 0f, t = 0f, yaw0 = pm.transform.eulerAngles.y, rot = 0f, lastYaw = yaw0;
            NPInput.UseOverride = true; bool first = true;
            while (t < sec)
            {
                var x = st; if (!first) x.jumpPressed = x.crouchPressed = x.reloadPressed = false; first = false;
                NPInput.Override = x;
                rig.SetAim(rig.Yaw + yawRate * Time.deltaTime, rig.Pitch);
                yield return null; t += Time.deltaTime;
                Vector3 l = lf.position, r = rf.position;
                float gy = pm.transform.position.y + 0.14f;   // ankle height when the foot is on the floor
                Vector3 d = Vector3.zero;
                if (l.y < gy && pl.y < gy) { var dl = l - pl; dl.y = 0; d += dl; }
                if (r.y < gy && pr.y < gy) { var dr = r - pr; dr.y = 0; d += dr; }
                // subtract the body's own translation (a foot moving with a moving body is not sliding)
                Vector3 bodyMove = pm.Velocity * Time.deltaTime; bodyMove.y = 0f;
                Vector3 hv = pm.Velocity; hv.y = 0f;
                if (hv.sqrMagnitude < 0.04f) slide += d.magnitude;
                if (an.GetCurrentAnimatorStateInfo(0).IsName("TurnInPlace")) turnAnim += Time.deltaTime;
                pl = l; pr = r;
                float y = pm.transform.eulerAngles.y; rot += Mathf.Abs(Mathf.DeltaAngle(lastYaw, y)); lastYaw = y;
            }
            NPInput.Override = default;
            Marks.Add($"{name}: planted-foot slide {slide / sec * 100f:F1} cm/s, body rotated {rot:F0} deg in {sec:F0}s, turn-in-place anim {turnAnim:F1}s");
        }

        IEnumerator Metrics()
        {
            Setup(); NPInput.UseOverride = true; hd.BeginRun(); yield return null; FreezeHostiles(true);
            var open = new Vector3(0f, 2.05f, -30f);
            foreach (bool legacy in new[] { true, false })
            {
                pm.legacyFacing = legacy; string tag = legacy ? "BEFORE" : "AFTER";
                Place(open, 0f); yield return Idle(1f);
                yield return Measure(tag + " BASELINE idle, camera still", 3f, default, 0f);
                Place(open, 0f); yield return Idle(1f);
                yield return Measure(tag + " idle, camera 45 deg/s", 6f, default, 45f);
                Place(open, 0f); yield return Idle(1f);
                yield return Measure(tag + " idle, camera 180 deg/s", 4f, default, 180f);
                Place(open, 0f); yield return Idle(1f);
                yield return Measure(tag + " idle, camera 20 deg/s (look around)", 4f, default, 20f);
                Place(open, 0f); yield return Idle(1f);
                yield return Measure(tag + " aiming, camera 45 deg/s", 4f, new NPInput.State { aim = true }, 45f);
            }
            pm.legacyFacing = false;
            Status = "M done"; Done = true;
        }


        IEnumerator TurnCompare()
        {
            Setup(); NPInput.UseOverride = true; hd.BeginRun(); yield return null; FreezeHostiles(true);
            var open = new Vector3(0f, 2.05f, -30f);
            foreach (bool legacy in new[] { true, false })
            {
                pm.legacyFacing = legacy; string tag = legacy ? "BEFORE (audit build)" : "AFTER (rescue)";
                Place(open, 0f); WorldOff(new Vector3(2.2f, 1.3f, 2.2f), 0.5f); yield return Idle(0.8f);
                Seg($"{tag}: camera turns 45 deg/s, standing — watch the feet"); yield return Hold(6f, default, 45f);
                Seg($"{tag}: camera turns 180 deg/s"); yield return Hold(3f, default, 180f);
                Seg($"{tag}: camera 90 deg snap left, then right"); rig.SetAim(rig.Yaw - 90f, rig.Pitch); yield return Idle(2f); rig.SetAim(rig.Yaw + 90f, rig.Pitch); yield return Idle(2f);
                Seg($"{tag}: aiming, camera 45 deg/s"); yield return Hold(4f, new NPInput.State { aim = true }, 45f);
                Seg($"{tag}: gameplay camera, slow look-around 30 deg/s"); Chase(); yield return Hold(5f, default, 30f);
            }
            pm.legacyFacing = false; label = ""; Status = "T done"; Done = true;
        }


        // Milestone 1 evidence (founder gate): the exact test list from the rescue directive, continuous, real input path.
        IEnumerator AimAt(CompetitorAI van, float sec, float burst)
        {
            float tt = 0;
            while (tt < sec)
            {
                Vector3 tp = van.transform.position + Vector3.up * 1.15f + van.Velocity * 0.12f;
                Vector3 to = tp - rig.cam.transform.position;
                float y = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, pt = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, y, 140f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, pt, 90f * Time.deltaTime));
                bool onT = Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, y)) < 2f;
                NPInput.Override = new NPInput.State { aim = true, fire = onT && !van.TaggedOut && (tt % 3f) < burst };
                tt += Time.deltaTime; yield return null;
            }
            NPInput.Override = default;
        }

        IEnumerator Milestone1()
        {
            Setup(); NPInput.UseOverride = true; hd.BeginRun(); yield return null; FreezeHostiles(true);
            var ph = pm.GetComponent<TagHealth>(); ph.InvulnerableUntil = Time.time + 9999f;
            var c = new Vector3(0f, 2.05f, -31f);
            Place(c, 0f); yield return Idle(0.6f);
            Seg("M1-1 WALK in a circle (walk key) - spectator high", "Scripted QA input through the real input path. Rivals frozen.");
            WorldOff(new Vector3(0f, 5.5f, -7f), 0.6f); yield return Hold(5f, new NPInput.State { move = new Vector2(0, 1), walk = true }, 60f);
            Seg("M1-1 RUN in a circle - spectator high"); yield return Hold(5f, Mv(0, 1f), 75f);
            Seg("M1-1 RUN in a circle - gameplay camera"); Chase(); yield return Hold(5f, Mv(0, 1f), 75f);
            Place(c, 0f); yield return Idle(0.5f);
            Seg("M1-2 SLOW camera rotation while standing (30 deg/s) - spectator"); WorldOff(new Vector3(2.4f, 1.5f, 2.4f), 0.6f); yield return Hold(6f, default, 30f);
            Seg("M1-2 FAST camera rotation while standing (180 deg/s) - spectator"); yield return Hold(3f, default, 180f); yield return Idle(1.2f);
            Seg("M1-2 Camera rotation - gameplay camera"); Chase(); yield return Hold(4f, default, 45f); yield return Hold(2f, default, -120f); yield return Idle(1f);
            Seg("M1-3 RUN, STOP, REVERSE - side view");
            Place(new Vector3(-7f, 2.05f, -34f), 90f);
            camFn = () => (new Vector3(pm.transform.position.x, 2.9f + 2.05f, -27.5f), pm.transform.position + Vector3.up);
            yield return Hold(1.8f, Mv(0, 1f)); yield return Idle(0.8f); yield return Hold(1.8f, Mv(0, -1f)); yield return Hold(1.2f, Mv(0, 1f)); yield return Hold(1.2f, Mv(0, -1f)); yield return Idle(1f);
            Seg("M1-4 STRAFE left/right + JUMP - front view");
            Place(c, 0f); camFn = () => (pm.transform.position + new Vector3(0.4f, 1.0f, 4.6f), pm.transform.position + Vector3.up * 0.9f);
            yield return Hold(1.6f, Mv(-1, 0)); yield return Hold(1.6f, Mv(1, 0));
            var jj = Mv(1, 0); jj.jumpPressed = true; yield return Hold(1.2f, jj); yield return Hold(0.6f, Mv(-1, 0));
            yield return Hold(1.4f, new NPInput.State { jumpPressed = true }); yield return Idle(0.6f);
            Seg("M1-4 JUMP while running - side view");
            Place(new Vector3(-7f, 2.05f, -34f), 90f); camFn = () => (new Vector3(pm.transform.position.x, 2.8f + 2.05f, -27.5f), pm.transform.position + Vector3.up);
            yield return Hold(0.8f, Mv(0, 1f)); var jr = Mv(0, 1f); jr.jumpPressed = true; yield return Hold(1.3f, jr); yield return Hold(0.8f, Mv(0, 1f)); yield return Idle(0.8f);

            CompetitorAI van = null; foreach (var a in ais) if (a && a.name.Contains("VANTA")) van = a;
            var pA = new GameObject("_M1PatrolA").transform; pA.position = new Vector3(-6.5f, 2.05f, -24.5f);
            var pB = new GameObject("_M1PatrolB").transform; pB.position = new Vector3(6.5f, 2.05f, -24.5f);
            var oldPatrol = van.patrol; van.patrol = new[] { pA, pB };
            var ag = van.GetComponent<UnityEngine.AI.NavMeshAgent>();
            ag.Warp(pA.position); van.enabled = true; ag.isStopped = false; van.ForgetPlayer();
            Place(new Vector3(0f, 2.05f, -33f), 0f); Chase(); yield return Idle(1.5f);
            Seg("M1-5 AIM and HIT a MOVING target (rival walking across) - gameplay camera", "Aim tracks the target with lead like a player would; 3 hits in 4 s tag the rival out");
            int tags0 = hd.RivalsTagged;
            yield return AimAt(van, 9f, 1.4f);
            Marks.Add($"moving-target tags: {hd.RivalsTagged - tags0}");
            Seg("M1-5 Same, spectator side view"); camFn = () => (pm.transform.position + new Vector3(4f, 1.4f, 1.5f), pm.transform.position + Vector3.up * 1.2f + Vector3.forward * 2f);
            yield return AimAt(van, 5f, 1.2f);

            Seg("M1-6 RELOAD - gameplay camera"); Chase(); yield return Hold(0.3f, new NPInput.State { fire = true }); yield return Hold(2.2f, new NPInput.State { reloadPressed = true });
            Seg("M1-6 RELOAD - front view"); camFn = () => (pm.transform.position + pm.transform.rotation * new Vector3(0.2f, 1.35f, 1.7f), pm.transform.position + Vector3.up * 1.15f);
            yield return Hold(0.3f, new NPInput.State { fire = true }); yield return Hold(2.2f, new NPInput.State { reloadPressed = true });
            Seg("M1-6 RELOAD - left side view, slow motion x0.35"); camFn = () => (pm.transform.position + pm.transform.rotation * new Vector3(-1.7f, 1.3f, 0.3f), pm.transform.position + Vector3.up * 1.15f);
            slow = 0.35f; yield return Hold(0.2f, new NPInput.State { fire = true }); yield return Hold(1.8f, new NPInput.State { reloadPressed = true }); slow = 1f; Time.timeScale = 1f;
            Seg("M1-6 RELOAD - right side view"); camFn = () => (pm.transform.position + pm.transform.rotation * new Vector3(1.7f, 1.3f, 0.3f), pm.transform.position + Vector3.up * 1.15f);
            yield return Hold(0.3f, new NPInput.State { fire = true }); yield return Hold(2.2f, new NPInput.State { reloadPressed = true });

            Seg("M1-7 TAG-OUT and RECOVERY - the rival now sees the player and fires (real AI)");
            Place(new Vector3(0f, 2.05f, -33f), 0f); ph.InvulnerableUntil = 0f;
            ag.Warp(new Vector3(0f, 2.05f, -25f)); van.transform.rotation = Quaternion.LookRotation(Vector3.back);
            camFn = () => (pm.transform.position + new Vector3(4.2f, 2.0f, -2.5f), Vector3.Lerp(pm.transform.position, van.transform.position, 0.35f) + Vector3.up * 1.1f);
            yield return Idle(0.5f);
            rig.SetAim(0f, 25f); yield return Hold(0.6f, new NPInput.State { fire = true });   // shots into the floor: real noise, the rival hears it
            float w = 0; while (!pm.TaggedOut && w < 20f) { w += Time.deltaTime; yield return null; }
            Marks.Add($"player tagged: {pm.TaggedOut} after {w:F1}s");
            yield return Idle(2.5f);
            Seg("M1-7 RECOVERY - respawn at the nearest checkpoint, gameplay camera"); Chase();
            van.enabled = false; ag.isStopped = true;
            yield return Idle(4f);
            yield return Hold(1.5f, Mv(0, 1f)); yield return Idle(1f);
            van.patrol = oldPatrol; Destroy(pA.gameObject); Destroy(pB.gameObject);
            label = ""; Status = "M1 done"; Done = true;
        }


        // Milestone 2 evidence: rivals built on CRYPTO, two factions that fight each other (player out of the fight).
        IEnumerator Factions()
        {
            Setup(); NPInput.UseOverride = true; hd.BeginRun(); yield return null; FreezeHostiles(true);
            var ph = pm.GetComponent<TagHealth>(); ph.InvulnerableUntil = Time.time + 9999f;
            CompetitorAI kes = null, van = null, halo = null, orb = null;
            foreach (var a in FindObjectsByType<CompetitorAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { if (a.name.Contains("KESTREL")) kes = a; if (a.name.Contains("VANTA")) van = a; if (a.name.Contains("HALO")) halo = a; if (a.name.Contains("ORBIT")) orb = a; }
            ais = new[] { kes, van, halo, orb };
            // line-up on the arrival platform
            Place(new Vector3(0f, 2.05f, -31f), 0f);
            void Put(CompetitorAI a, Vector3 p, float yaw) { var ag = a.GetComponent<UnityEngine.AI.NavMeshAgent>(); ag.Warp(p); a.transform.rotation = Quaternion.Euler(0, yaw, 0); }
            Put(kes, new Vector3(-2.4f, 2.05f, -31f), 0f); Put(van, new Vector3(-1.2f, 2.05f, -31f), 0f);
            Put(halo, new Vector3(1.2f, 2.05f, -31f), 0f); Put(orb, new Vector3(2.4f, 2.05f, -31f), 0f);
            Seg("F1 ALL HUMAN RIVALS NOW USE CRYPTO — VECTOR crew (crimson) · CRYPTO (player, cyan) · HALO syndicate (gold)", "Same model, animator, rifle and IK as the player; faction suit materials");
            camFn = () => (new Vector3(0f, 3.4f, -25.5f), new Vector3(0f, 3.05f, -31f)); yield return Idle(4f);
            Orbit(pm.transform, 5.5f, 0.6f, 40f, 0f, 1.0f); yield return Idle(6f);

            // faction fight in the main hall: VECTOR pair vs HALO pair, 14 m apart, player away and undetectable
            Place(new Vector3(0f, 2.05f, -36f), 0f);
            Put(kes, new Vector3(-6f, 0f, -12f), 0f); Put(van, new Vector3(-3f, 0f, -12f), 0f);
            Put(halo, new Vector3(-6f, 0f, 3f), 180f); Put(orb, new Vector3(-3f, 0f, 3f), 180f);
            FreezeHostiles(false);
            int tags0 = hd.RivalsTagged;
            Seg("F2 FACTION vs FACTION — VECTOR (crimson) and HALO (gold) spot each other and fight (real AI)", "Player is out of the fight for this clip; nothing is scripted besides the start positions");
            camFn = () => (new Vector3(8f, 6.5f, -4.5f), (kes.transform.position + halo.transform.position) * 0.5f + Vector3.up);
            float t = 0; while (t < 22f) { t += Time.deltaTime; sub = $"VECTOR: {State(kes)} / {State(van)}   HALO: {State(halo)} / {State(orb)}   rival tag-outs: {hd.RivalsTagged - tags0}"; yield return null; }
            Seg("F3 CLOSE VIEW of the fight"); camFn = () => (kes.transform.position + new Vector3(3.5f, 1.8f, -3.5f), (kes.transform.position + halo.transform.position) * 0.5f + Vector3.up);
            t = 0; while (t < 12f) { t += Time.deltaTime; sub = $"VECTOR: {State(kes)} / {State(van)}   HALO: {State(halo)} / {State(orb)}   rival tag-outs: {hd.RivalsTagged - tags0}"; yield return null; }
            Marks.Add($"faction fight: rival tag-outs {hd.RivalsTagged - tags0}; states {State(kes)} {State(van)} {State(halo)} {State(orb)}");
            label = ""; Status = "F done"; Done = true;
        }
        static string State(CompetitorAI a) => a ? a.Current + (a.Target ? ">" + a.Target.name.Replace("Rival_", "") : "") : "-";


        // Debug: walk the west route (bridge -> west stairs -> trench) and log the player's position.
        IEnumerator PathProbe()
        {
            Setup(); NPInput.UseOverride = true; hd.BeginRun(); yield return null; FreezeHostiles(true);
            pm.GetComponent<TagHealth>().InvulnerableUntil = Time.time + 9999f;
            var ap = PlayerAutopilot.Ensure();
            Place(new Vector3(19.5f, 4.55f, -8f), 0f); yield return Idle(0.5f);
            foreach (var target in new[] { new Vector3(-20.5f, 4.5f, 6f), new Vector3(-20.5f, 0, -23.5f), new Vector3(-14f, 0, -23.2f), new Vector3(-14f, -3.5f, 3f) })
            {
                var c = ap.Run(ap.GoTo(target, sprint: true));
                float t = 0; while (ap.Busy && t < 25f) { t += 0.5f; Marks.Add($"->{target}: pos {pm.transform.position:F1} grounded {pm.Grounded}"); yield return new WaitForSeconds(0.5f); }
            }
            Status = "P done"; Done = true;
        }


        // Gate 1 check: the Iris relay sequence with aimed single shots (wrong pad first, then 1-2-3, then the core).
        IEnumerator IrisTest()
        {
            Setup(); NPInput.UseOverride = true; hd.BeginRun(); yield return null; FreezeHostiles(true);
            pm.GetComponent<TagHealth>().InvulnerableUntil = Time.time + 9999f;
            var iris = FindFirstObjectByType<IrisRelay>();
            Place(new Vector3(18.6f, 4.55f, -2.6f), 90f, 0f); Chase(); yield return Idle(1f);
            IEnumerator ShootAt(Vector3 p, float hold)
            {
                for (float t = 0; t < 0.5f; t += Time.deltaTime)
                {
                    Vector3 d = p - rig.cam.transform.position;
                    rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 180f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, 120f * Time.deltaTime));
                    NPInput.Override = new NPInput.State { aim = true }; yield return null;
                }
                for (float t = 0; t < hold; t += Time.deltaTime) { NPInput.Override = new NPInput.State { aim = true, fire = t < 0.12f }; yield return null; }
                NPInput.Override = new NPInput.State { aim = true };
            }
            Seg("G1 IRIS: wrong pad first (3) -> quiet reject", $"next={iris.Next}");
            yield return ShootAt(iris.pads[2].bounds.center, 0.9f); Marks.Add($"after wrong pad: next={iris.Next}");
            for (int k = 0; k < 3; k++) { Seg($"G1 IRIS: pad {k + 1}"); yield return ShootAt(iris.pads[k].bounds.center, 0.9f); Marks.Add($"after pad {k + 1}: next={iris.Next}"); }
            Seg("G1 IRIS: core exposed"); yield return Hold(1.2f, new NPInput.State { aim = true });
            yield return ShootAt(iris.core.position, 1.2f); Marks.Add($"after core: next={iris.Next} relayOnline={iris.relay.Online} relays={hd.RelaysOnline}");
            Seg("G1 IRIS: relay online, power pulse to the reactor"); WorldOff(new Vector3(-6f, 3f, -8f), 1f);
            yield return Idle(3.5f);
            Status = "I done"; Done = true; label = "";
        }


        // Gate 1: stride calibration. Compare the blended clip's own root speed (Animator.velocity) with the character's
        // measured displacement for each gait. Ratio ~1 means the feet travel with the ground (no sliding).
        IEnumerator StrideCal()
        {
            Setup(); NPInput.UseOverride = true; hd.BeginRun(); yield return null; FreezeHostiles(true);
            pm.GetComponent<TagHealth>().InvulnerableUntil = Time.time + 9999f;
            var an = pm.GetComponentInChildren<Animator>();
            Marks.Add($"humanScale={an.humanScale:F3}");
            var tests = new (string n, NPInput.State st, float yaw)[] {
                ("walk fwd", new NPInput.State { move = new Vector2(0, 1), walk = true }, 90f), ("run fwd", Mv(0, 1), 90f), ("sprint", Mv(0, 1, sprint: true), 90f),
                ("run back", Mv(0, -1), -90f), ("strafe R", Mv(1, 0), 0f), ("strafe L", Mv(-1, 0), 180f), ("diag FR", Mv(0.7f, 0.7f), 45f), ("crouch fwd", new NPInput.State { move = new Vector2(0, 1) }, 90f) };
            foreach (var (n, st, yaw) in tests)
            {
                Place(new Vector3(-8f, 2.05f, -31f), yaw); yield return Idle(0.4f);
                if (n == "crouch fwd") yield return Hold(0.3f, new NPInput.State { crouchPressed = true });
                float t = 0, sa = 0, sm = 0; int k = 0;
                NPInput.Override = st;
                while (t < 2.4f) { NPInput.Override = st; yield return null; t += Time.deltaTime; if (t > 0.8f) { var av = an.velocity; av.y = 0; var mv = pm.Velocity; mv.y = 0; sa += av.magnitude; sm += mv.magnitude; k++; } }
                NPInput.Override = default;
                if (n == "crouch fwd") yield return Hold(0.3f, new NPInput.State { crouchPressed = true });
                Marks.Add($"{n}: clip root speed {sa / k:F2} m/s vs actual {sm / k:F2} m/s -> ratio {(sa / Mathf.Max(0.01f, sm)):F2}");
            }
            // wall stop: run into the arrival south wall and confirm measured speed drops to ~0
            Place(new Vector3(0f, 2.05f, -32.5f), 180f); yield return Idle(0.3f);
            float w = 0; while (w < 1.6f) { NPInput.Override = Mv(0, 1); yield return null; w += Time.deltaTime; }
            var vv = pm.Velocity; vv.y = 0; var cv = pm.CommandedVelocity; cv.y = 0;
            Marks.Add($"wall stop: measured {vv.magnitude:F2} m/s, commanded {cv.magnitude:F2} m/s, anim MoveZ {an.GetFloat("MoveZ"):F2}");
            NPInput.Override = default;
            Status = "K done"; Done = true;
        }


        // Gate 1: ONE continuous take of the first minute in the gameplay camera. Scripted QA input through the real
        // input path (NOT a human player): arrival -> look -> strafe -> route to the east stair -> catwalk -> Iris pad 1
        // (teaching shot) -> HALO acquires (laser + tone) -> evade + return fire -> reload -> pads 2, 3 -> core -> pulse.
        IEnumerator FirstMinute()
        {
            Setup(); NPInput.UseOverride = true; hd.BeginRun(); yield return null;
            var iris = FindFirstObjectByType<IrisRelay>();
            CompetitorAI halo = null; foreach (var a in ais) if (a && a.name.Contains("HALO")) halo = a;
            var ph = pm.GetComponent<TagHealth>();
            var ap = PlayerAutopilot.Ensure(); ap.fightBack = false;
            int playerHits = 0; System.Action<PaintHit> onHit = _ => playerHits++; ph.Hit += onHit;
            Chase();
            Seg("FIRST MINUTE (scripted QA input, real input path, gameplay camera, one take)", "");
            IEnumerator Go(Vector3 p, bool sprint) { ap.Run(ap.GoTo(p, sprint)); yield return null; float w = 0; while (ap.Busy && w < 30f) { w += Time.deltaTime; yield return null; } }
            IEnumerator Turn(float yaw, float pitch, float sec) { ap.Run(ap.Look(yaw, pitch, sec)); yield return null; while (ap.Busy) yield return null; }
            IEnumerator Shoot(System.Func<Vector3> p, float settle, float fireFor)
            {   // aim like a player: ease onto the point, then one short squeeze
                float t = 0;
                while (t < settle + fireFor)
                {
                    Vector3 d = p() - rig.cam.transform.position;
                    float y = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pt = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                    rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, y, 160f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, pt, 100f * Time.deltaTime));
                    NPInput.Override = new NPInput.State { aim = true, fire = t > settle && Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, y)) < 1.5f };
                    t += Time.deltaTime; yield return null;
                }
                NPInput.Override = new NPInput.State { aim = true };
            }
            // 1) arrival: settle, look toward the relay beacon, a few steps of free movement
            yield return Idle(1.5f);
            yield return Turn(37f, -8f, 2.0f); yield return Idle(0.8f); yield return Turn(15f, 2f, 1.2f);
            yield return Hold(1.0f, Mv(-1f, 0.2f)); yield return Hold(1.0f, Mv(1f, 0.2f)); yield return Hold(0.8f, Mv(0, 1f));
            Marks.Add($"{Time.time - t0:F1}s arrival done at {pm.transform.position:F1}");
            // 2) route: hall (run) -> east stair base (sprint) -> look up -> stair top -> catwalk shooting spot
            yield return Go(new Vector3(9f, 0f, -19f), false);
            yield return Go(new Vector3(18.6f, 0f, -21.2f), true);
            yield return Turn(10f, -22f, 1.4f); yield return Idle(0.5f);
            yield return Go(new Vector3(19.5f, 4.55f, -10.5f), false);
            yield return Go(new Vector3(18.5f, 4.55f, -4.2f), false);
            Marks.Add($"{Time.time - t0:F1}s at catwalk {pm.transform.position:F1}");
            // 3) teaching shot: pad 1 (look first, then one squeeze)
            yield return Shoot(() => iris.pads[0].bounds.center, 1.1f, 0.35f);
            Marks.Add($"{Time.time - t0:F1}s pad1 next={iris.Next}");
            yield return Hold(0.6f, new NPInput.State { aim = true });
            // 4) HALO acquires (laser + lock tone). Turn to the threat, evade the first burst, then trade shots with
            //    human-like aim error (shots can miss); HALO breaks to cover when hit and peeks back.
            float acq = 0; while (halo && halo.Current != CompetitorAI.State.Engage && acq < 4f) { acq += Time.deltaTime; NPInput.Override = new NPInput.State { aim = true }; yield return null; }
            Marks.Add($"{Time.time - t0:F1}s HALO state {halo?.Current} target {(halo && halo.Target ? halo.Target.name : "-")} after {acq:F1}s");
            yield return Shoot(() => halo.transform.position + Vector3.up * 1.3f, 0.35f, 0f);
            yield return Hold(1.1f, new NPInput.State { move = new Vector2(-1f, -0.3f), aim = true });           // evade the burst
            float ft = 0;
            while (halo && !halo.TaggedOut && ft < 14f && !pm.TaggedOut)
            {
                Vector3 err = new Vector3(Mathf.Sin(ft * 3.1f), Mathf.Sin(ft * 4.3f + 1f) * 0.6f, Mathf.Cos(ft * 2.7f)) * Mathf.Lerp(0.55f, 0.15f, Mathf.Clamp01(ft / 3f));
                Vector3 tp = halo.transform.position + Vector3.up * 1.2f + halo.Velocity * 0.1f + err, d = tp - rig.cam.transform.position;
                float y = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pt = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, y, 110f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, pt, 80f * Time.deltaTime));
                bool onT = Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, y)) < 2.5f;
                float cyc = ft % 2.2f; bool firing = cyc < 0.9f;                        // squeeze, then reposition
                float strafe = firing ? 0f : (Mathf.Sin(ft * 1.3f) > 0 ? 0.8f : -0.8f);
                NPInput.Override = new NPInput.State { aim = true, fire = onT && firing, move = new Vector2(strafe, 0f) };
                ft += Time.deltaTime; yield return null;
            }
            Marks.Add($"{Time.time - t0:F1}s HALO tagged={halo?.TaggedOut} after {ft:F1}s; player hits pending {ph.Hits}; player tagged={pm.TaggedOut}");
            if (pm.TaggedOut) { while (pm.TaggedOut) yield return null; yield return Idle(1.5f); yield return Go(new Vector3(18.5f, 4.55f, -4.2f), false); }
            yield return Hold(0.8f, new NPInput.State { aim = true });
            // 5) reload in the recovery window
            yield return Hold(1.9f, new NPInput.State { reloadPressed = true });
            // 6) finish the relay: pads 2, 3, then the exposed core
            yield return Shoot(() => iris.pads[1].bounds.center, 0.7f, 0.35f); Marks.Add($"{Time.time - t0:F1}s pad2 next={iris.Next}");
            yield return Shoot(() => iris.pads[2].bounds.center, 0.6f, 0.35f); Marks.Add($"{Time.time - t0:F1}s pad3 next={iris.Next}");
            yield return Hold(0.9f, new NPInput.State { aim = true });
            yield return Shoot(() => iris.core.position, 0.5f, 0.35f);
            Marks.Add($"{Time.time - t0:F1}s core next={iris.Next} relayOnline={iris.relay.Online} relays={hd.RelaysOnline}");
            // 7) follow the power pulse toward the reactor, then head off to the next objective
            yield return Hold(0.6f, default);
            Vector3 rd = new Vector3(0f, 3.5f, -4f) - pm.transform.position;
            yield return Turn(Mathf.Atan2(rd.x, rd.z) * Mathf.Rad2Deg, 6f, 3.0f);
            yield return Idle(2.0f);
            // 8) the 8 s breather: walk back along the catwalk to the stair head and look over the hall (the next relay)
            yield return Go(new Vector3(19.5f, 4.55f, -10.5f), false);
            yield return Turn(-115f, 12f, 1.6f); yield return Idle(1.2f);
            Marks.Add($"{Time.time - t0:F1}s END. phase {hd.Current} relays {hd.RelaysOnline}. {RuntimeErrorMonitor.Summary()}");
            Marks.Add($"player hits taken over the take: {playerHits}; rival tag-outs by player {hd.RivalsTagged}");
            ph.Hit -= onHit; ap.fightBack = true; label = ""; Status = "G done"; Done = true;
        }


        // Featured judge route (rules v2): Relay A (Iris) -> Core -> Finality Gate -> short escape -> extracted.
        // QA autopilot through the real input path; measures the route duration.
        IEnumerator JudgeRoute()
        {
            Setup(); var h = hd; var ap = PlayerAutopilot.Ensure();
            NPInput.UseOverride = true; HeistDirector.FullRoute = false; h.BeginRun();
            yield return new WaitForSeconds(0.6f);
            Seg("J — FEATURED ROUTE (scripted QA input)", "");
            var iris = FindFirstObjectByType<IrisRelay>(); Relay A = iris.relay; var O = new Vector3(-300f, 0f, 0f);
            // encounter reset: corridor -> breach -> peek (autopilot fights back like a player) -> around the reactor -> Iris -> core door
            foreach (var wp in new[] { new Vector3(6f, 0.1f, 4.5f), new Vector3(6f, 0.1f, 9.3f), new Vector3(5.8f, 0.1f, 11.8f) }) yield return ap.Run(ap.GoTo(O + wp));
            { float wt = 0; CompetitorAI g = null; foreach (var x in ais) if (x && x.name.Contains("HALO")) g = x; while (g && !g.TaggedOut && wt < 12f) { wt += 0.5f; yield return ap.Run(ap.AimFire(() => g.transform.position + Vector3.up * 1.25f, 0.5f, settle: 0.3f)); } }
            foreach (var wp in new[] { new Vector3(8.5f, 0.1f, 11.8f), new Vector3(8.5f, 0.1f, 16f), new Vector3(6f, 0.1f, 17f), new Vector3(2.8f, 0.1f, 19.3f), new Vector3(-3f, 0.1f, 21f), new Vector3(-5.5f, 0.1f, 21.4f) }) yield return ap.Run(ap.GoTo(O + wp));
            float t0 = Time.time;
            while (!A.Online && Time.time - t0 < 25f) { int k = iris.Next; yield return ap.Run(ap.AimFire(() => k < 3 ? iris.pads[k].bounds.center : iris.core.position, 0.25f, settle: 0.6f)); if (k == 3 && iris.Next == 3) yield return new WaitForSeconds(0.3f); }
            Marks.Add($"{Time.time - t0s():F1}s relay A online={A.Online} phase={h.Current}");
            var encounter=PaintballEncounter.Instance;
            if(encounter && encounter.Required) {
                Marks.Add($"combat gate before clear: count={encounter.Cleared}/{encounter.Total}, doorOpen={FindFirstObjectByType<RoomExit>().Open}, coreUnlocked={h.core.Unlocked}");
                float combatStart=Time.time;
                while(!encounter.Complete && Time.time-combatStart<80f) {
                    foreach(var g in encounter.guards) {
                        if(!g || g.TaggedOut) continue;
                        if(pm.TaggedOut) { yield return new WaitForSeconds(1f); continue; }
                        Vector3 p=g.transform.position;
                        // Move around the solid reactor via NavMesh corners. Aim and
                        // fire through the same input path as normal mouse controls.
                        Vector3 approach=p+Vector3.back*4f;
                        yield return ap.Run(ap.GoTo(approach));
                        yield return ap.Run(ap.AimFire(()=>g.transform.position+Vector3.up*1.35f,2.4f,settle:0.4f));
                    }
                }
                Marks.Add($"{Time.time-t0s():F1}s combat clear={encounter.Cleared}/{encounter.Total} playerTags={h.PlayerTags}");
            }
            yield return new WaitForSeconds(1.2f);
            yield return ap.Run(ap.GoTo(O + new Vector3(-2f, 0.1f, 26.4f))); yield return ap.Run(ap.GoTo(O + new Vector3(0f, 0.1f, 26.6f)));
            { rig.SetAim(0f, 0f); float w0 = 0; while (pm.transform.position.x < -100f && w0 < 3f) { NPInput.Override = Mv(0, 1f); w0 += Time.deltaTime; yield return null; } NPInput.Override = default; }
            Marks.Add($"{Time.time - t0s():F1}s through the exit: at {pm.transform.position:F1}");
            yield return ap.Run(ap.GoTo(new Vector3(0f, 0f, 20.4f), sprint: true));
            float w = 0; while (!h.core.Carried && w < 4f) { w += Time.deltaTime; yield return null; }
            Marks.Add($"{Time.time - t0s():F1}s core carried={h.core.Carried} phase={h.Current}");
            var legs = new[] { new Vector3(12.5f, 0, 18.5f), new Vector3(20f, 0, 16f), new Vector3(27f, 0, 2f), new Vector3(27f, 0, -14f) };
            for (int attempt = 0; attempt < 4 && (h.Current == HeistDirector.Phase.Pursuit || h.Current == HeistDirector.Phase.CoreUnlocked); attempt++)
            {
                if (!h.core.Carried) { while (pm.TaggedOut) yield return null; yield return ap.Run(ap.GoTo(h.core.coreVisual.position, sprint: true)); w = 0; while (!h.core.Carried && w < 3f) { w += Time.deltaTime; yield return null; } if (!h.core.Carried) continue; }
                foreach (var leg in legs) { if (!h.core.Carried || h.Current != HeistDirector.Phase.Pursuit) break; float lt = Time.time; int tg = h.PlayerTags; { var np = new UnityEngine.AI.NavMeshPath(); UnityEngine.AI.NavMesh.CalculatePath(pm.transform.position, leg, UnityEngine.AI.NavMesh.AllAreas, np); string cs = ""; foreach (var c in np.corners) cs += " " + c.ToString("F0"); Marks.Add($"  path from {pm.transform.position:F1} to {leg}: {np.status}{cs}"); } ap.Run(ap.GoTo(leg, sprint: true)); yield return null; while (ap.Busy && h.Current == HeistDirector.Phase.Pursuit) yield return null; if (ap.Busy) ap.Cancel(); Marks.Add($"  leg {leg} took {Time.time - lt:F1}s at {pm.transform.position:F1} carried={h.core.Carried} tagsNow={h.PlayerTags - tg}"); }
                w = 0; while (h.Current == HeistDirector.Phase.Pursuit && w < 2f) { w += Time.deltaTime; yield return null; }
                Marks.Add($"  attempt {attempt} phase={h.Current} carried={h.core.Carried}");
            }
            Marks.Add($"{Time.time - t0s():F1}s on-foot end phase={h.Current}");
            float rideStart = Time.time;
            if (h.Current == HeistDirector.Phase.FinalityRun)
            {
                Destroy(ap.gameObject);
                var bap = FinalityRun.Instance.gameObject.AddComponent<BikeAutopilot>();
                var bk = FinalityRun.Instance.bike; float s0 = bk.S; string gates = ""; foreach (var e in FinalityRun.Instance.Elements) if (e.kind == TrackElement.Kind.Gate) gates += e.s.ToString("F0") + " ";
                Marks.Add($"  ride start s={s0:F0} track length {bk.track.Length:F0} gate element(s) at {gates}");
                float t = 0, maxS = 0; while (h.Current == HeistDirector.Phase.FinalityRun && t < 120f) { t += Time.deltaTime; maxS = Mathf.Max(maxS, bk.Speed); yield return null; }
                Marks.Add($"  ride end s={bk.S:F0} after {t:F1}s, top speed {maxS:F0} m/s");
                Marks.Add($"{Time.time - t0s():F1}s ride took {Time.time - rideStart:F1}s, shards {FinalityRun.Instance.Shards}, crashes {FinalityRun.Instance.Crashes}, phase={h.Current}");
                while (h.Current == HeistDirector.Phase.Ascension) yield return null;
                Destroy(bap);
            }
            Marks.Add($"J end: phase {h.Current}, heist timer {(h.Elapsed + h.Penalty):F1}s, player tags {h.PlayerTags}, rivals tagged {h.RivalsTagged}. {RuntimeErrorMonitor.Summary()}");
            label = ""; Status = "J done"; Done = true;
        }
        float t0s() => t0;


        // Rig diagnosis (rescue order §1): CRYPTO puppet next to the original SWAT rig, same clips, same driven values,
        // flat evenly lit room, in place. CRYPTO's procedural layers are added one at a time; SWAT never has them.
        IEnumerator RigDiag()
        {
            Setup(); NPInput.UseOverride = true;
            var model = pm.GetComponentInChildren<Animator>().gameObject;
            Vector3 c = new Vector3(-601f, 0.05f, 9f);
            pm.Teleport(new Vector3(-608f, 0.05f, 3f), 0f);
            var hold = new GameObject("_RigDiagHold"); hold.SetActive(false);
            var cryptoClone = Instantiate(model, hold.transform);
            foreach (var mb in cryptoClone.GetComponentsInChildren<MonoBehaviour>(true)) if (!(mb is CharacterAnimator)) Destroy(mb);
            var cryptoRoot = new GameObject("_Puppet_CRYPTO"); var cs = cryptoRoot.AddComponent<PuppetSource>();
            cryptoRoot.transform.SetPositionAndRotation(c + new Vector3(-1.1f, 0, 0), Quaternion.Euler(0, 200f, 0));
            yield return null;
            cryptoClone.transform.SetParent(cryptoRoot.transform, false); cryptoClone.transform.localPosition = Vector3.zero; cryptoClone.transform.localRotation = Quaternion.identity;
            var ca = cryptoClone.GetComponent<CharacterAnimator>(); var canim = cryptoClone.GetComponent<Animator>();
            Destroy(hold); ca.SetSource(cs);
            GameObject swat = null;
#if UNITY_EDITOR
            var swatAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NeonProtocol/Art/Characters/Swat/Swat.fbx");
            swat = Instantiate(swatAsset); swat.name = "_Puppet_SWAT_control";
            swat.transform.SetPositionAndRotation(c + new Vector3(1.1f, 0, 0), Quaternion.Euler(0, 200f, 0));
            var sa = swat.GetComponent<Animator>() ?? swat.AddComponent<Animator>(); sa.runtimeAnimatorController = canim.runtimeAnimatorController; sa.applyRootMotion = false;   // in place, like the CRYPTO puppet
            var mir = swat.AddComponent<AnimatorParamMirror>(); mir.from = canim; mir.to = sa;
#endif
            camFn = () => (c + new Vector3(0.6f, 1.25f, -4.6f), c + new Vector3(0f, 0.95f, 0f)); spec.fieldOfView = 40f;
            var fill = new GameObject("_RigDiagFill").AddComponent<Light>(); fill.type = LightType.Point; fill.range = 9f; fill.intensity = 3.5f; fill.color = new Color(1f, 0.96f, 0.9f); fill.transform.position = c + new Vector3(0.4f, 2.2f, -3f);
            var gaits = new (string n, Vector3 v, bool sprint, int aim, float turn)[] {
                ("IDLE", Vector3.zero, false, 0, 0), ("WALK FWD", new Vector3(0, 0, 1.72f), false, 0, 0), ("RUN FWD", new Vector3(0, 0, 4.5f), false, 0, 0),
                ("SPRINT", new Vector3(0, 0, 7.2f), true, 0, 0), ("RUN BACK", new Vector3(0, 0, -3.5f), false, 0, 0), ("STRAFE LEFT", new Vector3(-3.5f, 0, 0), false, 0, 0),
                ("STRAFE RIGHT", new Vector3(3.5f, 0, 0), false, 0, 0), ("DIAGONAL FWD-RIGHT", new Vector3(2.5f, 0, 2.5f), false, 0, 0),
                ("TURN 90 LEFT", Vector3.zero, false, 0, -1f), ("TURN 90 RIGHT", Vector3.zero, false, 0, 1f), ("AIM IDLE", Vector3.zero, false, 1, 0), ("AIM WALK", new Vector3(0, 0, 1.72f), false, 1, 0) };
            string[] layerNames = { "CLIPS ONLY (no procedural layer)", "+ FINGER GRIP", "+ SPINE AIM", "+ RIGHT-ARM IK (rifle)", "+ SUPPORT HAND (full current stack)" };
            for (int L = 0; L <= 4; L++)
            {
                ca.maxLayer = L;
                foreach (var g in gaits)
                {
                    Seg($"RIG DIAG · CRYPTO (left) vs SWAT control (right) · {layerNames[L]}", g.n + " · same clips, same driven values, in place");
                    cs.velocityLocal = g.v; cs.sprinting = g.sprint; cs.aimMode = g.aim; cs.combat = g.aim > 0; cs.turning = g.turn != 0; cs.turnRate = g.turn;
                    cryptoRoot.transform.rotation = Quaternion.Euler(0, 200f, 0); if (swat) swat.transform.rotation = Quaternion.Euler(0, 200f, 0);
                    yield return new WaitForSeconds(g.turn != 0 ? 2.0f : 1.9f);
                }
            }
            cs.velocityLocal = Vector3.zero; cs.turning = false; cs.aimMode = 0;
            Destroy(cryptoRoot); if (swat) Destroy(swat); Destroy(fill.gameObject);
            label = ""; Status = "R done"; Done = true;
        }


        // R2: the Relay Room first minute (Quaternius room). Scripted QA input through the real input path.
        // arrival -> look -> strafe -> aisle -> pad 1 (teaching shot) -> HALO warns + engages -> evade/trade -> reload
        // -> pads 2, 3 -> core -> exit opens -> walk through it.
        IEnumerator RoomMinuteFP() { var r = FindFirstObjectByType<ShoulderRig>(); r.firstPersonTrial = true; yield return RoomMinute(); r.firstPersonTrial = false; }
        IEnumerator RoomMinute()
        {
            Setup(); NPInput.UseOverride = true; HeistDirector.FullRoute = false; hd.BeginRun(); yield return null;
            var O = new Vector3(-300f, 0f, 0f);
            var iris = FindFirstObjectByType<IrisRelay>();
            CompetitorAI halo = null; foreach (var a in ais) if (a && a.name.Contains("HALO")) halo = a;
            var ph = pm.GetComponent<TagHealth>(); int hits = 0; System.Action<PaintHit> onHit = _ => hits++; ph.Hit += onHit;
            var ap = PlayerAutopilot.Ensure(); ap.fightBack = false;
            Chase(); Seg("RELAY ROOM · " + (rig.firstPersonTrial ? "FIRST PERSON" : "THIRD PERSON") + " (scripted QA input, real input path)", "");
            IEnumerator Go(Vector3 p, bool sprint) { ap.Run(ap.GoTo(p, sprint)); yield return null; float w = 0; while (ap.Busy && w < 30f) { w += Time.deltaTime; yield return null; } }
            IEnumerator Turn(float yaw, float pitch, float sec) { ap.Run(ap.Look(yaw, pitch, sec)); yield return null; while (ap.Busy) yield return null; }
            IEnumerator Shoot(System.Func<Vector3> p, float settle, float fireFor)
            {
                float t = 0;
                while (t < settle + fireFor)
                {
                    Vector3 d = p() - rig.cam.transform.position;
                    float y = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pt = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                    rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, y, 160f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, pt, 100f * Time.deltaTime));
                    NPInput.Override = new NPInput.State { aim = true, fire = t > settle && Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, y)) < 1.5f };
                    t += Time.deltaTime; yield return null;
                }
                NPInput.Override = new NPInput.State { aim = true };
            }
            yield return Idle(1.2f);
            yield return Turn(-12f, -10f, 1.2f); yield return Turn(12f, -12f, 1.2f); yield return Turn(0f, -6f, 0.8f);
            yield return Hold(0.8f, Mv(-1f, 0.2f)); yield return Hold(0.8f, Mv(1f, 0.2f));
            yield return Go(O + new Vector3(-5.5f, 0f, 8.5f), false);
            yield return Go(O + new Vector3(-8.1f, 4.5f, 19.6f), false);     // up the west stairs
            yield return Go(O + new Vector3(-3.5f, 4.5f, 21.2f), false);
            Marks.Add($"{Time.time - t0:F1}s on the mezzanine at {pm.transform.position:F1}");
            yield return Shoot(() => iris.pads[0].bounds.center, 1.0f, 0.3f); Marks.Add($"{Time.time - t0:F1}s pad1 next={iris.Next}");
            float acq = 0; while (halo && halo.Current != CompetitorAI.State.Engage && acq < 5f) { acq += Time.deltaTime; NPInput.Override = new NPInput.State { aim = true }; yield return null; }
            Marks.Add($"{Time.time - t0:F1}s HALO {halo?.Current} after {acq:F1}s");
            yield return Shoot(() => halo.transform.position + Vector3.up * 1.3f, 0.35f, 0f);
            yield return Hold(1.0f, new NPInput.State { move = new Vector2(-1f, -0.2f), aim = true });     // evade toward cover
            float ft = 0;
            while (halo && !halo.TaggedOut && ft < 14f && !pm.TaggedOut)
            {
                Vector3 err = new Vector3(Mathf.Sin(ft * 3.1f), Mathf.Sin(ft * 4.3f + 1f) * 0.6f, Mathf.Cos(ft * 2.7f)) * Mathf.Lerp(0.55f, 0.15f, Mathf.Clamp01(ft / 3f));
                Vector3 tp = halo.transform.position + Vector3.up * 1.2f + halo.Velocity * 0.1f + err, d = tp - rig.cam.transform.position;
                float y = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pt = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, y, 110f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, pt, 80f * Time.deltaTime));
                bool onT = Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, y)) < 2.5f; float cyc = ft % 2.2f; bool firing = cyc < 0.9f;
                NPInput.Override = new NPInput.State { aim = true, fire = onT && firing, move = new Vector2(firing ? 0f : (Mathf.Sin(ft * 1.3f) > 0 ? 0.8f : -0.8f), 0f) };
                ft += Time.deltaTime; yield return null;
            }
            Marks.Add($"{Time.time - t0:F1}s HALO tagged={halo?.TaggedOut} after {ft:F1}s, player hits {hits}, tagged={pm.TaggedOut}");
            if (pm.TaggedOut) { while (pm.TaggedOut) yield return null; yield return Idle(1f); yield return Go(O + new Vector3(-8.1f, 4.5f, 19.6f), false); yield return Go(O + new Vector3(-3.5f, 4.5f, 21.2f), false); }
            yield return Hold(1.9f, new NPInput.State { reloadPressed = true });
            yield return Shoot(() => iris.pads[1].bounds.center, 0.7f, 0.3f); yield return Shoot(() => iris.pads[2].bounds.center, 0.6f, 0.3f);
            yield return Hold(1.0f, new NPInput.State { aim = true });
            yield return Shoot(() => iris.core.position, 0.5f, 0.3f);
            Marks.Add($"{Time.time - t0:F1}s core next={iris.Next} relayOnline={iris.relay.Online} phase={hd.Current}");
            yield return Idle(1.5f);
            Vector3 before = pm.transform.position;
            yield return Go(O + new Vector3(-8.1f, 0f, 9f), false);           // back down
            yield return Go(O + new Vector3(0f, 0f, 26.6f), false);
            yield return Hold(1.2f, Mv(0, 1f));
            Marks.Add($"{Time.time - t0:F1}s after exit: at {pm.transform.position:F1} (teleported {(pm.transform.position - before).magnitude > 20f})");
            yield return Idle(2f);
            ph.Hit -= onHit; ap.fightBack = true; label = ""; Status = "G2 done"; Done = true;
        }


        // Map bounds probe (OPUS_NEXT_BRIEF "Map"): from points on the featured route, sprint diagonally + jump into
        // 8 directions; flag any end position off the walkable NavMesh, below the floor, or any safety-net recovery.
        IEnumerator BoundsProbe()
        {
            Setup(); NPInput.UseOverride = true; HeistDirector.FullRoute = false; hd.BeginRun(); yield return null; FreezeHostiles(true);
            pm.GetComponent<TagHealth>().InvulnerableUntil = Time.time + 9999f;
            int rec0 = PlayerMotor.WorldRecoveries, tests = 0, bad = 0;
            var pts = new[] { new Vector3(-300f, 0.1f, 2.5f), new Vector3(-300f, 0.1f, 14f), new Vector3(-303f, 4.6f, 22f), new Vector3(-308.1f, 2.3f, 14f),
                new Vector3(0f, 0.1f, 9f), new Vector3(0f, 0.1f, 16f), new Vector3(-6f, 0.1f, 18f), new Vector3(6f, 0.1f, 18f), new Vector3(12.5f, 0.1f, 18.5f),
                new Vector3(20f, 0.1f, 16f), new Vector3(25f, 0.1f, 8f), new Vector3(27f, 0.1f, 2f), new Vector3(27f, 0.1f, -8f), new Vector3(27f, 0.1f, -14f) };
            Seg("MAP BOUNDS PROBE (scripted QA input)", "sprint + jump into 8 directions from route points");
            foreach (var p in pts)
            {
                for (int d = 0; d < 8; d++)
                {
                    float yaw = d * 45f + 22.5f;
                    pm.Teleport(p, yaw); rig.SetAim(yaw, 0f); yield return null; yield return null;
                    float t = 0; var st = new NPInput.State { move = new Vector2(0.7f, 0.7f), sprint = true };
                    while (t < 1.8f) { var x = st; x.jumpPressed = (t > 0.5f && t < 0.55f) || (t > 1.2f && t < 1.25f); NPInput.Override = x; t += Time.deltaTime; yield return null; }
                    NPInput.Override = default; yield return new WaitForSeconds(0.25f);
                    tests++;
                    Vector3 e = pm.transform.position;
                    bool onNav = UnityEngine.AI.NavMesh.SamplePosition(e, out var nh, 1.2f, UnityEngine.AI.NavMesh.AllAreas);
                    bool below = e.y < p.y - 3.2f;
                    if (!onNav || below) { bad++; Marks.Add($"ESCAPE? from {p:F1} dir {yaw:F0}: ended {e:F2} onNav={onNav} below={below}"); }
                }
            }
            Marks.Add($"bounds probe: {tests} pushes, {bad} suspicious end positions, recoveries {PlayerMotor.WorldRecoveries - rec0}");
            label = ""; Status = "BP done"; Done = true;
        }


        // Encounter reset acceptance (encounter-reset/README.md) in real Unity: sightlines with the AI's own ray mask,
        // crouch/stand cover, NavMesh routes, and a zero-spread hit test while turning (QA-only spread override).
        IEnumerator EncounterVerify()
        {
            Setup(); NPInput.UseOverride = true; HeistDirector.FullRoute = false; hd.BeginRun(); yield return null; FreezeHostiles(true);
            pm.GetComponent<TagHealth>().InvulnerableUntil = Time.time + 9999f;
            var O = new Vector3(-300f, 0f, 0f); int mask = ~((1 << 2) | (1 << 11) | (1 << 12));
            CompetitorAI halo = null; foreach (var a in ais) if (a && a.name.Contains("HALO")) halo = a;
            var iris = FindFirstObjectByType<IrisRelay>();
            Vector3 haloEye = halo.transform.position + Vector3.up * 1.6f;
            bool Sees(Vector3 from, Vector3 to, Transform target)
            {
                Vector3 d = to - from; if (!Physics.Raycast(from, d.normalized, out var h, d.magnitude, mask, QueryTriggerInteraction.Ignore)) return true;
                return target && h.collider.transform.IsChildOf(target);
            }
            void Check(string name, bool got, bool want) { Marks.Add($"{(got == want ? "PASS" : "FAIL")} {name}: {got}"); }
            Vector3 P(float x, float y, float z) => O + new Vector3(x, y, z);
            float stand = 0.1f + 1.3f, crouch = 0.1f + 0.85f;   // the AI aims at feet + these chest heights
            Check("spawn: HALO cannot see the player", Sees(haloEye, P(-6f, stand, 2.2f), pm.transform), false);
            Check("spawn: relay core not visible from the spawn eye", Sees(P(-6f, 1.62f, 2.2f), iris.core.position, iris.core), false);
            Check("peek standing: HALO sees the player", Sees(haloEye, P(5.8f, stand, 11.8f), null), true);
            Marks.Add($"     peek -> HALO distance {Vector3.Distance(P(5.8f, 0, 11.8f), halo.transform.position):F1} m");
            Check("peek crouched: HALO cannot see the player", Sees(haloEye, P(5.8f, crouch, 12.3f), null), false);
            Check("retreat standing: hidden", Sees(haloEye, P(-6.5f, stand, 14.8f), null), false);
            Check("flank standing: sees HALO (different angle)", Sees(P(-5.5f, 1.62f, 21f), halo.transform.position + Vector3.up * 1.3f, halo.transform), true);
            foreach (var (n, a, b) in new[] { ("spawn->peek", P(-6f, 0.1f, 2.2f), P(5.8f, 0.1f, 11.8f)), ("breach->flank", P(6f, 0.1f, 9.5f), P(-5.5f, 0.1f, 21f)), ("flank->relay", P(-5.5f, 0.1f, 21f), P(-5.5f, 0.1f, 23.4f)), ("relay->exit", P(-5.5f, 0.1f, 23.4f), P(0f, 0.1f, 26.6f)), ("HALO post->peek", halo.transform.position, P(5.8f, 0.1f, 11.8f)) })
            {
                var path = new UnityEngine.AI.NavMeshPath(); UnityEngine.AI.NavMesh.CalculatePath(a, b, UnityEngine.AI.NavMesh.AllAreas, path);
                Check("navmesh " + n, path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete, true);
            }
            // zero-spread hit test while turning: fire at a 0.5 m target 9 m away while the view sweeps across it
            var rifle = pm.GetComponentInChildren<PaintRifle>();
            float hs = rifle.hipSpread, ads = rifle.adsSpread, zs = rifle.zoomSpread, bps = rifle.bloomPerShot, ms = rifle.moveSpread; rifle.hipSpread = rifle.adsSpread = rifle.zoomSpread = rifle.bloomPerShot = rifle.moveSpread = 0f;
            var tgt = GameObject.CreatePrimitive(PrimitiveType.Cube); tgt.name = "_QA_Target"; tgt.transform.localScale = new Vector3(0.5f, 0.5f, 0.1f);
            var counter = tgt.AddComponent<QAHitCounter>();
            foreach (var cam in new[] { "tp", "fp" })
            {
                FindFirstObjectByType<GameBoot>().SetCameraMode(cam); yield return null;
                pm.Teleport(P(-6f, 0.12f, 9.5f), 90f); rig.SetAim(90f, 0f); yield return new WaitForSeconds(0.6f);
                tgt.transform.position = rig.cam.transform.position + new Vector3(9f, 0f, 0f); tgt.transform.rotation = Quaternion.LookRotation(Vector3.left);
                counter.hits = 0; int shots0 = rifle.ShotsFired; rifle.Refill();
                float t = 0;
                while (t < 3f)
                {   // sweep +-6 deg around the target at up to ~120 deg/s, firing only when the crosshair is on it (like a player)
                    float yaw = 90f + Mathf.Sin(t * 3.2f) * 6f; rig.SetAim(yaw, 0f);
                    Vector3 d = tgt.transform.position - rig.cam.transform.position; float onAngle = Vector3.Angle(rig.cam.transform.forward, d);
                    NPInput.Override = new NPInput.State { aim = t > 1.5f, fire = onAngle < 1.2f };
                    t += Time.deltaTime; yield return null;
                }
                NPInput.Override = default; yield return new WaitForSeconds(0.6f);
                int shots = rifle.ShotsFired - shots0;
                Marks.Add($"{(counter.hits >= shots * 0.9f ? "PASS" : "FAIL")} zero-spread turning hit test [{cam}]: {counter.hits}/{shots} hits");
            }
            Destroy(tgt); rifle.hipSpread = hs; rifle.adsSpread = ads; rifle.zoomSpread = zs; rifle.bloomPerShot = bps; rifle.moveSpread = ms;
            FindFirstObjectByType<GameBoot>().SetCameraMode("fp");
            Status = "EV done"; Done = true;
        }


        // Encounter reset: one continuous encounter, scripted QA input through the real input path (first person).
        // spawn -> corridor -> breach (HALO armed, warns) -> peek: stand, trade, crouch when he warns -> HALO down ->
        // around the reactor -> Iris pads 1-2-3 + core -> core door opens -> through it.
        IEnumerator EncounterMinute()
        {
            Setup(); NPInput.UseOverride = true; HeistDirector.FullRoute = false; FindFirstObjectByType<GameBoot>().SetCameraMode("fp"); hd.BeginRun(); yield return null;
            var O = new Vector3(-300f, 0f, 0f); Vector3 P(float x, float z) => O + new Vector3(x, 0.1f, z);
            var iris = FindFirstObjectByType<IrisRelay>();
            CompetitorAI halo = null; foreach (var a in ais) if (a && a.name.Contains("HALO")) halo = a;
            var ph = pm.GetComponent<TagHealth>(); int hits = 0; System.Action<PaintHit> onHit = _ => hits++; ph.Hit += onHit;
            var ap = PlayerAutopilot.Ensure(); ap.fightBack = false;
            Chase(); Seg("ENCOUNTER · " + (rig.firstPersonTrial ? "FIRST PERSON" : "THIRD PERSON") + " (scripted QA input, real input path)", "");
            IEnumerator Go(Vector3 p, bool sprint) { ap.Run(ap.GoTo(p, sprint)); yield return null; float w = 0; while (ap.Busy && w < 20f) { w += Time.deltaTime; yield return null; } }
            IEnumerator Aim(System.Func<Vector3> p, float settle, float fireFor, bool crouchOnWarn = false)
            {
                float t = 0;
                while (t < settle + fireFor)
                {
                    Vector3 d = p() - rig.cam.transform.position;
                    float y = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pt = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                    rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, y, 140f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, pt, 90f * Time.deltaTime));
                    NPInput.Override = new NPInput.State { aim = true, fire = t > settle && Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, y)) < 1.5f };
                    t += Time.deltaTime; yield return null;
                }
                NPInput.Override = new NPInput.State { aim = true };
            }
            yield return Idle(1.2f);
            yield return Go(P(6f, 4.5f), false);
            yield return Go(P(6f, 9.3f), false);
            Marks.Add($"{Time.time - t0:F1}s breach; HALO {halo.Current}");
            yield return Go(P(5.8f, 11.8f), false);
            float acq = 0; while (halo.Current != CompetitorAI.State.Engage && acq < 4f) { acq += Time.deltaTime; NPInput.Override = new NPInput.State { aim = true }; yield return null; }
            Marks.Add($"{Time.time - t0:F1}s HALO {halo.Current} after {acq:F1}s at the peek");
            // trade: stand + fire in short bursts; crouch behind the low cover while he bursts
            float ft = 0;
            while (!halo.TaggedOut && ft < 16f && !pm.TaggedOut)
            {
                bool warn = halo.Current == CompetitorAI.State.Engage && ((ft % 2.6f) > 1.6f);
                if (warn && !pm.Crouched) { NPInput.Override = new NPInput.State { crouchPressed = true, aim = true }; yield return null; }
                if (!warn && pm.Crouched) { NPInput.Override = new NPInput.State { crouchPressed = true, aim = true }; yield return null; }
                Vector3 err = new Vector3(Mathf.Sin(ft * 3.1f), Mathf.Sin(ft * 4.3f + 1f) * 0.6f, Mathf.Cos(ft * 2.7f)) * 0.35f;   // human-like aim error (some shots miss)
                Vector3 tp = halo.transform.position + Vector3.up * 1.25f + err, d = tp - rig.cam.transform.position;
                float y = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pt = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, y, 110f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, pt, 80f * Time.deltaTime));
                NPInput.Override = new NPInput.State { aim = true, fire = !pm.Crouched && Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, y)) < 2f && (ft % 1.3f) < 0.7f };
                ft += Time.deltaTime; yield return null;
            }
            if (pm.Crouched) { NPInput.Override = new NPInput.State { crouchPressed = true }; yield return null; }
            Marks.Add($"{Time.time - t0:F1}s HALO tagged={halo.TaggedOut} after {ft:F1}s; player hits {hits}, tagged={pm.TaggedOut}");
            if (pm.TaggedOut) { while (pm.TaggedOut) yield return null; yield return Idle(1f); }
            yield return Hold(1.8f, new NPInput.State { reloadPressed = true });
            foreach (var (x, z) in new[] { (8.5f, 11.8f), (8.5f, 16f), (6f, 17f), (2.8f, 19.3f), (-3f, 21f), (-5.5f, 21.4f) }) yield return Go(P(x, z), false);
            for (int k = 0; k < 3; k++) yield return Aim(() => iris.pads[iris.Next < 3 ? iris.Next : 2].bounds.center, 0.55f, 0.25f);
            yield return Hold(0.9f, new NPInput.State { aim = true });
            yield return Aim(() => iris.core.position, 0.4f, 0.25f);
            Marks.Add($"{Time.time - t0:F1}s relay online={iris.relay.Online} next={iris.Next}");
            yield return Idle(1.2f);
            yield return Go(P(-2f, 26.4f), false); yield return Go(P(0f, 26.7f), false);
            rig.SetAim(0f, 0f); float w2 = 0; while (pm.transform.position.x < -100f && w2 < 3f) { NPInput.Override = Mv(0, 1f); w2 += Time.deltaTime; yield return null; } NPInput.Override = default;
            Marks.Add($"{Time.time - t0:F1}s through the core door: {pm.transform.position.x > -100f}. recoveries {PlayerMotor.WorldRecoveries}. {RuntimeErrorMonitor.Summary()}");
            yield return Idle(1.5f);
            ph.Hit -= onHit; ap.fightBack = true; label = ""; Status = "G3 done"; Done = true;
        }


        // Controls at 30 / 60 / 120 FPS and across a long frame: same input -> same distance, clean stop, shots
        // match the fire rate (no duplicates or losses after a hitch).
        IEnumerator FrameRates()
        {
            Setup(); NPInput.UseOverride = true; HeistDirector.FullRoute = false; hd.BeginRun(); yield return null; FreezeHostiles(true);
            pm.GetComponent<TagHealth>().InvulnerableUntil = Time.time + 9999f;
            var rifle = pm.GetComponentInChildren<PaintRifle>(); var O = new Vector3(-300f, 0f, 0f);
            int vs = QualitySettings.vSyncCount; QualitySettings.vSyncCount = 0;
            foreach (int fps in new[] { 30, 60, 120 })
            {
                Application.targetFrameRate = fps;
                pm.Teleport(O + new Vector3(-8f, 0.12f, 3f), 90f); rig.SetAim(90f, 0f); yield return new WaitForSeconds(0.5f);
                Vector3 a = pm.transform.position; float t = 0; while (t < 1.5f) { NPInput.Override = Mv(0, 1f); t += Time.deltaTime; yield return null; }
                Vector3 b = pm.transform.position; NPInput.Override = default; yield return new WaitForSeconds(0.6f);
                Vector3 c = pm.transform.position;
                rifle.Refill(); int s0 = rifle.ShotsFired; t = 0; while (t < 1.0f) { NPInput.Override = new NPInput.State { fire = true, aim = true }; t += Time.deltaTime; yield return null; }
                NPInput.Override = default; int shots = rifle.ShotsFired - s0;
                Marks.Add($"{fps} fps: run 1.5 s = {(b - a).magnitude:F2} m, stop overshoot {(c - b).magnitude:F2} m, 1 s of fire = {shots} shots (rate {rifle.fireRate})");
            }
            Application.targetFrameRate = 60;
            // long frame while firing: one 250 ms hitch must not burst extra shots
            rifle.Refill(); int h0 = rifle.ShotsFired; float tt = 0; bool hitched = false;
            while (tt < 1.0f) { NPInput.Override = new NPInput.State { fire = true, aim = true }; if (!hitched && tt > 0.4f) { hitched = true; System.Threading.Thread.Sleep(250); } tt += Time.unscaledDeltaTime; yield return null; }
            NPInput.Override = default;
            Marks.Add($"long frame (250 ms hitch) during 1 s of fire: {rifle.ShotsFired - h0} shots (expected <= rate+1)");
            QualitySettings.vSyncCount = vs; Application.targetFrameRate = -1;
            Status = "FR done"; Done = true;
        }

        // R4 door acceptance: locked door holds, frame edges block, oblique and straight crossings pass, restart re-locks.
        // Setup teleports place the runner for each case (unit test); the route recording is the end-to-end proof.
        IEnumerator DoorTest()
        {
            Setup(); NPInput.UseOverride = true; HeistDirector.FullRoute = false; hd.BeginRun(); yield return null; FreezeHostiles(true);
            pm.GetComponent<TagHealth>().InvulnerableUntil = Time.time + 9999f;
            var O = new Vector3(-300f, 0f, 0f); var iris = FindFirstObjectByType<IrisRelay>(); var exit = FindFirstObjectByType<RoomExit>();
            var ap = PlayerAutopilot.Ensure(); ap.fightBack = false;
            IEnumerator Walk(Vector3 from, Vector3 toward, float sec)
            {
                Vector3 d = toward - from; float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                Place(from, yaw, 0f); yield return null; float t = 0; while (t < sec) { NPInput.Override = Mv(0, 1f); t += Time.deltaTime; if (pm.transform.position.x > -100f) break; yield return null; }
                NPInput.Override = default; yield return null;
            }
            IEnumerator PowerIris()
            {
                Place(O + new Vector3(-5.5f, 0.1f, 21.4f), 180f, 0f); yield return null; float t0 = Time.time;
                while (!iris.relay.Online && Time.time - t0 < 20f) { int k = iris.Next; yield return ap.Run(ap.AimFire(() => k < 3 ? iris.pads[k].bounds.center : iris.core.position, 0.25f, settle: 0.5f)); if (k == 3 && iris.Next == 3) yield return new WaitForSeconds(0.3f); }
                yield return new WaitForSeconds(1.6f);   // the door slides open (~1.1 s)
            }
            string R(bool ok, string what) { string s = (ok ? "PASS " : "FAIL ") + what; Marks.Add(s); return s; }
            int p0 = RoomExit.Passed;
            yield return Walk(O + new Vector3(0f, 0.1f, 25.5f), O + new Vector3(0f, 0.1f, 32f), 2.5f);
            R(pm.transform.position.x < -100f && pm.transform.position.z < O.z + 27.9f && RoomExit.Passed == p0, $"locked door holds before the Iris (stopped at z={pm.transform.position.z:F2}, door plane 27.4-28)");
            yield return PowerIris();
            R(exit.Open && !exit.block.enabled, $"Iris online -> door open, blocker off (open={exit.Open}, block={exit.block.enabled})");
            foreach (float ex in new[] { -1.85f, 1.85f })
            {
                yield return Walk(O + new Vector3(ex, 0.1f, 25.5f), O + new Vector3(ex, 0.1f, 32f), 2.0f);
                R(pm.transform.position.x < -100f && RoomExit.Passed == p0 && pm.transform.position.z < O.z + 28.0f, $"frame edge x={ex:F2} blocks (z={pm.transform.position.z:F2})");
            }
            foreach (var (fx, fz, tx) in new[] { (3.2f, 24.5f, -0.6f), (-3.2f, 24.5f, 0.6f), (0.9f, 25.5f, 0.9f), (-0.9f, 25.5f, -0.9f), (0f, 25.5f, 0f) })
            {
                int before = RoomExit.Passed;
                hd.BeginRun(); yield return null; FreezeHostiles(true); pm.GetComponent<TagHealth>().InvulnerableUntil = Time.time + 9999f;
                R(!exit.Open && exit.block.enabled, "restart re-locks the door");
                yield return PowerIris();
                yield return Walk(O + new Vector3(fx, 0.1f, fz), O + new Vector3(tx, 0.1f, 31f), 4f);
                yield return new WaitForSeconds(0.2f);
                R(RoomExit.Passed == before + 1 && pm.transform.position.x > -100f, $"walk from ({fx:F1},{fz:F1}) toward x={tx:F1} crosses the door -> arrived at {pm.transform.position:F1}");
            }
            Marks.Add($"door test end. recoveries {PlayerMotor.WorldRecoveries}. {RuntimeErrorMonitor.Summary()}");
            FreezeHostiles(false); Status = "DR done"; Done = true;
        }

        // R4 bike acceptance: steering, release-to-straight, outer walls, divider head-on / oblique / with dodge, at 30 and
        // 60 FPS and across a 300 ms hitch. Warp() places the bike for each case (unit test, not route proof).
        IEnumerator BikeTest()
        {
            Setup(); hd.PracticeFinality(); NPInput.UseOverride = true; yield return new WaitForSeconds(0.5f);
            var run = FinalityRun.Instance; var b = run.bike; var t = b.track;
            float clear = t.dividerHalf + 0.55f;
            int vs = QualitySettings.vSyncCount; QualitySettings.vSyncCount = 0;
            string R(bool ok, string what) { string s = (ok ? "PASS " : "FAIL ") + what; Marks.Add(s); return s; }
            foreach (int fps in new[] { 60, 30 })
            {
                Application.targetFrameRate = fps; yield return new WaitForSeconds(0.3f);
                // steer + release on the straight before the bridge
                b.Warp(300f, 0f); yield return Hold(0.6f, default);
                float x0 = b.X; yield return Hold(0.8f, Mv(-1f, 0)); float xl = b.X; yield return Hold(0.8f, Mv(1f, 0)); float xr = b.X;
                yield return Hold(0.4f, default); float xa = b.X; yield return Hold(1.2f, default); float xb = b.X;
                R(xl < x0 - 3f && xr > xl + 3f, $"{fps} fps steer: left {x0:F1}->{xl:F1}, right ->{xr:F1}");
                R(Mathf.Abs(xb - xa) < 1.2f, $"{fps} fps release-to-straight: drift {Mathf.Abs(xb - xa):F2} m over 1.2 s at s={b.S:F0} (bend push capped)");
                // outer wall, boost + steer held
                b.Warp(330f, 0f); float maxX = 0; float tt = 0; while (tt < 2.5f) { NPInput.Override = Mv(1f, 0, sprint: true); maxX = Mathf.Max(maxX, b.X); tt += Time.deltaTime; yield return null; }
                R(maxX <= t.HalfWidth(b.S) - 0.75f + 0.01f, $"{fps} fps right wall: max x {maxX:F2} <= lane {t.HalfWidth(b.S) - 0.75f:F2}");
                // divider: head-on (centred, boost, no steer)
                int h0 = Lightcycle.DividerHits; b.RestoreShields(); b.Warp(t.dividerS0 - 40f, 0f); bool bad = false; float minA = 99f;
                tt = 0; while (tt < 3f) { NPInput.Override = Mv(0, 0, sprint: true); if (b.S >= t.dividerS0 && b.S <= t.dividerS1) { minA = Mathf.Min(minA, Mathf.Abs(b.X)); if (Mathf.Abs(b.X) < clear - 0.01f) bad = true; } tt += Time.deltaTime; yield return null; }
                R(Lightcycle.DividerHits > h0 && !bad, $"{fps} fps head-on nose: hits {Lightcycle.DividerHits - h0}, min |x| inside {minA:F2} (clear {clear:F2}), shields {b.Shields}");
                // oblique + steer into the divider + dodges, both sides
                foreach (float side in new[] { 1f, -1f })
                {
                    b.RestoreShields(); b.Warp(t.dividerS0 + 10f, side * 4f); float s0 = Mathf.Sign(b.X); bool flip = false; minA = 99f; tt = 0;
                    while (tt < 3f && b.S < t.dividerS1) { NPInput.Override = new NPInput.State { move = new Vector2(-side, 0), sprint = true, jumpPressed = (tt % 0.5f) < Time.deltaTime * 1.5f }; minA = Mathf.Min(minA, Mathf.Abs(b.X)); if (Mathf.Sign(b.X) != s0) flip = true; tt += Time.deltaTime; yield return null; }
                    R(!flip && minA >= clear - 0.01f, $"{fps} fps steer+dodge into divider from x={side * 4f:F0}: never crossed (min |x| {minA:F2})");
                }
            }
            // hitch: 300 ms frame while steering hard into the divider
            Application.targetFrameRate = 60; b.Warp(t.dividerS0 + 20f, 1.2f);
            { float tt = 0; bool flip = false, hitched = false; while (tt < 1.5f) { NPInput.Override = new NPInput.State { move = new Vector2(-1f, 0), sprint = true, jumpPressed = tt > 0.3f && !hitched }; if (!hitched && tt > 0.35f) { hitched = true; System.Threading.Thread.Sleep(300); } if (b.X < 0f) flip = true; tt += Time.unscaledDeltaTime; yield return null; }
              R(!flip, $"300 ms hitch + dodge into the divider: stayed on the right (x={b.X:F2})"); }
            QualitySettings.vSyncCount = vs; Application.targetFrameRate = -1;
            Marks.Add($"bike test end: divider hits {Lightcycle.DividerHits}, scrapes {Lightcycle.DividerScrapes} frames, wall frames {Lightcycle.WallFrames}. {RuntimeErrorMonitor.Summary()}");
            NPInput.Override = default; run.Abort(); Status = "BK done"; Done = true;
        }

        // Vault walk for the browser route (scripted QA input through NPInput): walk to the centrepiece, inspect it (E),
        // then walk to the collectors' wing gate and stand there; the page shows the real ownership state.
        IEnumerator VaultWalk()
        {
            Setup(); NPInput.UseOverride = true; Chase();
            float w = 0; while ((!VaultRoom.Instance || !VaultRoom.Instance.Active) && w < 10f) { w += Time.deltaTime; yield return null; }
            var v = VaultRoom.Instance; if (!v || !v.Active) { Marks.Add("vault not active"); Status = "VW done"; Done = true; yield break; }
            Seg("THE VAULT (scripted QA input)", "");
            IEnumerator WalkTo(Vector3 p, float stopAt, float maxT)
            {
                float t = 0;
                while (t < maxT)
                {
                    Vector3 d = p - pm.transform.position; d.y = 0; if (d.magnitude < stopAt) break;
                    float y = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, y, 160f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, 2f, 30f * Time.deltaTime));
                    NPInput.Override = Mv(0, Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, y)) > 50f ? 0.2f : 0.75f); t += Time.deltaTime; yield return null;
                }
                NPInput.Override = default;
            }
            IEnumerator Face(Vector3 p, float sec) { float t = 0; while (t < sec) { Vector3 d = p - rig.cam.transform.position; float y = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pt = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg; rig.SetAim(Mathf.MoveTowardsAngle(rig.Yaw, y, 120f * Time.deltaTime), Mathf.MoveTowards(rig.Pitch, pt, 60f * Time.deltaTime)); t += Time.deltaTime; yield return null; } }
            yield return Idle(1.5f);
            Vector3 c0 = v.slots[0].bounds.center; Vector3 front = c0 + (pm.transform.position - c0).normalized * 2.4f; front.y = pm.transform.position.y;
            yield return WalkTo(front, 0.4f, 8f);
            yield return Face(c0, 1.2f);
            Marks.Add($"{Time.time - t0:F1}s at the centrepiece");
            NPInput.Override = new NPInput.State { interactPressed = true }; yield return null; NPInput.Override = default;
            yield return Idle(6f);   // the page's inspect panel (record / registered / mint state)
            if (v.gateFront)
            {
                Vector3 g = v.gateFront.position, side = new Vector3(c0.x + 3.2f, pm.transform.position.y, c0.z);   // round the centrepiece
                yield return WalkTo(side - Vector3.forward * 2f, 0.5f, 6f); yield return WalkTo(side + Vector3.forward * 4f, 0.5f, 6f);
                yield return WalkTo(g, 0.6f, 10f);
                yield return Face(v.gateDoor ? v.gateDoor.position + Vector3.up * 1.5f : v.gateFront.position + v.gateFront.forward * 3f, 1.2f);
                Marks.Add($"{Time.time - t0:F1}s at the collectors' wing gate: state {v.GateState}");
                yield return Idle(5f);
            }
            label = ""; Status = "VW done"; Done = true;
        }

        // Editor proof of the browser path: complete the featured route, then enter the Vault the way the page does.
        IEnumerator RouteThenVault()
        {
            yield return JudgeRoute(); Done = false;
            FindFirstObjectByType<GameBoot>().EnterVaultFromWeb(); yield return null;
            yield return VaultWalk();
            var p = pm.transform.position; Marks.Add($"after the route: player active={pm.gameObject.activeInHierarchy} rig={rig.enabled} at {p:F1}");
        }

        // R4 ghost-wall test: walk INTO every visible solid-looking object on the route from each open side; if the
        // player's centre ends inside its visible bounds, it is walk-through. Results: GhostList (for the fix).
        public static readonly List<string> GhostList = new List<string>();
        IEnumerator GhostWalls()
        {
            Setup(); NPInput.UseOverride = true; HeistDirector.FullRoute = false; hd.BeginRun(); yield return null; FreezeHostiles(true);
            pm.GetComponent<TagHealth>().InvulnerableUntil = Time.time + 9999f; GhostList.Clear(); slow = 3f;
            var skip = new[] { "LightShaft", "Petal", "Accent", "bezel", "Recess", "Exhibit", "Frame", "Glow", "Wheel", "Bike", "Core", "Passage", "Signal", "Label", "Lamp", "Seam", "Line", "Cap", "Trim" };
            var cands = new List<Renderer>();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue; var b = r.bounds; var c = b.center;
                bool area = (c.x > -312 && c.x < -288 && c.z > -1 && c.z < 33) || (c.x > -20 && c.x < 40 && c.z > -30 && c.z < 40) || (c.x > -615 && c.x < -585);
                if (!area || b.size.y < 0.6f || b.min.y > 0.8f || Mathf.Max(b.size.x, b.size.z) < 0.5f) continue;
                bool sk = false; foreach (var k in skip) if (r.name.Contains(k)) sk = true; if (sk) continue;
                if (r.GetComponentInParent<PlayerMotor>() || r.GetComponentInParent<CompetitorAI>()) continue;
                cands.Add(r);
            }
            Marks.Add($"ghost-wall candidates: {cands.Count}");
            foreach (var r in cands)
            {
                var b = r.bounds; bool ghost = false; string how = "";
                foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                {
                    float ext = Mathf.Abs(d.x) * b.extents.x + Mathf.Abs(d.z) * b.extents.z;
                    var start = new Vector3(b.center.x, b.min.y + 0.2f, b.center.z) + d * (ext + 1.1f);
                    if (!NavMesh.SamplePosition(start, out var nh, 0.6f, NavMesh.AllAreas)) continue;
                    if (Mathf.Abs(nh.position.y - b.min.y) > 0.6f) continue;
                    float yaw = Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg;
                    pm.Teleport(nh.position + Vector3.up * 0.05f, yaw); rig.SetAim(yaw, 0f); yield return null;
                    float t = 0; while (t < 1.0f) { NPInput.Override = Mv(0, 1f); t += Time.deltaTime; yield return null; }
                    NPInput.Override = default; yield return null;
                    var p = pm.transform.position; var inner = new Bounds(b.center, b.size - new Vector3(0.2f, 0, 0.2f));
                    if (p.x > inner.min.x && p.x < inner.max.x && p.z > inner.min.z && p.z < inner.max.z && p.y < b.max.y - 0.5f) { ghost = true; how = $"from {d:F0} ended {p:F1}"; break; }
                }
                if (ghost) { string id = $"{r.name} @{b.center:F1} size {b.size:F1} parent {(r.transform.parent ? r.transform.parent.name : "")} {how}"; GhostList.Add(id); Marks.Add("GHOST " + id); }
            }
            slow = 1f; Time.timeScale = 1f;
            Marks.Add($"ghost walls: {GhostList.Count} of {cands.Count}. {RuntimeErrorMonitor.Summary()}");
            FreezeHostiles(false); Status = "GW done"; Done = true;
        }

        IEnumerator SwitchProbe()
        {
            Setup(); NPInput.UseOverride = true; hd.PracticeFinality(); yield return new WaitForSeconds(0.3f);
            var run = FinalityRun.Instance; var b = run.bike; TrackElement sw = null; foreach (var e in run.Elements) if (e.kind == TrackElement.Kind.Switch && sw == null) sw = e;
            b.Warp(560f, 4.5f); var bap = run.gameObject.AddComponent<BikeAutopilot>(); int f0 = Lightcycle.ShotsFired; float t = 0;
            while (t < 12f && b.S < sw.s + 5f) { t += Time.deltaTime; if (Lightcycle.ShotsFired != f0 && Marks.Count < 40) { Vector3 to = sw.transform.position - b.muzzle.position; var v = b.ShotVelocity(b.muzzle.position, b.transform.forward); string hn = Physics.Raycast(b.muzzle.position, v.normalized, out var hh, 90f, ~(1 << b.gameObject.layer), QueryTriggerInteraction.Collide) ? hh.collider.name + "@" + hh.distance.ToString("F0") + " L" + hh.collider.gameObject.layer : "nothing"; var lt = sw.lamp.transform.position - b.muzzle.position; Marks.Add($"shot at s={b.S:F0} x={b.X:F1} d={lt.magnitude:F0} lampAngle={Vector3.Angle(b.transform.forward, lt):F1} shotVsLamp={Vector3.Angle(v, lt):F1} ray hits {hn} bikeLayer {b.gameObject.layer} switchDone={sw.done}"); f0 = Lightcycle.ShotsFired; } yield return null; }
            Marks.Add($"switch done={sw.done} bike s={b.S:F0} damage: " + string.Join(" | ", Lightcycle.DamageLog));
            Destroy(bap); run.Abort(); Status = "SW done"; Done = true;
        }

        // Lightcycle: real Finality Run, QA autopilot steering (no human).
        IEnumerator VideoD()
        {
            Setup();
            hd.PracticeFinality();
            Seg("D — FINALITY RUN (Act II) — QA bike autopilot steers via the real bike input", "Gameplay camera. Rival riders: NOT IMPLEMENTED");
            var ap = FinalityRun.Instance.gameObject.AddComponent<BikeAutopilot>();
            float t = 0; while ((hd.Current == HeistDirector.Phase.FinalityRun || hd.Current == HeistDirector.Phase.Ascension) && t < 110f) { t += Time.deltaTime; if (hd.Current == HeistDirector.Phase.Ascension) { label = "D — ACT III: THE FINALITY ELEVATOR"; sub = $"ascent {FinalityElevator.Progress * 100f:F0}% · real Monad blocks observed {FinalityElevator.Blocks}"; yield return null; continue; } sub = $"s={FinalityRun.Instance.bike.S:F0} m  shards {FinalityRun.Instance.Shards}/{FinalityRun.Instance.ShardsTotal}  crashes {FinalityRun.Instance.Crashes}"; yield return null; }
            Destroy(ap);
            Seg($"D — END phase {hd.Current}", RuntimeErrorMonitor.Summary());
            Marks.Add("damage: " + string.Join(" | ", Lightcycle.DamageLog)); Marks.Add($"D: shards {FinalityRun.Instance.Shards}/{FinalityRun.Instance.ShardsTotal} crashes {FinalityRun.Instance.Crashes} phase {hd.Current}");
            yield return Idle(3f);
            label = ""; Status = "D done"; Done = true;
        }
    }
}
