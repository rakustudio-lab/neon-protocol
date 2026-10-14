using UnityEngine;

namespace NeonProtocol
{
    // The Finality Runner lightcycle. Controlled in track space: A/D steer, SHIFT boost, S brake, SPACE dodge,
    // LMB paint launcher (auto-aims a few degrees toward switches/drones), R reload. Curves push the bike outward,
    // walls scrape speed, barriers and closed shutters cost a shield and stop the bike. 0 shields = crash -> checkpoint.
    public class Lightcycle : MonoBehaviour
    {
        public FinalityTrack track;
        public Transform wheelFront, wheelRear, body, muzzle, riderRoot;
        public float cruise = 50f, boostSpeed = 68f, brakeSpeed = 28f, accel = 16f, decel = 34f;
        public float steerSpeed = 15f, curvePush = 0.045f, wheelRadius = 0.42f;
        public int magazine = 20; public float fireRate = 8f, reloadTime = 1.3f, muzzleSpeed = 95f;
        public int maxShields = 3;
        public AudioSource engine, sfx;
        public AudioClip hitClip, shardClip, fireClip, dodgeClip;

        public float S { get; private set; }
        public float X { get; private set; }
        public float Speed { get; private set; }
        public float Boost { get; private set; } = 1f;
        public int Ammo { get; private set; }
        public bool Reloading => reloadT > 0f;
        public float ReloadProgress => Reloading ? 1f - reloadT / reloadTime : 0f;
        public int Shields { get; private set; }
        public float DodgeReady => Mathf.Clamp01(1f - dodgeCd / 1.2f);
        public bool Active { get; set; }
        public bool Finale { get; private set; }
        Vector3 finaleLook; float finaleT;

        float xVel, dodgeT, dodgeCd, dodgeDir, fireT, reloadT, invuln, lean, shake, divSide;
        public static int DividerHits, DividerScrapes, WallFrames, ShotsFired;   // QA counters
        public static readonly System.Collections.Generic.List<string> DamageLog = new System.Collections.Generic.List<string>();
        Camera cam; float camLat; RiderPose rider;

        // End of the ride: park just before the gate and let the camera pull back to frame rider + gate.
        public void BeginFinale(float gateS)
        {
            Finale = true; Active = false; finaleT = 0f;
            S = Mathf.Min(S, gateS - 7f); X = Mathf.Clamp(X, -2f, 2f); lean = 0f; Pose(0f);
            finaleLook = track.World(gateS, 0f, 3.6f);
            if (engine) engine.volume = 0.15f;
        }

        public void Place(float s, float x)
        {
            Finale = false;
            S = s; X = x; xVel = 0f; divSide = 0f; Speed = 0f; Boost = 1f; Ammo = magazine; reloadT = 0f; Shields = maxShields; invuln = 1f;
            Pose(0f); ClearTrails();
            cam = Camera.main;
            if (cam) { cam.transform.position = transform.position - transform.forward * 6f + transform.up * 2.4f; cam.transform.LookAt(transform.position + transform.forward * 8f); }
        }

        // Move without resetting shields/ammo (used after bumping into an obstacle).
        public void RestoreShields() => Shields = maxShields;   // QA only
        public void Warp(float s, float x) { S = s; X = x; xVel = 0f; divSide = 0f; Speed = Mathf.Min(Speed, 12f); Pose(0f); ClearTrails(); }
        void ClearTrails() { foreach (var t in GetComponentsInChildren<TrailRenderer>()) t.Clear(); }

        public void Damage(string why, bool stop)
        {
            if (invuln > 0f) return;
            if (DamageLog.Count < 200) DamageLog.Add($"s={S:F0} x={X:F2} {why}");
            Shields--; invuln = 0.9f; shake = 1f;
            if (sfx && hitClip) sfx.PlayOneShot(hitClip);
            if (stop) Speed = Mathf.Min(Speed, 10f);
            else Speed *= 0.6f;
            FinalityRun.Instance?.OnDamage(why, Shields);
        }

