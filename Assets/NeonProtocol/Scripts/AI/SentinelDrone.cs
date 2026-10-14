using UnityEngine;

namespace NeonProtocol
{
    // The signature drone: patrols a route, sweeps a searchlight, raises the alarm (rivals converge), marks the player
    // with slow paint shots, and shuts down temporarily after enough paint hits. Hover keeps real ground clearance
    // and slows for obstacles ahead (it never sinks into geometry).
    public class SentinelDrone : MonoBehaviour, IPaintHittable
    {
        public enum State { Patrol, Alert, Shutdown }

        public Transform[] route;
        public Transform body;               // visual (tilts with acceleration)
        public Transform[] rotors;
        public Light searchlight;
        public Renderer eye;
        public Transform muzzle;
        public float cruiseSpeed = 3.2f, alertSpeed = 4.5f, hoverHeight = 3.6f, minClearance = 2.2f;
        public float scanAngle = 32f, scanRange = 22f, fireRate = 1.0f, aimError = 4f, muzzleSpeed = 40f;
        public int hitsToShutdown = 6;
        public float shutdownTime = 12f;
        public AudioSource hum;
        public AudioClip alarmClip, shutdownClip, fireClip;

        public State Current { get; private set; } = State.Patrol;
        public int Hits { get; private set; }

        Vector3 vel; int idx; float fireT, alertUntil, shutdownUntil, sweep, lastHitT;
        PlayerMotor player;
        static readonly int idEmission = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock _mpb;
        MaterialPropertyBlock mpb => _mpb ??= new MaterialPropertyBlock();

        void OnEnable() => CompetitorAIRegistry.Drones.Add(this);
        void OnDisable() => CompetitorAIRegistry.Drones.Remove(this);

        void Start() { player = HeistDirector.Instance ? HeistDirector.Instance.player : FindFirstObjectByType<PlayerMotor>(); }

        public bool OnPaintHit(in PaintHit hit)
        {
            if (Current == State.Shutdown || hit.team != 0) return false;
            Hits++; lastHitT = Time.time;
            Alert(hit.shooter ? hit.shooter.transform.position : transform.position);
            if (Hits >= hitsToShutdown)
            {
                Current = State.Shutdown; shutdownUntil = Time.time + shutdownTime; Hits = 0;
                if (hum && shutdownClip) hum.PlayOneShot(shutdownClip);
                HeistDirector.Instance?.Recorder?.Mark(SignatureRecorder.Kind.Drone, transform.position);
            }
            return true;
        }

        public void ForgetPlayer() { if (Current != State.Shutdown) { Current = State.Patrol; alertUntil = 0f; } }

        public void Investigate(Vector3 at) { if (Current != State.Shutdown) Alert(at); }

