using UnityEngine;

namespace NeonProtocol
{
    // The one exceptional weapon. Camera and weapon never disagree:
    //  * the target is the camera's aim point (ShoulderRig.AimPoint);
    //  * the ball leaves the muzzle aimed at that point, with gravity compensated so it lands under the crosshair;
    //  * if something sits between muzzle and target (a wall edge next to the shoulder), the ball honestly hits it.
    public class PaintRifle : MonoBehaviour
    {
        public ShoulderRig rig;
        public PlayerMotor motor;
        public Transform muzzle;
        public int team = 0;

        [Header("Handling")]
        public float fireRate = 8.5f;
        public float muzzleSpeed = 78f;
        public int magazine = 28;
        public float reloadTime = 1.45f;     // full mechanical cycle: release, eject, fetch, insert, pressurize
        public const float SeatAt = 0.74f;   // fraction of the cycle when the new canister seats (ammo is committed here)
        public int lowAmmo = 7;
        public float hipSpread = 1.1f, adsSpread = 0.22f, zoomSpread = 0.05f, moveSpread = 0.9f, bloomPerShot = 0.25f, bloomMax = 1.6f;
        public float hipKick = 0.55f, adsKick = 0.32f;

        [Header("Feedback")]
        public AudioSource fireSource;
        public AudioClip[] fireClips;
        public AudioClip reloadClip, emptyClip;
        public Light muzzleLight;
        public Renderer muzzleFlash;

        public enum Phase { Ready, Release, Eject, Fetch, Insert, Pressurize }
        public Phase ReloadPhase { get; private set; } = Phase.Ready;
        public float Pressure { get; private set; } = 1f;   // conduit glow / HUD gauge: 0 while the chamber is open, refills while pressurizing
        public bool CanisterSeated { get; private set; } = true;
        public string StateLabel => Reloading ? (ReloadPhase == Phase.Pressurize ? "PRESSURIZING" : ReloadPhase == Phase.Fetch ? "NEW CANISTER" : ReloadPhase == Phase.Insert ? "INSERTING" : "EJECTING")
                                              : Ammo == 0 ? "EMPTY · PRESS R" : Ammo <= lowAmmo ? "LOW PAINT · R TO RELOAD" : "READY";
        public int Ammo { get; private set; }
        public bool Reloading { get; private set; }
        public float ReloadProgress { get; private set; }
        public float CurrentSpread { get; private set; }
        public int AimMode { get; private set; }
        public int ShotsFired { get; private set; }
        public event System.Action Fired, ReloadStarted, ReloadFinished, ReloadCancelled, CanisterSeatedEvent;

        float cooldown, bloom, flashT, reloadT;
        int ignoreLayer;
        bool pendingFire;

        void Awake()
        {
            var driver = GetComponent<RifleFrameDriver>();
            if (!driver) driver = gameObject.AddComponent<RifleFrameDriver>();
            driver.rifle = this;
            Ammo = magazine;
            ignoreLayer = gameObject.layer;
            if (motor) ignoreLayer = motor.gameObject.layer;
        }

        // Tag-out / respawn: abort any reload (nothing is committed unless the canister seated), restock fully.
        public void Refill() { Ammo = magazine; Reloading = false; ReloadPhase = Phase.Ready; Pressure = 1f; CanisterSeated = true; ReloadCancelled?.Invoke(); }
        public void CancelReload() { if (!Reloading) return; Reloading = false; ReloadPhase = Phase.Ready; if (!CanisterSeated) { CanisterSeated = true; } Pressure = 1f; ReloadCancelled?.Invoke(); }