        void Update()
        {
            if (!Active || !track) return;
            float dt = Time.deltaTime;
            var inp = NPInput.Read();
            float steer = inp.move.x;
            bool braking = inp.controller ? inp.brake > 0.1f : inp.move.y < -0.3f;
            bool boosting = inp.sprint && Boost > 0.02f && !braking && (!inp.controller || inp.throttle > 0.1f);
            // Triggers are conventional throttle/brake. Keyboard keeps the existing assisted cruise.
            float target = inp.controller ? (braking ? 0f : inp.throttle * (boosting ? boostSpeed : cruise))
                : braking ? brakeSpeed : boosting ? boostSpeed : cruise;
            Speed = Mathf.MoveTowards(Speed, target, (Speed < target ? accel : decel) * dt);
            Boost = Mathf.Clamp01(Boost + (boosting ? -0.3f : 0.07f) * dt);

            // dodge: short lateral dash with brief invulnerability
            dodgeCd -= dt; invuln -= dt;
            if (inp.jumpPressed && dodgeCd <= 0f) { dodgeT = 0.22f; dodgeCd = 1.2f; dodgeDir = Mathf.Abs(steer) > 0.1f ? Mathf.Sign(steer) : (X > 0 ? -1f : 1f); invuln = Mathf.Max(invuln, 0.3f); if (sfx && dodgeClip) sfx.PlayOneShot(dodgeClip); }
            float dash = 0f; if (dodgeT > 0f) { dodgeT -= dt; dash = dodgeDir * 15f; }

            float turn = track.Turn(S);
            xVel = Mathf.Lerp(xVel, steer * steerSpeed, 1f - Mathf.Exp(-9f * dt));
            // R4: the outward push on bends is capped so steering always wins and a released stick drifts gently
            // (0.045 gave ~3 m/s at boost on the 0.8 deg/m bends = a 9 m swerve into the wall over one bend)
            float push = Mathf.Clamp(-turn * Mathf.Deg2Rad * Speed * Speed * curvePush, -1.6f, 1.6f);
            float prevS = S;
            S += Speed * dt;
            X += (xVel + dash + push) * dt;
            float half = track.HalfWidth(S) - 0.75f;
            if (Mathf.Abs(X) > half)
            {
                X = Mathf.Sign(X) * half; xVel = 0f; WallFrames++;
                Speed = Mathf.Max(inp.controller ? 0f : 18f, Speed - 22f * dt);
                shake = Mathf.Max(shake, 0.35f);
            }
            // the lane-choice divider is solid (audit 2026-10-11: the bike used to drive straight through it)
            // R4: the lane side is chosen ONCE at the nose and kept until the divider ends. It used to be re-read from the
            // sign of X every frame, so a long frame / dodge that moved X past 0 put the bike on the other side (through
            // the divider), and a bike arriving centred was snapped 1 m sideways. Now the nose is a head-on collision.
            if (track.dividerS1 > track.dividerS0)
            {
                float s0 = track.dividerS0, s1 = track.dividerS1, clear = track.dividerHalf + 0.55f;
                if (S < s0 || S > s1) divSide = 0f;
                else if (divSide == 0f)
                {
                    if (prevS < s0 && Mathf.Abs(X) < clear)
                    {   // nose hit: stop at the nose, bounce toward the nearer side, lose a shield (once per invulnerability window)
                        float away = Mathf.Abs(X) > 0.05f ? Mathf.Sign(X) : Mathf.Abs(steer) > 0.1f ? Mathf.Sign(steer) : 1f;
                        S = s0 - 0.8f; prevS = Mathf.Min(prevS, S); Speed = Mathf.Min(Speed, 10f);
                        xVel = away * 9f; shake = 1f; DividerHits++;
                        Damage("DIVIDER HIT", true);
                    }
                    else divSide = X >= 0f ? 1f : -1f;
                }
                if (divSide != 0f && X * divSide < clear)
                {
                    X = divSide * clear; xVel = 0f;                                          // scraping the divider: like a wall
                    Speed = Mathf.Max(inp.controller ? 0f : 18f, Speed - 22f * dt); shake = Mathf.Max(shake, 0.35f); DividerScrapes++;
                }
            }
            FinalityRun.Instance?.Sweep(this, prevS, S, X);

            // weapon
            fireT -= dt;
            if (reloadT > 0f) { reloadT -= dt; if (reloadT <= 0f) Ammo = magazine; }
            else if ((inp.reloadPressed && Ammo < magazine) || Ammo == 0) reloadT = reloadTime;
            if (inp.fire && reloadT <= 0f && Ammo > 0 && fireT <= 0f && PaintProjectiles.Instance && muzzle)
            {
                fireT = 1f / fireRate; Ammo--; ShotsFired++;
                PaintProjectiles.Instance.Fire(muzzle.position, ShotVelocity(muzzle.position, transform.forward), 0, this, gameObject.layer);
                if (sfx && fireClip) sfx.PlayOneShot(fireClip, 0.5f);
            }

            lean = Mathf.Lerp(lean, -(xVel + dash) / steerSpeed * 26f - turn * 9f, 1f - Mathf.Exp(-6f * dt));
            Pose(Speed * dt / wheelRadius * Mathf.Rad2Deg);
            if (engine) { engine.pitch = 0.6f + Speed / boostSpeed * 0.9f + (boosting ? 0.15f : 0f); engine.volume = 0.55f + Speed / boostSpeed * 0.4f; }
        }

