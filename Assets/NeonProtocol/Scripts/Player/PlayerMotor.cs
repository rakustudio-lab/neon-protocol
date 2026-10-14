using UnityEngine;

namespace NeonProtocol
{
    // Character movement: responsive first, cinematic second.
    //  * Velocity approaches the input target with high accel / stronger brake (clean direction changes, controlled stops).
    //  * Relaxed: the body turns toward travel. Combat (aiming / recently fired): the body faces the aim and strafes.
    //  * Stairs: CharacterController step offset + ground snapping so descending never "floats".
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour, CharacterAnimator.ISource
    {
        public ShoulderRig rig;
        [Header("Speeds (m/s)")]
        public float runSpeed = 4.6f, sprintSpeed = 6.9f, adsSpeed = 2.6f, crouchSpeed = 2.2f, walkSpeed = 1.8f;
        [Header("Response")]
        public float accel = 38f, brake = 42f, airAccel = 10f;   // stops are crisper than starts (was 30: stops drifted)
        public float gravity = 24f, jumpSpeed = 6.8f, snapSpeed = 8f;
        public float relaxedTurnOmega = 14f, combatTurnOmega = 30f;
        [Header("Capsule")]
        public float standHeight = 1.8f, crouchHeight = 1.15f;

        public CharacterController Controller { get; private set; }
        // Gate 1: animation and other readers get the MEASURED post-collision displacement (not the commanded velocity),
        // so walking into a wall plays idle instead of a treadmill walk. Vertical stays the physics value.
        public Vector3 Velocity => new Vector3(measured.x, velocity.y, measured.z);
        public Vector3 CommandedVelocity => velocity;
        Vector3 measured; bool teleported;
        public Vector3 LocalVelocity => transform.InverseTransformDirection(new Vector3(velocity.x, 0, velocity.z));
        public bool Grounded { get; private set; }
        public bool Crouched { get; private set; }
        public bool Sprinting { get; private set; }
        public bool CombatStance { get; private set; }
        public float LastFireTime { get; set; } = -10f;
        public bool InputLocked { get; set; }
        public float SpeedMultiplier { get; set; } = 1f;
        public event System.Action Jumped, Landed;
        public int AimMode => aimMode;
        public Vector3 AimPoint => rig ? rig.AimPoint : transform.position + transform.forward * 20f;
        public bool TaggedOut { get; set; }
        // turn-in-place (rescue 2026-10-11): the body holds its facing inside a dead-zone and turns with stepping animation beyond it
        public float TurnRate { get; private set; }
        public bool TurningInPlace { get; private set; }
        public bool AnimGrounded => Grounded || airTime < 0.15f;
        public float VerticalSpeed => Grounded ? 0f : velocity.y;
        [Header("Turn in place")]
        public float idleDeadZone = 65f, combatDeadZone = 30f, turnInPlaceOmega = 11f;
        bool turning; float turnSign = 1f; float rootYaw, turnTime, turnRootTotal;
        public void AddRootYaw(float degrees) => rootYaw += degrees;
        [System.NonSerialized] public bool legacyFacing;
        [System.NonSerialized] public bool firstPersonBody;   // true first person: the body faces exactly where the camera looks   // QA A/B only: the pre-rescue facing (body always springs to the camera)

        Vector3 velocity;
        float yaw, yawVel, height, airTime;
        int aimMode;

        void Awake()
        {
            Controller = GetComponent<CharacterController>();
            height = standHeight;
            yaw = transform.eulerAngles.y;
        }

        public void SetAimMode(int m) => aimMode = m;

        Vector3 lastSafe; bool hasSafe; float safeT;
        public static int WorldRecoveries { get; private set; }   // QA: any recovery fails a map run

        public void Teleport(Vector3 pos, float facing)
        {
            Controller.enabled = false;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, facing, 0));
            Controller.enabled = true;
            velocity = Vector3.zero; yaw = facing; yawVel = 0; measured = Vector3.zero; teleported = true;
            if (rig) rig.Snap();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;   // paused: no state changes (crouch toggles etc.) while time is frozen
            if (!Controller.enabled) { velocity = measured = Vector3.zero; Sprinting = CombatStance = TurningInPlace = false; TurnRate = 0f; aimMode = 0; Grounded = true; return; }   // carried (Finality Elevator)
            // safety net: never fall out of the world. Remember the last solid ground; recover there after a long fall.
            safeT -= dt;
            if (Grounded && safeT <= 0f) { safeT = 0.5f; lastSafe = transform.position; hasSafe = true; }
            if (hasSafe && transform.position.y < Mathf.Min(lastSafe.y - 12f, -15f)) { WorldRecoveries++; Teleport(lastSafe + Vector3.up * 0.1f, yaw); Debug.LogWarning("[NP] player fell out of the world; recovered at " + lastSafe); return; }
            var input = InputLocked ? default : NPInput.Read();

            if (input.crouchPressed) TryCrouch(!Crouched);
            if (input.sprint && Crouched && input.move.y > 0.1f) TryCrouch(false);

            float camYaw = rig ? rig.Yaw : yaw;
            Quaternion camFlat = Quaternion.Euler(0, camYaw, 0);
            Vector3 wish = camFlat * new Vector3(input.move.x, 0, input.move.y);

            CombatStance = aimMode > 0 || Time.time - LastFireTime < 1.2f;
            Sprinting = input.sprint && !Crouched && aimMode == 0 && input.move.y > 0.35f && Mathf.Abs(input.move.x) < 0.8f && Time.time - LastFireTime > 0.25f;
            float speed = Crouched ? crouchSpeed : aimMode > 0 ? adsSpeed : Sprinting ? sprintSpeed : input.walk ? walkSpeed : runSpeed;
            Vector3 targetVel = wish * speed * SpeedMultiplier;