        void Update()
        {
            float dt = Time.deltaTime;
            var input = motor && motor.InputLocked ? default : NPInput.Read();

            // Aim mode: RMB = ADS, V / MMB toggles the scope zoom while aiming.
            if (input.aim) { if (input.zoomPressed) AimMode = AimMode == 2 ? 1 : 2; else if (AimMode == 0) AimMode = 1; }
            else AimMode = 0;
            if (motor && motor.Sprinting && AimMode == 0) { /* sprint lowers the rifle; firing cancels sprint below */ }
            if (rig) rig.AimMode = AimMode;
            if (motor) motor.SetAimMode(AimMode);

            if (input.reloadPressed && !Reloading && Ammo < magazine) StartReload();
            if (Reloading)
            {
                reloadT += dt; ReloadProgress = Mathf.Clamp01(reloadT / reloadTime);
                float k = ReloadProgress;
                ReloadPhase = k < 0.16f ? Phase.Release : k < 0.36f ? Phase.Eject : k < 0.56f ? Phase.Fetch : k < SeatAt ? Phase.Insert : Phase.Pressurize;
                if (!CanisterSeated && k >= SeatAt) { CanisterSeated = true; Ammo = magazine; CanisterSeatedEvent?.Invoke(); }   // commit only here
                Pressure = k < 0.16f ? Mathf.Lerp(1f, 0f, k / 0.16f) : k < SeatAt ? 0f : Mathf.Clamp01((k - SeatAt) / (1f - SeatAt));
                if (reloadT >= reloadTime) { Reloading = false; ReloadPhase = Phase.Ready; Pressure = 1f; ReloadFinished?.Invoke(); }
            }

            cooldown -= dt;
            bloom = Mathf.Max(0f, bloom - dt * 3.2f);
            pendingFire = input.fire && !Reloading; // resolve after current camera and muzzle poses

            float baseSpread = AimMode == 2 ? zoomSpread : AimMode == 1 ? adsSpread : hipSpread;
            float speed01 = motor ? Mathf.Clamp01(new Vector2(motor.Velocity.x, motor.Velocity.z).magnitude / motor.runSpeed) : 0f;
            CurrentSpread = baseSpread + bloom + moveSpread * speed01 * (AimMode == 0 ? 1f : 0.35f) + (motor && !motor.Grounded ? 2f : 0f);
            if (motor && motor.Crouched) CurrentSpread *= 0.7f;

            flashT -= dt;
            if (muzzleLight) { muzzleLight.enabled = flashT > 0f; muzzleLight.intensity = Mathf.Max(0f, flashT) * 40f; }
            if (muzzleFlash) muzzleFlash.enabled = flashT > 0.02f;
        }

        // Called once, after ShoulderRig and FirstPersonView LateUpdate.
        public void ResolvePendingFire()
        {
            bool fire = pendingFire; pendingFire = false;
            if (!fire || !isActiveAndEnabled || Time.deltaTime <= 0f || Reloading || cooldown > 0f || (motor && motor.InputLocked)) return;
            if (Ammo > 0) Shoot();
            else { StartReload(); if (emptyClip && fireSource) fireSource.PlayOneShot(emptyClip, 0.6f); cooldown = 0.3f; }
        }

        void OnDisable() { pendingFire = false; }

        void StartReload()
        {
            if (Reloading) return;
            Reloading = true; reloadT = 0f; ReloadProgress = 0f; CanisterSeated = false; ReloadPhase = Phase.Release;
            if (reloadClip && fireSource) fireSource.PlayOneShot(reloadClip, 0.7f);
            ReloadStarted?.Invoke();
        }

        void Shoot()
        {
            cooldown += 1f / fireRate;
            if (cooldown < 0f) cooldown = 0f;
            Ammo--; ShotsFired++;
            if (motor) motor.LastFireTime = Time.time;

            Vector3 origin = muzzle ? muzzle.position : transform.position;
            Vector3 target = rig ? rig.AimPoint : origin + transform.forward * 50f;

            // Honest obstruction check: muzzle -> target.
            Vector3 toT = target - origin;
            float dist = toT.magnitude;
            Vector3 dir = dist > 0.01f ? toT / dist : (rig ? rig.AimDirection : transform.forward);

            // Spread as a cone around the aim direction.
            float s = CurrentSpread * Mathf.Deg2Rad;
            Vector2 r = Random.insideUnitCircle * Mathf.Tan(s);
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized; if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            Vector3 up = Vector3.Cross(dir, right);
            dir = (dir + right * r.x + up * r.y).normalized;

            // Gravity compensation so a still target under the crosshair is hit exactly.
            float g = PaintProjectiles.Instance ? PaintProjectiles.Instance.gravity : 0f;
            float tFlight = dist / muzzleSpeed;
            Vector3 vel = dir * muzzleSpeed + Vector3.up * (0.5f * g * tFlight);

            PaintProjectiles.Instance?.Fire(origin, vel, team, motor ? (Component)motor : this, ignoreLayer);

            bloom = Mathf.Min(bloomMax, bloom + bloomPerShot * (AimMode > 0 ? 0.5f : 1f));
            if (rig) { rig.Kick(AimMode > 0 ? adsKick : hipKick, Random.Range(-0.12f, 0.12f)); rig.AddShake(0.06f); }
            flashT = 0.05f;
            if (muzzleFlash) muzzleFlash.enabled = true;
            if (muzzleLight) { muzzleLight.enabled = true; muzzleLight.intensity = flashT * 40f; }
            if (fireSource && fireClips != null && fireClips.Length > 0)
            {
                fireSource.pitch = Random.Range(0.96f, 1.05f);
                fireSource.PlayOneShot(fireClips[Random.Range(0, fireClips.Length)], 0.8f);
            }
            NoiseSystem.Emit(origin, 26f, motor ? (Component)motor : this);
            Fired?.Invoke();
        }
    }
}