        void Alert(Vector3 at)
        {
            bool wasPatrol = Current == State.Patrol;
            Current = State.Alert; alertUntil = Time.time + 6f;
            if (wasPatrol) { if (hum && alarmClip) hum.PlayOneShot(alarmClip); NoiseSystem.Emit(at, 32f, this); }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            Vector3 target = pos;
            float speed = cruiseSpeed;

            if (Current == State.Shutdown)
            {
                // drop to a low idle hover, lights off
                vel = Vector3.Lerp(vel, Vector3.zero, dt * 2f);
                float ground = GroundY(pos);
                transform.position = new Vector3(pos.x, Mathf.Lerp(pos.y, ground + 1.1f, dt * 1.5f), pos.z);
                if (Time.time >= shutdownUntil) { Current = State.Alert; alertUntil = Time.time + 4f; }
                Visuals(dt, 0.05f);
                return;
            }

            bool sees = CanSeePlayer();
            if (sees) Alert(player.transform.position);
            if (Current == State.Alert && Time.time > alertUntil && !sees) Current = State.Patrol;

            if (Current == State.Patrol && route != null && route.Length > 0)
            {
                target = route[idx % route.Length].position;
                if ((new Vector3(target.x, pos.y, target.z) - pos).sqrMagnitude < 1f) idx++;
            }
            else if (Current == State.Alert && player)
            {
                // hold a stand-off position above and in front of the player
                Vector3 to = player.transform.position - pos; to.y = 0;
                target = player.transform.position - to.normalized * 9f;
                speed = alertSpeed;
                fireT -= dt;
                if (sees && fireT <= 0f) { fireT = 1f / fireRate; Fire(); }
            }

            // desired altitude above local ground
            float gy = GroundY(new Vector3(target.x, pos.y, target.z));
            target.y = Mathf.Max(gy + hoverHeight, GroundY(pos) + minClearance);
            Vector3 desired = Vector3.ClampMagnitude(target - pos, 1f) * speed;
            // brake for obstacles ahead
            if (desired.sqrMagnitude > 0.01f && Physics.SphereCast(pos, 0.6f, desired.normalized, out var hit, 2.5f, ~((1 << 8) | (1 << 9) | (1 << 10) | (1 << 11) | (1 << 12)), QueryTriggerInteraction.Ignore))
                desired = Vector3.ProjectOnPlane(desired, hit.normal) + hit.normal * 0.5f;
            vel = Vector3.Lerp(vel, desired, dt * 2.2f);
            transform.position = pos + vel * dt;

            // face travel / target
            Vector3 look = Current == State.Alert && player ? player.transform.position - pos : vel;
            look.y = 0;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), dt * 3f);
            Visuals(dt, 1f);
        }

        float GroundY(Vector3 p)
        {
            int mask = ~((1 << 8) | (1 << 9) | (1 << 10) | (1 << 11) | (1 << 12));
            return Physics.Raycast(new Vector3(p.x, p.y + 0.5f, p.z), Vector3.down, out var hit, 40f, mask, QueryTriggerInteraction.Ignore) ? hit.point.y : p.y - hoverHeight;
        }

        bool CanSeePlayer()
        {
            if (!player || player.TaggedOut || !searchlight) return false;
            Vector3 eyePos = searchlight.transform.position;
            Vector3 to = player.transform.position + Vector3.up * 1f - eyePos;
            float d = to.magnitude;
            if (d > scanRange * (player.Crouched ? 0.7f : 1f)) return false;
            if (Vector3.Angle(searchlight.transform.forward, to) > scanAngle) return false;
            int mask = ~((1 << 10) | (1 << 11) | (1 << 12));
            return Physics.Raycast(eyePos, to / d, out var hit, d, mask, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<PlayerMotor>();
        }

        void Fire()
        {
            Vector3 origin = muzzle ? muzzle.position : transform.position + Vector3.down * 0.4f;
            Vector3 target = player.transform.position + Vector3.up * 1.1f;
            float t = Vector3.Distance(origin, target) / muzzleSpeed;
            Vector3 dir = Quaternion.Euler(Random.Range(-aimError, aimError), Random.Range(-aimError, aimError), 0) * (target - origin).normalized;
            float g = PaintProjectiles.Instance ? PaintProjectiles.Instance.gravity : 0f;
            PaintProjectiles.Instance?.Fire(origin, dir * muzzleSpeed + Vector3.up * (0.5f * g * t), 1, this, 10);
            if (hum && fireClip) hum.PlayOneShot(fireClip, 0.6f);
        }

        void Visuals(float dt, float power)
        {
            if (rotors != null) foreach (var r in rotors) if (r) r.Rotate(0, 1400f * power * dt, 0, Space.Self);
            if (body)
            {
                Vector3 localVel = transform.InverseTransformDirection(vel);
                body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(localVel.z * 5f, 0, -localVel.x * 5f) * Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 2.3f) * 2f), dt * 4f);
            }
            if (searchlight)
            {
                sweep += dt * (Current == State.Alert ? 0f : 0.8f);
                var aim = Current == State.Alert && player ? Quaternion.LookRotation(player.transform.position - searchlight.transform.position) : transform.rotation * Quaternion.Euler(38f, Mathf.Sin(sweep) * 40f, 0);
                searchlight.transform.rotation = Quaternion.Slerp(searchlight.transform.rotation, aim, dt * 5f);
                searchlight.enabled = power > 0.5f;
                searchlight.color = Current == State.Alert ? new Color(1f, 0.25f, 0.6f) : new Color(0.55f, 0.75f, 1f);
            }
            if (eye)
            {
                Color c = Current == State.Shutdown ? Color.black : Current == State.Alert ? new Color(4f, 0.3f, 1.6f) : new Color(0.4f, 1.8f, 3f);
                if (Time.time - lastHitT < 0.1f) c *= 3f;
                eye.GetPropertyBlock(mpb); mpb.SetColor(idEmission, c); eye.SetPropertyBlock(mpb);
            }
            if (hum) hum.pitch = Mathf.Lerp(0.6f, 1.15f, power) + vel.magnitude * 0.03f;
        }
    }
}