        // Aim assist (a few degrees toward switches/drones). R4: the shot used to be dir*muzzle + forward*bikeSpeed, which
        // bent an assisted shot ~2.5 deg back toward straight ahead (68 m/s of bike speed), and the gravity lift was ~3x
        // too high, so switches were missed even when the assist chose them. Assisted shots now fly straight at the
        // target with the exact ballistic lift; unassisted shots keep the bike's speed added.
        public Vector3 ShotVelocity(Vector3 from, Vector3 fwd)
        {
            float speed = muzzleSpeed + Speed;
            Vector3 v = fwd * muzzleSpeed + fwd * Speed;
            if (!FinalityRun.Instance) return v;
            float bestA = 12f; float g = PaintProjectiles.Instance ? PaintProjectiles.Instance.gravity : 0f;
            foreach (var t in FinalityRun.Instance.AimTargets)
            {
                if (!t || !t.gameObject.activeInHierarchy) continue;
                Vector3 to = t.position - from; float d = to.magnitude;
                if (d < 4f || d > 75f) continue;
                float a = Vector3.Angle(fwd, to);
                if (a < bestA) { bestA = a; float tf = d / speed; v = (to + Vector3.up * 0.5f * g * tf * tf).normalized * speed; }
            }
            return v;
        }

        void Pose(float wheelDeg)
        {
            track.Sample(S, out var p, out var f, out var r, out var u, out _);
            transform.SetPositionAndRotation(p + r * X, Quaternion.LookRotation(f, u));
            if (body) body.localRotation = Quaternion.Euler(0f, 0f, lean);
            if (riderRoot) { if (!rider) rider = riderRoot.GetComponent<RiderPose>(); if (rider) rider.lean = lean; }
            if (wheelFront) wheelFront.Rotate(-wheelDeg, 0f, 0f, Space.Self);   // model is turned 180 deg: local X points left
            if (wheelRear) wheelRear.Rotate(-wheelDeg, 0f, 0f, Space.Self);
        }

        // R4 cockpit camera (default): the camera rides at the rider's eyes and the rider body is hidden, so the player
        // sees the bike front and the road instead of a rider whose hands don't sit on the bars. Chase camera kept for QA.
        public static bool Cockpit = true;
        bool riderHidden, handsChecked, hasHands;
        void HideRider(bool hide)
        {
            if (!riderRoot || riderHidden == hide) return; riderHidden = hide;
            foreach (var r in riderRoot.GetComponentsInChildren<Renderer>(true)) r.enabled = !hide;
        }
        void CockpitCam(Quaternion look, float fov)
        {
            cam.transform.position = transform.position + transform.up * 1.62f + transform.forward * 0.88f + Random.insideUnitSphere * shake * 0.05f * Settings.CameraShake;
            cam.transform.rotation = look;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, Time.deltaTime * 3f);
            cam.nearClipPlane = 0.05f;
        }

        void LateUpdate()
        {
            if (!handsChecked) { handsChecked = true; hasHands = riderRoot && riderRoot.GetComponent<RiderHands>(); }
            HideRider(Cockpit && (Active || Finale) && !hasHands);   // with RiderHands the rider stays: arms on the grips, head hidden
            if (Finale && cam && Cockpit)
            {
                CockpitCam(Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(finaleLook - cam.transform.position, transform.up), 1f - Mathf.Exp(-3f * Time.deltaTime)), 70f);
                return;
            }
            if (Active && cam && Cockpit)
            {
                shake = Mathf.Max(0f, shake - Time.deltaTime * 2.5f);
                float sp0 = Speed / boostSpeed;
                CockpitCam(Quaternion.LookRotation(transform.forward, transform.up) * Quaternion.Euler(6f, 0f, lean * 0.2f), 74f + sp0 * 14f);
                return;
            }
            if (Finale && cam)
            {
                finaleT += Time.deltaTime;
                float k = 1f - Mathf.Exp(-1.2f * finaleT);
                Vector3 fw = transform.position - transform.forward * Mathf.Lerp(5f, 10f, k) + transform.up * Mathf.Lerp(2f, 3.6f, k) + transform.right * Mathf.Lerp(0f, 2.5f, k);
                cam.transform.position = Vector3.Lerp(cam.transform.position, fw, 1f - Mathf.Exp(-4f * Time.deltaTime));
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(finaleLook - cam.transform.position, transform.up), 1f - Mathf.Exp(-3f * Time.deltaTime));
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 58f, Time.deltaTime * 2f);
                return;
            }
            if (!Active || !cam) return;
            float sp = Speed / boostSpeed;
            shake = Mathf.Max(0f, shake - Time.deltaTime * 2.5f);
            Vector3 want = transform.position - transform.forward * (4.6f + sp * 0.9f) + transform.up * (1.85f - sp * 0.25f);
            // rigid chase in the bike's frame (a smoothed follow lags metres behind at 50+ m/s); lateral offset eases a little
            camLat = Mathf.Lerp(camLat, X, 1f - Mathf.Exp(-5f * Time.deltaTime));
            want += transform.right * (camLat - X) * 0.6f;
            cam.transform.position = want + Random.insideUnitSphere * shake * 0.12f * Settings.CameraShake;
            var look = Quaternion.LookRotation(transform.position + transform.forward * 10f + transform.up * 0.9f - cam.transform.position, transform.up);
            cam.transform.rotation = look * Quaternion.Euler(0f, 0f, lean * 0.25f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 62f + sp * 20f, Time.deltaTime * 3f);
        }
    }
}
