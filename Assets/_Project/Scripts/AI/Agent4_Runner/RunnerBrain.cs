using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ScreamerAI;

namespace ZombieGame
{
    /// <summary>
    /// THE RUNNER (Member 4 - IS agent): a fast, aggressive zombie.
    ///  * Pathfinding: its own A* on NavGrid with a re-routing cost for closed doors and barricades.
    ///    If a path gets blocked it recalculates (forgetting nothing, but treating the obstacle as impassable for a while)
    ///    instead of getting stuck; closed doors are shouldered open, barricades are routed around.
    ///  * It sprints straight at the player once it has seen / heard them.
    ///  * It gets FASTER as the player's health drops (tension).
    /// Movement is done by the AgentMotor (the Agent Controller): it consumes the path array.
    /// </summary>
    [RequireComponent(typeof(AgentMotor))]
    public class RunnerBrain : MonoBehaviour, IAlertListener
    {
        public enum State { Roam, Hunt, Search, Attack }

        public Transform player;

        [Header("Senses")]
        public float sightRange = 28f;
        public float sightAngle = 130f;
        public float closeSense = 5f;
        public float eyeHeight = 1.6f;
        public float hearingSensitivity = 1f;
        public float loseInterestSeconds = 9f;

        [Header("Speed (m/s)")]
        public float roamSpeed = 1.3f;
        public float baseSpeed = 4.6f;
        public float maxSpeed = 7.4f;

        [Header("Attack")]
        public float attackRange = 1.45f;
        public float attackDamage = 9f;
        public float attackWindup = 0.3f;
        public float attackCooldown = 0.9f;

        [Header("Pathfinding")]
        public float repathInterval = 0.4f;
        public float doorBashDelay = 0.9f;
        public float forbidSeconds = 12f;
        public float clawDamage = 6f;

        public State CurrentState { get; private set; }
        public bool LastPathPartial { get; private set; }
        public float CurrentTopSpeed { get; private set; }

        public Transform AlertTransform { get { return transform; } }

        AgentMotor motor;
        ZombieHealth health;
        PlayerStats stats;
        AudioSource voice;
        readonly List<Vector3> pathBuf = new List<Vector3>(64);
        readonly List<ForbiddenZone> forbidden = new List<ForbiddenZone>();

        struct ForbiddenZone { public Bounds bounds; public float until; }

        Vector3 target;
        bool hasTarget;
        float lastSensed;
        float nextSense;
        float nextRepath;
        float nextRoam;
        float nextGrowl;
        float bashTimer;
        float attackStart = -1f;
        float nextAttack;
        float nextClaw;
        bool sees;
        float distToPlayer = 999f;

        public string DebugInfo
        {
            get
            {
                return "Runner  " + CurrentState + "  v=" + motor.CurrentSpeed.ToString("0.0") + "/" + CurrentTopSpeed.ToString("0.0")
                       + (LastPathPartial ? "  REROUTE" : "") + (motor.IsBlocked ? "  STUCK" : "");
            }
        }

        void Awake()
        {
            motor = GetComponent<AgentMotor>();
            health = GetComponent<ZombieHealth>();
            voice = GetComponent<AudioSource>();
            if (voice == null) voice = gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 1f;
            voice.minDistance = 3f;
            voice.maxDistance = 35f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.playOnAwake = false;
            nextGrowl = Time.time + Random.Range(3f, 8f);
        }

        void Start()
        {
            if (player == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) player = p.transform;
            }
            if (player != null) stats = player.GetComponent<PlayerStats>();
            CurrentState = State.Roam;
            nextRoam = Time.time + Random.Range(0.5f, 2f);
        }

        void OnEnable()
        {
            AlertSystem.Register(this);
            NoiseSystem.NoiseEmitted += OnNoise;
        }

        void OnDisable()
        {
            AlertSystem.Unregister(this);
            NoiseSystem.NoiseEmitted -= OnNoise;
        }

        // ------------------------------------------------------------------ stimuli
        public void OnAlert(AlertEvent alert)
        {
            if (!health.IsAlive) return;
            SetTarget(alert.TargetPosition, State.Search);
        }

        void OnNoise(NoiseEvent e)
        {
            if (!health.IsAlive || CurrentState == State.Attack) return;
            if (sees) return;   // already has eyes on the player
            if (!NoiseSystem.CanHear(e, transform.position, hearingSensitivity)) return;
            SetTarget(e.position, State.Search);
        }

