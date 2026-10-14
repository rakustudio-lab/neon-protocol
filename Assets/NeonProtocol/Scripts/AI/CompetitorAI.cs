using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace NeonProtocol
{
    // A nonlethal rival crew member. Perception is honest: a vision cone with line of sight (crouching shortens it),
    // hearing through NoiseSystem, and an awareness meter.
    // Rescue 2026-10-11: rivals belong to FACTIONS (team 1 = VECTOR crew, team 2 = HALO syndicate). They hunt the player
    // AND any rival of another faction, pick the most relevant visible target, only fire when the muzzle has a clear
    // shot, and break line of sight to cover after being hit.
    // States: Patrol -> Investigate -> Engage <-> Cover -> Search -> Patrol, plus Down (tagged out, then reboots).
    [RequireComponent(typeof(NavMeshAgent), typeof(TagHealth))]
    public class CompetitorAI : MonoBehaviour, CharacterAnimator.ISource
    {
        public enum State { Patrol, Investigate, Engage, Search, Down, Cover }

        public Transform[] patrol;
        public Transform muzzle;
        public string displayName = "RIVAL";
        public int team = 1;                       // faction: 1 VECTOR, 2 HALO (player is team 0)
        [Header("Perception")]
        public float viewDistance = 26f, viewAngle = 70f, eyeHeight = 1.6f;
        public float awarenessGain = 2.2f, loseSightTime = 3.5f, searchTime = 7f;
        [Header("Combat")]
        public float fireRate = 3f, burstLength = 0.8f, burstPause = 1.9f, aimErrorDeg = 3.5f, muzzleSpeed = 60f;
        public float preferredRange = 12f;
        [Range(0, 1)] public float coverChance = 0.65f;
        [Header("Readability (Gate 1)")]
        public float telegraphTime = 0.8f;        // visible aim laser + lock tone before the first burst of an engagement
        public float preBurstTell = 0.3f;         // short re-tell before every following burst
        public IrisRelay wakeOnIris;              // first lesson: stay passive until the player reaches this relay step
        public int wakeAt = 1;
        public bool lessonGuard;                  // first encounter: misses more, stays down while the relay is unfinished                    // Iris step that wakes this rival (1 = first pad latched, 4 = relay online)
        public Transform dormantPost;             // where a passive rival holds (off the lesson route), facing the post's forward
        public AudioClip lockClip;
        public Material laserMaterial;
        [Header("Speeds")]
        public float walkSpeed = 1.72f, runSpeed = 4.5f, combatSpeed = 2.6f;

        public State Current { get; private set; } = State.Patrol;
        public float Awareness { get; private set; }
        public bool EngagingPlayer => (Current == State.Engage || Current == State.Cover) && target && target == playerHealth;
        public TagHealth Target => target;

        // CharacterAnimator.ISource
        public Vector3 Velocity => agent ? agent.velocity : Vector3.zero;
        public bool Grounded => true;
        public bool Crouched => false;
        public bool Sprinting => (Current == State.Investigate || Current == State.Cover) && agent && agent.velocity.magnitude > 3.5f;
        public int AimMode => Current == State.Engage ? 1 : 0;
        public bool CombatStance => Current == State.Engage || Current == State.Search || Current == State.Cover;
        public Vector3 AimPoint => aimPoint;
        public bool TaggedOut => health && health.TaggedOut;

        NavMeshAgent agent; TagHealth health; PlayerMotor player; TagHealth playerHealth; CharacterAnimator anim;
        TagHealth target;
        Vector3 lastKnown, aimPoint, strafeTarget, coverPoint; int patrolIdx; float lastSeen = -100f, stateTime, thinkT, fireT, burstT, strafeT;
        bool canSee, hadSight;
        AudioSource audioSrc;
        public AudioClip[] fireClips;
        static readonly int SightMask = ~((1 << 2) | (1 << 11) | (1 << 12));
        LineRenderer laser; float telegraphUntil, laserT;
        float baseDownTime;
        bool woken, wasDormant;                   // a hit always wakes a passive rival; cleared at the start of every run
        [System.NonSerialized] public bool encounterSuspended, stayDownForRun;
        public bool Dormant => encounterSuspended || !woken && wakeOnIris && (wakeOnIris.Next < wakeAt || (wakeAt >= 4 && Time.time < wakeOnIris.DoneAt + 8f)) && HeistDirector.Instance && HeistDirector.Instance.Current == HeistDirector.Phase.Infiltrate;   // 8 s breather after the relay

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>(); health = GetComponent<TagHealth>(); anim = GetComponentInChildren<CharacterAnimator>();
            health.team = team; baseDownTime = health.downTime;
            health.Tagged += () => { SetState(State.Down); if (lastHitTeam == 0) HeistDirector.Instance?.OnRivalTagged(transform.position); };   // only the player's tag-outs count
            health.Recovered += () => { Awareness = 0.6f; SetState(State.Search); foreach (var pd in GetComponentsInChildren<PaintDynamic>()) pd.Clear(); };   // fresh suit after a reboot
            health.Hit += OnHit;
            audioSrc = gameObject.AddComponent<AudioSource>(); audioSrc.spatialBlend = 1f; audioSrc.maxDistance = 40f; audioSrc.rolloffMode = AudioRolloffMode.Logarithmic; audioSrc.minDistance = 3f; audioSrc.playOnAwake = false;
            aimPoint = transform.position + transform.forward * 10f + Vector3.up * 1.4f;
            laser = gameObject.AddComponent<LineRenderer>(); laser.positionCount = 2; laser.widthMultiplier = 0.018f; laser.enabled = false;
            laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; laser.receiveShadows = false;
            if (laserMaterial) laser.sharedMaterial = laserMaterial;
            laser.startColor = laser.endColor = team == 2 ? new Color(1f, 0.75f, 0.2f, 0.9f) : new Color(1f, 0.25f, 0.8f, 0.9f);
        }

        void OnEnable() { NoiseSystem.Heard += OnNoise; CompetitorAIRegistry.All.Add(this); }
        void OnDisable() { NoiseSystem.Heard -= OnNoise; CompetitorAIRegistry.All.Remove(this); }

        void Start()
        {
            player = HeistDirector.Instance ? HeistDirector.Instance.player : FindFirstObjectByType<PlayerMotor>();
            playerHealth = player ? player.GetComponent<TagHealth>() : null;
            // In player builds the agent can initialise before the NavMeshSurface registers its data. R4: agents are saved
            // DISABLED in the scene (no "Failed to create agent" warnings at load) and enabled here, after the surface's OnEnable.
            if (!agent.enabled) agent.enabled = true;
            if (!agent.isOnNavMesh) { agent.enabled = false; agent.enabled = true; if (!agent.isOnNavMesh) agent.Warp(transform.position); }
            if (!agent.isOnNavMesh) Debug.LogWarning("[NP] " + name + " is not on the NavMesh after start");
            GoToPatrol();
            if (HeistDirector.Instance) HeistDirector.Instance.PhaseChanged += p => { if (p == HeistDirector.Phase.Infiltrate) woken = false; };
        }

        int lastHitTeam = -1;
        void OnHit(PaintHit h)
        {
            lastHitTeam = h.team; woken = true;
            anim?.PlayHit();
            var shooterHealth = h.shooter ? h.shooter.GetComponentInParent<TagHealth>() : null;
            lastKnown = h.shooter ? h.shooter.transform.position : transform.position - h.direction * 10f;
            Awareness = Mathf.Max(Awareness, 0.8f);
            if (shooterHealth && shooterHealth.team != team) { target = shooterHealth; Awareness = 1f; }
            if (Current == State.Down) return;
            if (Current == State.Engage && Random.value < coverChance && FindCover(lastKnown, out coverPoint)) { SetState(State.Cover); return; }
            if (Current != State.Engage && Current != State.Cover) SetState(State.Investigate);
        }

        void OnNoise(NoiseSystem.Noise n)
        {
            if (Dormant) return;
            if (Current == State.Down || Current == State.Engage || Current == State.Cover) return;
            if (n.source && n.source.transform.IsChildOf(transform)) return;
            if (n.source is CompetitorAI c && c.team == team) return;      // our own crew
            if ((n.position - transform.position).sqrMagnitude > n.radius * n.radius) return;
            lastKnown = n.position;
            if (Current != State.Investigate) SetState(State.Investigate);
            if (agent.isOnNavMesh) agent.SetDestination(lastKnown);
        }

        void SetState(State s)
        {
            Current = s; stateTime = 0f;
            if (!agent.isOnNavMesh) return;
            agent.isStopped = s == State.Down;
            agent.updateRotation = s != State.Engage;
            agent.speed = s == State.Patrol ? walkSpeed : s == State.Engage ? combatSpeed : runSpeed;
            if (s == State.Patrol) GoToPatrol();
            if (s == State.Search || s == State.Investigate) agent.SetDestination(lastKnown);
            if (s == State.Cover) agent.SetDestination(coverPoint);
            if (s == State.Engage)
            {
                NoiseSystem.Emit(target ? target.transform.position : transform.position, 22f, this);   // call it out
                telegraphUntil = Time.time + telegraphTime; burstT = 0f;                                 // readable acquisition before any paint flies
                if (audioSrc && lockClip) { audioSrc.pitch = 1f; audioSrc.PlayOneShot(lockClip, 0.8f); }
            }
        }

        public void ResetEncounter(Transform post)
        {
            stayDownForRun = true; encounterSuspended = true; woken = false;
            team = 2; health.team = 2; health.hitsToTag = 5; health.hitMemory = 6f;
            health.downTime = 900f; health.Revive(); health.InvulnerableUntil = 0f;
            target = null; canSee = hadSight = false; Awareness = 0f; lastHitTeam = -1;
            dormantPost = post; patrol = new[] { post }; lessonGuard = false;
            agent.Warp(post.position); transform.rotation = post.rotation;
            telegraphTime = 0.85f; aimErrorDeg = 4.5f; preferredRange = 9f;
            SetState(State.Patrol);
        }

        public void Wake() => woken = true;   // QA/perf: leave the passive lesson state

        public void ForgetPlayer()
        {
            if (Current == State.Down) return;
            if (target && target != playerHealth) return;   // busy with another crew
            Awareness = 0f; lastSeen = -100f; target = null; SetState(State.Patrol);
        }

        void GoToPatrol()
        {
            if (!agent.isOnNavMesh) return;
            if (Dormant && dormantPost) { agent.SetDestination(dormantPost.position); return; }
            if (patrol == null || patrol.Length == 0) return;
            var p = patrol[patrolIdx % patrol.Length]; if (!p) return;
            agent.SetDestination(p.position);
        }

        // --- targets ---------------------------------------------------------------------------------------------
        static Vector3 Chest(TagHealth t) { var pm = t.GetComponent<PlayerMotor>(); return t.transform.position + Vector3.up * (pm && pm.Crouched ? 0.85f : 1.3f); }
        static Vector3 Vel(TagHealth t) { var pm = t.GetComponent<PlayerMotor>(); if (pm) return pm.Velocity; var ag = t.GetComponent<NavMeshAgent>(); return ag ? ag.velocity : Vector3.zero; }
        bool Hostile(TagHealth t) => t && t != health && t.team != team && !t.TaggedOut && Time.time >= t.InvulnerableUntil && t.isActiveAndEnabled;

        bool Visible(TagHealth t, bool wide)
        {
            if (!Hostile(t)) return false;
            Vector3 eye = transform.position + Vector3.up * eyeHeight, tp = Chest(t);
            Vector3 to = tp - eye; float d = to.magnitude;
            var pm = t.GetComponent<PlayerMotor>();
            float range = viewDistance * (pm && pm.Crouched ? 0.55f : 1f) * (wide ? 1.4f : 1f);
            if (d > range) return false;
            if (Vector3.Angle(transform.forward, to) > viewAngle * (wide ? 1.6f : 1f) && d > 2.5f) return false;
            if (Physics.Raycast(eye, to / d, out var hit, d, SightMask, QueryTriggerInteraction.Ignore))
                return hit.collider.GetComponentInParent<TagHealth>() == t;
            return true;
        }

        // Choose what to fight: keep a visible current target; otherwise the closest visible hostile (player or other crew).
        void Perceive()
        {
            bool wide = Current == State.Engage || Current == State.Search || Current == State.Cover;
            if (target && Visible(target, wide)) { canSee = true; return; }
            TagHealth best = null; float bd = float.MaxValue;
            if (playerHealth && player && !player.TaggedOut && Visible(playerHealth, wide)) { best = playerHealth; bd = (player.transform.position - transform.position).sqrMagnitude * 0.8f; }   // the thief is the priority
            foreach (var ai in CompetitorAIRegistry.All)
            {
                if (!ai || ai == this || ai.team == team || ai.TaggedOut || ai.Dormant) continue;   // passive rivals are not part of the fight yet
                var th = ai.health; float d = (ai.transform.position - transform.position).sqrMagnitude;
                if (d < bd && Visible(th, wide)) { best = th; bd = d; }
            }
            if (best) target = best;
            canSee = best != null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            stateTime += dt;
            // first lesson: a tagged-out lesson rival does not reboot behind the player while the relay is unfinished
            health.downTime = stayDownForRun ? 900f : wakeOnIris && (lessonGuard || wakeAt <= 1) && !wakeOnIris.Done ? 30f : baseDownTime;
            if (Current == State.Down) return;
            if (target && !Hostile(target)) { target = null; canSee = false; if (Current == State.Engage || Current == State.Cover) SetState(State.Search); }

            thinkT -= dt;
            if (thinkT <= 0f) { thinkT = 0.12f; if (Dormant) { canSee = false; target = null; } else Perceive(); }
            laserT = Mathf.Max(0f, laserT - dt);
            if (laser) { laser.enabled = laserT > 0f && Current == State.Engage && target && muzzle; if (laser.enabled) { laser.SetPosition(0, muzzle.position); Vector3 lv = aimPoint - muzzle.position; laser.SetPosition(1, muzzle.position + lv * Mathf.Max(0.3f, 1f - 1.1f / Mathf.Max(1.2f, lv.magnitude)));   /* stop ~1 m short: a line, not a beam into the victim's camera */ laser.widthMultiplier = 0.012f + 0.01f * Mathf.Abs(Mathf.Sin(Time.time * 40f)); } }

            // lost sight behind cover -> must reacquire: a short re-tell before any paint flies again
            if (canSee && !hadSight && Current == State.Engage) { telegraphUntil = Mathf.Max(telegraphUntil, Time.time + 0.45f); if (audioSrc && lockClip) { audioSrc.pitch = 1.15f; audioSrc.PlayOneShot(lockClip, 0.5f); } }
            hadSight = canSee;
            if (canSee && target)
            {
                lastSeen = Time.time; lastKnown = target.transform.position;
                float d = Vector3.Distance(transform.position, lastKnown);
                Awareness = Mathf.Min(1.2f, Awareness + awarenessGain * dt * Mathf.Lerp(2.2f, 0.5f, d / viewDistance));
                if (Awareness >= 1f && Current != State.Engage && Current != State.Cover) SetState(State.Engage);
            }
            else Awareness = Mathf.Max(0f, Awareness - dt * (Current == State.Engage ? 0.1f : 0.35f));

            switch (Current)
            {
                case State.Patrol:
                    if (Dormant != wasDormant) { wasDormant = Dormant; GoToPatrol(); }
                    if (wasDormant && dormantPost)
                    {   // hold the post: stand still facing the post's direction (no blind walking past the player)
                        if (!agent.pathPending && agent.remainingDistance < 0.4f) transform.rotation = Quaternion.Slerp(transform.rotation, dormantPost.rotation, dt * 4f);
                        aimPoint = Vector3.Lerp(aimPoint, transform.position + transform.forward * 10f + Vector3.up * 1.4f, dt * 4f);
                        break;
                    }
                    aimPoint = Vector3.Lerp(aimPoint, transform.position + transform.forward * 10f + Vector3.up * 1.4f, dt * 4f);
                    if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance < 0.6f) { patrolIdx++; GoToPatrol(); }
                    break;
                case State.Investigate:
                    aimPoint = Vector3.Lerp(aimPoint, lastKnown + Vector3.up * 1.3f, dt * 4f);
                    if (agent.isOnNavMesh && (!agent.pathPending && agent.remainingDistance < 1.2f || stateTime > 12f)) SetState(State.Search);
                    break;
                case State.Search:
                    aimPoint = transform.position + Quaternion.Euler(0, Mathf.Sin(stateTime * 1.2f) * 80f, 0) * transform.forward * 10f + Vector3.up * 1.4f;
                    if (stateTime > searchTime) { Awareness = 0f; target = null; SetState(State.Patrol); }
                    break;
                case State.Engage:
                    Engage(dt);
                    break;
                case State.Cover:
                    aimPoint = Vector3.Lerp(aimPoint, lastKnown + Vector3.up * 1.3f, dt * 6f);
                    // hold cover briefly once there, then peek back into the fight
                    if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance < 0.5f && stateTime > 1.8f || stateTime > 4.5f) SetState(target ? State.Engage : State.Search);
                    break;
            }
        }

        void Engage(float dt)
        {
            if (!target || Time.time - lastSeen > loseSightTime) { SetState(State.Search); return; }
            Vector3 tPos = target.transform.position, chest = Chest(target);
            Vector3 toP = tPos - transform.position; toP.y = 0;
            float d = toP.magnitude;
            if (toP.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toP), dt * 8f);
            // keep a preferred range with a little strafing; reposition at once if the muzzle has no clear shot
            strafeT -= dt;
            bool clear = ClearShot(chest);
            if (strafeT <= 0f || (!clear && strafeT > 0.6f))
            {
                strafeT = clear ? Random.Range(1.2f, 2.4f) : 0.6f;
                Vector3 side = Vector3.Cross(Vector3.up, toP.normalized) * Random.Range(-4f, 4f);
                Vector3 range = -toP.normalized * (preferredRange - d) * 0.6f;
                if (NavMesh.SamplePosition(transform.position + side + range, out var hit, 3f, NavMesh.AllAreas)) strafeTarget = hit.position;
                if (agent.isOnNavMesh) agent.SetDestination(canSee ? strafeTarget : lastKnown);
            }
            aimPoint = Vector3.Lerp(aimPoint, canSee ? chest : lastKnown + Vector3.up * 1.3f, dt * 10f);
            if (Time.time < telegraphUntil) { laserT = 0.1f; return; }       // acquiring: laser on, no paint yet
            burstT += dt;
            float cycle = burstLength + burstPause, phase = burstT % cycle;
            bool inBurst = phase < burstLength;
            if (phase > cycle - preBurstTell && canSee) laserT = 0.1f;       // re-tell just before each burst
            if (inBurst && canSee) laserT = 0.1f;
            fireT -= dt;
            if (canSee && clear && inBurst && fireT <= 0f && Awareness >= 1f) { fireT = 1f / fireRate; Fire(chest, Vel(target)); }
        }

        // The eye can see over low cover that the muzzle cannot shoot over: don't waste paint on the crate.
        bool ClearShot(Vector3 chest)
        {
            Vector3 origin = muzzle ? muzzle.position : transform.position + Vector3.up * 1.4f;
            Vector3 to = chest - origin; float d = to.magnitude; if (d < 0.5f) return true;
            if (Physics.SphereCast(origin, 0.06f, to / d, out var hit, d - 0.3f, SightMask & ~(1 << 9) & ~(1 << 8), QueryTriggerInteraction.Ignore))
                return hit.collider.GetComponentInParent<TagHealth>() == target;
            return true;
        }

        // A nearby NavMesh point that the threat cannot see (chest-high ray blocked), not further than ~7 m.
        bool FindCover(Vector3 threat, out Vector3 point)
        {
            point = transform.position; Vector3 eye = threat + Vector3.up * 1.5f;
            float best = float.MaxValue; bool found = false;
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f * Mathf.Deg2Rad; float r = 2.5f + (i % 3) * 2f;
                Vector3 p = transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                if (!NavMesh.SamplePosition(p, out var nh, 1.5f, NavMesh.AllAreas)) continue;
                Vector3 c = nh.position + Vector3.up * 1.0f;
                if (!Physics.Linecast(eye, c, SightMask & ~(1 << 9) & ~(1 << 8), QueryTriggerInteraction.Ignore)) continue;   // still exposed
                float score = (nh.position - transform.position).sqrMagnitude;
                if (score < best) { best = score; point = nh.position; found = true; }
            }
            return found;
        }

        void Fire(Vector3 tgt, Vector3 tgtVel)
        {
            Vector3 origin = muzzle ? muzzle.position : transform.position + Vector3.up * 1.4f + transform.forward * 0.6f;
            float t = Vector3.Distance(origin, tgt) / muzzleSpeed;
            tgt += tgtVel * t * 0.7f;
            Vector3 dir = (tgt - origin).normalized;
            float err = wakeOnIris && (lessonGuard || wakeAt <= 1) && !wakeOnIris.Done ? aimErrorDeg * 2.2f : aimErrorDeg;   // first lesson: a survivable warning, not an execution
            dir = Quaternion.Euler(Random.Range(-err, err), Random.Range(-err, err), 0) * dir;
            float g = PaintProjectiles.Instance ? PaintProjectiles.Instance.gravity : 0f;
            Vector3 vel = dir * muzzleSpeed + Vector3.up * (0.5f * g * t);
            PaintProjectiles.Instance?.Fire(origin, vel, team, this, 31);   // rivals can hit other crews; own body skipped by PaintProjectiles
            anim?.Kick(0.6f);
            if (audioSrc && fireClips != null && fireClips.Length > 0) { audioSrc.pitch = Random.Range(0.85f, 0.95f); audioSrc.PlayOneShot(fireClips[Random.Range(0, fireClips.Length)], 0.7f); }
        }
    }
}