            // Horizontal velocity: accelerate toward target, brake harder when stopping or reversing.
            Vector3 hv = new Vector3(velocity.x, 0, velocity.z);
            float rate = Grounded ? (Vector3.Dot(targetVel, hv) < 0f || targetVel.sqrMagnitude < hv.sqrMagnitude ? brake : accel) : airAccel;
            hv = Vector3.MoveTowards(hv, targetVel, rate * dt);

            // Vertical
            if (Grounded)
            {
                velocity.y = -snapSpeed;   // keeps the capsule glued to stairs and slopes on the way down
                if (input.jumpPressed && !Crouched) { velocity.y = jumpSpeed; Grounded = false; Jumped?.Invoke(); }
            }
            else velocity.y -= gravity * dt;
            velocity.x = hv.x; velocity.z = hv.z;

            Vector3 before = transform.position;
            var flags = Controller.Move(velocity * dt);
            Vector3 moved = transform.position - before; moved.y = 0f;
            if (teleported) { teleported = false; measured = Vector3.zero; }
            else measured = Vector3.Lerp(measured, moved / dt, 1f - Mathf.Exp(-30f * dt));   // light smoothing of frame jitter only
            // blocked by a wall: drop the commanded component the wall absorbed, so acceleration restarts from reality
            if ((flags & CollisionFlags.Sides) != 0) { Vector3 m = moved / dt; velocity.x = m.x; velocity.z = m.z; }
            bool wasGrounded = Grounded;
            Grounded = (flags & CollisionFlags.Below) != 0;
            if (!Grounded && wasGrounded && velocity.y < 0f)
            {
                // Walked off a step: try to snap down before declaring airborne.
                if (Physics.SphereCast(transform.position + Vector3.up * (Controller.radius + 0.05f), Controller.radius * 0.95f, Vector3.down, out var hit, Controller.stepOffset + 0.1f, ~0, QueryTriggerInteraction.Ignore))
                {
                    Controller.Move(Vector3.down * (hit.distance - 0.04f));
                    Grounded = true;
                }
            }
            if (Grounded && !wasGrounded && airTime > 0.25f) Landed?.Invoke();
            airTime = Grounded ? 0f : airTime + dt;
            if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0) velocity.y = 0;

            // Facing
            // Shooter convention: the body faces the aim (strafe locomotion), so arms/rifle never fight the torso.
            // Only sprinting turns the body toward travel (sprint clips are forward-only, rifle lowered).
            float want = camYaw;
            float omega = CombatStance ? combatTurnOmega : relaxedTurnOmega;
            bool moving = input.move.sqrMagnitude > 0.01f || hv.sqrMagnitude > 0.25f;
            if (Sprinting && hv.sqrMagnitude > 0.2f) want = Mathf.Atan2(hv.x, hv.z) * Mathf.Rad2Deg;
            else if (!moving && Grounded && !legacyFacing)
            {
                // Standing still: hold the facing while the camera looks around; past the dead-zone, step around to it.
                float delta = Mathf.DeltaAngle(yaw, camYaw);
                if (!turning && Mathf.Abs(delta) > (CombatStance ? combatDeadZone : idleDeadZone)) { turning = true; turnTime = 0f; turnRootTotal = 0f; turnSign = Mathf.Sign(delta); }
                if (turning && (Mathf.Abs(delta) < 8f || Mathf.Sign(delta) != turnSign)) turning = false;
                if (turning)
                {
                    turnTime += dt;
                    // The authored turn rotates the body (feet stay planted). Fallback spring only if no root yaw arrives.
                    float step = Mathf.Clamp(rootYaw, -Mathf.Abs(delta), Mathf.Abs(delta));
                    turnRootTotal += Mathf.Abs(step);
                    if (turnTime > 0.35f && turnRootTotal < 4f) { want = camYaw; omega = turnInPlaceOmega; }
                    else { yaw += step; yawVel = 0f; want = yaw; }
                }
                else { want = yaw; yawVel = Mathf.MoveTowards(yawVel, 0f, 2000f * dt); }
            }
            else turning = false;
            rootYaw = 0f;
            if (firstPersonBody) { turning = false; want = camYaw; yaw = camYaw; yawVel = 0f; }   // arms + rifle stay in front of the eyes
            else
            yaw = Springs.StepAngle(yaw, ref yawVel, want, omega, dt);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            TurningInPlace = turning && !moving;
            TurnRate = TurningInPlace ? turnSign : 0f;

            // Crouch capsule
            float hWant = Crouched ? crouchHeight : standHeight;
            height = Springs.Damp(height, hWant, 18f, dt);
            Controller.height = height;
            Controller.center = new Vector3(0, height * 0.5f, 0);

            if (rig) { rig.Crouched = Crouched; rig.Sprinting = Sprinting && hv.magnitude > runSpeed; }
        }

        void TryCrouch(bool crouch)
        {
            if (!crouch && Crouched)
            {
                // Don't stand up into a ceiling.
                float r = Controller.radius * 0.9f;
                if (Physics.SphereCast(transform.position + Vector3.up * (crouchHeight - r), r, Vector3.up, out _, standHeight - crouchHeight + 0.05f, ~0, QueryTriggerInteraction.Ignore))
                    return;
            }
            Crouched = crouch;
        }
    }
}