        void SetTarget(Vector3 pos, State next)
        {
            target = pos;
            hasTarget = true;
            lastSensed = Time.time;
            if (CurrentState != State.Hunt) CurrentState = next;
            nextRepath = 0f;
            if (CurrentState == State.Search) Growl(GameAudio.Growl, 0.7f);
        }

        // ------------------------------------------------------------------ update
        void Update()
        {
            if (player == null || !health.IsAlive || NavGrid.Instance == null) return;
            float dt = Time.deltaTime;

            // speed scales with the player's lost health
            float desperation = stats != null ? 1f - stats.HealthFraction : 0f;
            CurrentTopSpeed = (CurrentState == State.Roam) ? roamSpeed : Mathf.Lerp(baseSpeed, maxSpeed, desperation);
            motor.maxSpeed = CurrentTopSpeed;
            motor.RunBlendReference = Mathf.Max(3f, maxSpeed);

            if (Time.time >= nextSense)
            {
                nextSense = Time.time + 0.12f;
                Sense();
            }

            switch (CurrentState)
            {
                case State.Roam: TickRoam(); break;
                case State.Hunt: TickHunt(); break;
                case State.Search: TickSearch(); break;
                case State.Attack: TickAttack(dt); break;
            }

            if (motor.IsBlocked && motor.HasPath && CurrentState != State.Attack) HandleBlocked(dt);
            else bashTimer = 0f;

            if (CurrentState != State.Roam && Time.time >= nextGrowl)
            {
                nextGrowl = Time.time + Random.Range(5f, 11f);
                Growl(GameAudio.Growl, 0.6f);
            }
        }

        void Sense()
        {
            bool wasSeeing = sees;
            sees = Senses.CanSee(transform, eyeHeight, player, sightRange, sightAngle, closeSense, out distToPlayer);
            if (sees)
            {
                target = player.position;
                hasTarget = true;
                lastSensed = Time.time;
                if (CurrentState == State.Roam || CurrentState == State.Search)
                {
                    CurrentState = State.Hunt;
                    nextRepath = 0f;
                    if (!wasSeeing) Growl(GameAudio.Roar, 1f);
                }
            }
        }

        // ------------------------------------------------------------------ states
        void TickRoam()
        {
            if (!motor.HasPath && Time.time >= nextRoam)
            {
                Vector3 p;
                if (NavGrid.Instance.RandomPointNear(transform.position, 12f, out p)) RepathTo(p);
                nextRoam = Time.time + Random.Range(2f, 5f);
            }
        }

        void TickHunt()
        {
            if (sees)
            {
                if (distToPlayer <= attackRange && Mathf.Abs(player.position.y - transform.position.y) < 1.6f)
                {
                    EnterAttack();
                    return;
                }
                if (Time.time >= nextRepath)
                {
                    nextRepath = Time.time + repathInterval;
                    RepathToPlayer();
                }
            }
            else
            {
                // lost sight: run to the last known position, then search
                if (Time.time >= nextRepath)
                {
                    nextRepath = Time.time + repathInterval * 2f;
                    RepathTo(target);
                }
                if (!motor.HasPath || Planar(transform.position, target) < 1.5f) CurrentState = State.Search;
            }
            ClawIfTrapped();
        }

        void TickSearch()
        {
            if (Time.time >= nextRepath && hasTarget)
            {
                nextRepath = Time.time + repathInterval * 3f;
                if (Planar(transform.position, target) > 1.5f) RepathTo(target);
            }
            if (Time.time - lastSensed > loseInterestSeconds)
            {
                hasTarget = false;
                CurrentState = State.Roam;
                motor.Stop();
                nextRoam = Time.time + Random.Range(1f, 3f);
            }
            ClawIfTrapped();
        }

        void EnterAttack()
        {
            CurrentState = State.Attack;
            motor.Stop();
            attackStart = -1f;
        }

