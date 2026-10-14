using UnityEngine;

namespace NeonProtocol
{
    // Over-the-shoulder camera.
    //  * Orientation is exactly the mouse aim (yaw/pitch) plus spring-driven visual recoil: no rotational smoothing.
    //  * The pivot follows the character horizontally with no lag; only height is sprung (stairs, crouch, landings).
    //  * Boom length comes from a sphere probe: pulls in instantly when blocked, eases back out.
    //  * The aim ray is cast from the rendered camera, so the crosshair is always the truth.
    [DefaultExecutionOrder(-50)]
    public class ShoulderRig : MonoBehaviour
    {
        public Transform target;                  // character root
        public Camera cam;
        public LayerMask collisionMask = ~0;
        public LayerMask aimMask = ~0;

        [Header("Local first-person camera trial (not a finished weapon view)")]
        public bool firstPersonTrial;
        [Range(55f, 90f)] public float firstPersonFov = 74f;
        [Range(0f, 6f)] public float firstPersonSprintKick = 3f;
        [Range(0f, 1f)] public float firstPersonShake = 0.2f;

        [Header("Framing")]
        public float pivotHeight = 1.55f, crouchPivotHeight = 1.05f;
        public float shoulder = 0.62f, adsShoulder = 0.78f;
        public float distance = 3.1f, adsDistance = 1.45f, zoomDistance = 1.2f;
        public float fov = 72f, adsFov = 50f, zoomFov = 24f, sprintFovKick = 6f;
        public float minPitch = -70f, maxPitch = 78f;
        [Header("Response")]
        public float heightOmega = 16f, modeOmega = 20f, boomOutOmega = 7f;
        public float probeRadius = 0.28f;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public float ShoulderSide { get; private set; } = 1f;   // +1 right, -1 left
        public Vector3 AimPoint { get; private set; }
        public Vector3 AimDirection { get; private set; }
        public Collider AimCollider { get; private set; }
        public bool Crouched { get; set; }
        public bool Sprinting { get; set; }
        public int AimMode { get; set; }   // 0 hip, 1 ADS, 2 zoom
        public float BoomLength => boom;
        public bool CloseToCharacter { get; private set; }

        float pivotY, pivotYVel, modeT, modeTVel, zoomT, zoomTVel, boom, side, sideVel, fovNow;
        float recoilPitch, recoilPitchVel, recoilYaw, recoilYawVel, shake;
        bool initialized;

        void Awake()
        {
            if (!cam) cam = GetComponentInChildren<Camera>();
            if (!cam) cam = Camera.main;
        }

        public void Snap()
        {
            if (!target) return;
            Yaw = target.eulerAngles.y; Pitch = 5f;
            pivotY = target.position.y + pivotHeight;
            boom = distance; side = ShoulderSide; fovNow = fov;
            initialized = true;
        }

        public void ApplyLook(Vector2 look)
        {
            // FOV-proportional sensitivity: the same mouse motion moves the image the same amount when aiming/zoomed
            float baseFov = firstPersonTrial ? firstPersonFov : fov;
            if (fovNow > 1f && baseFov > 1f) look *= Mathf.Tan(fovNow * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad);
            Yaw += look.x;
            Pitch = Mathf.Clamp(Pitch - look.y, minPitch, maxPitch);
        }

        public void ToggleShoulder() => ShoulderSide = -ShoulderSide;

        public void SetAim(float yaw, float pitch) { Yaw = yaw; Pitch = Mathf.Clamp(pitch, minPitch, maxPitch); }

        // Visual + real recoil: the camera kicks up, the aim ray follows the camera, then it recovers.
        public void Kick(float pitchUp, float yawJitter)
        {
            recoilPitchVel += pitchUp * 60f;
            recoilYawVel += yawJitter * 60f;
        }

        public void AddShake(float amount) => shake = Mathf.Min(1f, shake + amount);

        void Update()
        {
            var input = NPInput.Read();
            ApplyLook(input.look);
            if (input.shoulderPressed) ToggleShoulder();
        }