        void TickAttack(float dt)
        {
            bool inReach = distToPlayer <= attackRange * 1.4f && Mathf.Abs(player.position.y - transform.position.y) < 1.8f;
            if (!inReach || !sees)
            {
                motor.SetAttackPose(0f);
                attackStart = -1f;
                CurrentState = State.Hunt;
                nextRepath = 0f;
                return;
            }

            motor.FaceTowards(player.position, dt);

            if (attackStart < 0f && Time.time >= nextAttack)
            {
                attackStart = Time.time;
                motor.SetAttackPose(1f);
                Growl(GameAudio.Growl, 0.5f);
            }
            else if (attackStart >= 0f && Time.time - attackStart >= attackWindup)
            {
                IDamageable d = player.GetComponent<IDamageable>();
                if (d != null && d.IsAlive)
                    d.TakeDamage(new DamageInfo(attackDamage, player.position + Vector3.up, (player.position - transform.position).normalized, false, gameObject));
                nextAttack = Time.time + attackCooldown;
                attackStart = -1f;
                motor.SetAttackPose(0f);
            }
        }

        // ------------------------------------------------------------------ pathfinding glue
        void RepathToPlayer()
        {
            // sprint straight at the player when nothing is in the way
            NavMeshHit h;
            Vector3 snapped = player.position;
            if (NavMesh.SamplePosition(player.position, out h, 2f, NavMesh.AllAreas)) snapped = h.position;
            NavMeshHit edge;
            if (!NavMesh.Raycast(transform.position, snapped, out edge, NavMesh.AllAreas) && Mathf.Abs(snapped.y - transform.position.y) < 0.6f)
            {
                pathBuf.Clear();
                pathBuf.Add(transform.position);
                pathBuf.Add(snapped);
                motor.SetPath(pathBuf);
                LastPathPartial = false;
                return;
            }
            RepathTo(snapped);
        }

        void RepathTo(Vector3 dest)
        {
            bool partial;
            bool ok = NavGrid.Instance.FindPath(transform.position, dest, pathBuf, out partial, IsForbiddenNode);
            if (ok)
            {
                motor.SetPath(pathBuf);
                LastPathPartial = partial;
            }
            else
            {
                motor.Stop();
                LastPathPartial = true;
            }
        }

        bool IsForbiddenNode(int node)
        {
            if (forbidden.Count == 0) return false;
            Vector3 p = NavGrid.Instance.NodePos(node);
            for (int i = 0; i < forbidden.Count; i++)
                if (forbidden[i].bounds.Contains(p)) return true;
            return false;
        }

        void PruneForbidden()
        {
            for (int i = forbidden.Count - 1; i >= 0; i--)
                if (Time.time > forbidden[i].until) forbidden.RemoveAt(i);
        }

        void HandleBlocked(float dt)
        {
            PruneForbidden();
            IBlocker b = BlockerRegistry.FindNear(transform.position + transform.forward * 0.7f, 1.3f);
            if (b == null)
            {
                // nothing we know about: just recompute
                motor.ClearBlocked();
                nextRepath = 0f;
                return;
            }

            Door door = b as Door;
            if (door != null)
            {
                // closed door: shoulder it open
                bashTimer += dt;
                motor.SetAttackPose(1f);
                if (bashTimer >= doorBashDelay)
                {
                    door.Bash(transform.position);
                    bashTimer = 0f;
                    motor.ClearBlocked();
                    motor.SetAttackPose(0f);
                    nextRepath = Time.time + 0.5f;
                }
                return;
            }

            // barricade: the Runner cannot break it quickly, so it re-routes around it
            ForbiddenZone z = new ForbiddenZone();
            Bounds bb = b.BlockBounds;
            bb.Expand(new Vector3(0.8f, 0.8f, 0.8f));
            z.bounds = bb;
            z.until = Time.time + forbidSeconds;
            forbidden.Add(z);
            motor.ClearBlocked();
            nextRepath = 0f;
            if (CurrentState == State.Hunt && sees) RepathToPlayer();
            else if (hasTarget) RepathTo(target);
        }

        // when every route is blocked the Runner ends up at the closest reachable point; it claws at the obstacle
        void ClawIfTrapped()
        {
            if (!LastPathPartial || motor.HasPath || Time.time < nextClaw) return;
            IBlocker b = BlockerRegistry.FindNear(transform.position + transform.forward * 0.8f, 1.8f);
            if (b == null) return;
            nextClaw = Time.time + 0.9f;
            motor.FaceTowards(b.BreakPoint, 1f);
            b.TakeBlockDamage(clawDamage, gameObject);
        }

        void Growl(AudioClip clip, float volume)
        {
            if (voice != null && clip != null && !voice.isPlaying) voice.PlayOneShot(clip, volume);
        }

        static float Planar(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