        void LateUpdate()
        {
            if (!target || !cam) return;
            if (!initialized) Snap();
            float dt = Time.deltaTime;

            float ads = AimMode >= 1 ? 1f : 0f, zoom = AimMode == 2 ? 1f : 0f;
            modeT = Springs.Step(modeT, ref modeTVel, ads, modeOmega, dt);
            zoomT = Springs.Step(zoomT, ref zoomTVel, zoom, modeOmega, dt);
            side = Springs.Step(side, ref sideVel, ShoulderSide, 12f, dt);

            float hTarget = target.position.y + (Crouched ? crouchPivotHeight : pivotHeight);
            pivotY = Springs.Step(pivotY, ref pivotYVel, hTarget, heightOmega, dt);
            // never let the sprung height fall far behind (big drops / teleports)
            if (Mathf.Abs(pivotY - hTarget) > 1.5f) pivotY = hTarget;

            recoilPitch = Springs.Step(recoilPitch, ref recoilPitchVel, 0f, 14f, dt);
            recoilYaw = Springs.Step(recoilYaw, ref recoilYawVel, 0f, 14f, dt);
            shake = Mathf.Max(0f, shake - dt * 2.5f);

            float t = Time.time * 31f;
            float sh = shake * shake * Settings.CameraShake * (firstPersonTrial ? firstPersonShake : 1f);
            float shakeP = (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2.2f * sh;
            float shakeY = (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2.2f * sh;

            Quaternion rot = Quaternion.Euler(Pitch - recoilPitch + shakeP, Yaw + recoilYaw + shakeY, 0f);
            Vector3 pivot = new Vector3(target.position.x, pivotY, target.position.z);

            if (firstPersonTrial)
            {
                // Capsule-centered lens, never attached to an animated head bone.
                // No head bob, roll or rotational lag. Keep existing collision-aware movement.
                boom = 0f;
                CloseToCharacter = true; // existing CharacterFade hides the local body/weapon
                transform.SetPositionAndRotation(pivot, rot);
                if (cam.transform != transform) cam.transform.SetPositionAndRotation(pivot, rot);
                float fpFov = Mathf.Lerp(firstPersonFov, 58f, modeT);
                fpFov = Mathf.Lerp(fpFov, zoomFov, zoomT);
                fpFov += (Sprinting ? firstPersonSprintKick : 0f) * (1f - modeT);
                fovNow = Springs.Damp(fovNow, fpFov, 14f, dt);
                cam.fieldOfView = fovNow;
                UpdateAim();
                return;
            }

            float sh0 = Mathf.Lerp(shoulder, adsShoulder, modeT) * side;
            float want = Mathf.Lerp(Mathf.Lerp(distance, adsDistance, modeT), zoomDistance, zoomT);

            // One probe from INSIDE the character (pivot) to the desired lens position: catches walls the shoulder
            // point could already be inside. Pulls in instantly, eases back out.
            Vector3 right = rot * Vector3.right, back = rot * Vector3.back;
            Vector3 desired = pivot + right * sh0 + back * want;
            Vector3 toDesired = desired - pivot; float full = toDesired.magnitude;
            float allowedFrac = 1f;
            if (full > 0.0001f && Physics.SphereCast(pivot, probeRadius, toDesired / full, out var hit, full, collisionMask, QueryTriggerInteraction.Ignore))
                allowedFrac = Mathf.Clamp01((hit.distance - 0.04f) / full);
            float allowed = allowedFrac * full;
            boom = allowed < boom ? allowed : Springs.Damp(boom, allowed, boomOutOmega, dt);
            Vector3 shoulderPt = pivot;   // lens = pivot + direction * boom
            Vector3 lens = full > 0.0001f ? pivot + (toDesired / full) * boom : pivot;
            CloseToCharacter = boom < 0.75f;
            transform.SetPositionAndRotation(lens, rot);
            if (cam.transform != transform) cam.transform.SetPositionAndRotation(transform.position, rot);

            float fTarget = Mathf.Lerp(Mathf.Lerp(fov, adsFov, modeT), zoomFov, zoomT) + (Sprinting ? sprintFovKick : 0f) * (1f - modeT);
            fovNow = Springs.Damp(fovNow, fTarget, 14f, dt);
            cam.fieldOfView = fovNow;

            UpdateAim();
        }

        void UpdateAim()
        {
            Vector3 origin = cam.transform.position, dir = cam.transform.forward;
            // Start the ray at the character's depth so nothing between the lens and the character is hit.
            float skip = Mathf.Max(0f, boom - 0.2f);
            origin += dir * skip;
            AimDirection = dir;
            if (Physics.Raycast(origin, dir, out var hit, 400f, aimMask, QueryTriggerInteraction.Ignore))
            {
                AimPoint = hit.point; AimCollider = hit.collider;
            }
            else { AimPoint = origin + dir * 400f; AimCollider = null; }
        }
    }
}
