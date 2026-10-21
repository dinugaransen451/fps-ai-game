using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ScreamerAI;

namespace ZombieGame
{
    /// <summary>Patrol waypoints for the Tank, filled in by the level (one loop per floor).</summary>
    public static class PatrolRoutes
    {
        public static readonly List<Vector3[]> Routes = new List<Vector3[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Routes.Clear(); }

        public static Vector3[] Closest(Vector3 pos)
        {
            Vector3[] best = null;
            float bestY = 999f;
            for (int i = 0; i < Routes.Count; i++)
            {
                if (Routes[i] == null || Routes[i].Length == 0) continue;
                float dy = Mathf.Abs(Routes[i][0].y - pos.y);
                if (dy < bestY) { bestY = dy; best = Routes[i]; }
            }
            return best;
        }
    }

    /// <summary>
    /// THE TANK (Member 2 - IS agent): a slow, heavy zombie driven by a BEHAVIOR TREE.
    ///   Root (priority selector)
    ///    1. Attack      : InMeleeRange -> Strike
    ///    2. Chase       : ShouldChase -> [ PathBlocked -> Smash barricade/door | MoveToPlayer ]
    ///    3. Investigate : HasNoise -> GoLook      (gunshots, thrown bottles, doors, screams)
    ///    4. Patrol      : walk the corridor loop
    /// It cannot fit through narrow gaps (NavMesh area "Narrow" is excluded from its area mask), so it takes the
    /// wider, predictable routes - and when a barricade or door blocks the only path it walks up and breaks it.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class TankBrain : MonoBehaviour, IAlertListener
    {
        public const int NarrowArea = 3;

        public Transform player;

        [Header("Senses")]
        public float sightRange = 20f;
        public float sightAngle = 110f;
        public float closeSense = 3.5f;
        public float eyeHeight = 2.0f;
        public float chaseMemory = 7f;
        public float hearingSensitivity = 1f;

        [Header("Speed (m/s) - slow and heavy")]
        public float patrolSpeed = 1.0f;
        public float investigateSpeed = 1.6f;
        public float chaseSpeed = 2.7f;

        [Header("Attack")]
        public float attackReach = 2.1f;
        public float attackDamage = 30f;
        public float attackWindup = 0.7f;
        public float attackCooldown = 1.9f;
        public float blockerDamage = 45f;
        public float blockerHitInterval = 1.3f;

        public string DebugInfo
        {
            get { return "Tank  " + (tree != null ? tree.ActivePathString() : "") + "  v=" + (agent != null ? agent.velocity.magnitude.ToString("0.0") : "0"); }
        }

        public Transform AlertTransform { get { return transform; } }

        NavMeshAgent agent;
        ZombieHealth health;
        AgentLocomotionAnimator loco;
        AudioSource voice;
        BehaviorTree tree;
        readonly NavMeshPath scratchPath = new NavMeshPath();

        // perception
        bool sees;
        float distToPlayer = 999f;
        Vector3 lastSeenPos;
        float lastSeenTime = -999f;
        bool hasChaseTarget;
        float nextSense;

        // investigate
        bool hasInvestigate;
        Vector3 investigatePos;
        float investigateArrivedAt = -1f;

        // patrol
        Vector3[] route;
        int routeIndex;
        float patrolWaitUntil;
        bool patrolWaiting;
        float patrolStuckTimer;

        // swing (attack) state
        float swingStart = -1f;
        float nextSwing;
        bool swingTicked;

        // blocker handling
        IBlocker currentBlocker;
        bool pathBlocked;
        float nextPathCheck;
        float nextRepath;
        float nextRoar;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<ZombieHealth>();
            loco = GetComponent<AgentLocomotionAnimator>();
            voice = GetComponent<AudioSource>();
            if (voice == null) voice = gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 1f;
            voice.minDistance = 4f;
            voice.maxDistance = 45f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.playOnAwake = false;

            // the Tank cannot use narrow gaps
            agent.areaMask = ~(1 << NarrowArea);

            tree = new BehaviorTree(new BTSelector("Root",
                new BTSequence("Attack",
                    new BTCondition("InMeleeRange", InMeleeRange),
                    new BTAction("Strike", Strike)),
                new BTSequence("Chase",
                    new BTCondition("ShouldChase", ShouldChase),
                    new BTSelector("ChaseOrBreak",
                        new BTSequence("BreakBlocker",
                            new BTCondition("PathBlocked", IsPathBlocked),
                            new BTAction("Smash", SmashBlocker)),
                        new BTAction("MoveToPlayer", ChasePlayer))),
                new BTSequence("Investigate",
                    new BTCondition("HasNoise", delegate { return hasInvestigate; }),
                    new BTAction("GoLook", Investigate)),
                new BTAction("Patrol", Patrol)));

            nextRoar = Time.time + Random.Range(6f, 12f);
        }

        void Start()
        {
            if (player == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) player = p.transform;
            }
            if (!agent.isOnNavMesh)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(transform.position, out hit, 6f, agent.areaMask)) agent.Warp(hit.position);
            }
            route = PatrolRoutes.Closest(transform.position);
            if (route != null) routeIndex = NearestRouteIndex();
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
            lastSeenPos = alert.TargetPosition;
            lastSeenTime = Time.time;
            hasChaseTarget = true;
        }

        void OnNoise(NoiseEvent e)
        {
            if (!health.IsAlive || sees) return;
            if (!NoiseSystem.CanHear(e, transform.position, hearingSensitivity)) return;
            hasInvestigate = true;
            investigatePos = e.position;
            investigateArrivedAt = -1f;
        }

        // ------------------------------------------------------------------ update
        void Update()
        {
            if (player == null || !health.IsAlive || !agent.enabled || !agent.isOnNavMesh) return;

            if (Time.time >= nextSense)
            {
                nextSense = Time.time + 0.15f;
                sees = Senses.CanSee(transform, eyeHeight, player, sightRange, sightAngle, closeSense, out distToPlayer);
                if (sees)
                {
                    lastSeenPos = player.position;
                    lastSeenTime = Time.time;
                    hasChaseTarget = true;
                    hasInvestigate = false;
                }
            }

            swingTicked = false;
            tree.Tick();
            if (!swingTicked) CancelSwing();

            if (Time.time >= nextRoar && (sees || hasChaseTarget))
            {
                nextRoar = Time.time + Random.Range(7f, 14f);
                if (voice != null && !voice.isPlaying) voice.PlayOneShot(GameAudio.Roar, 0.9f);
            }
        }

        // ------------------------------------------------------------------ BT leaves
        bool InMeleeRange()
        {
            PlayerStats ps = PlayerStats.Instance;
            return sees && (ps == null || ps.IsAlive) && distToPlayer <= attackReach && Mathf.Abs(player.position.y - transform.position.y) < 1.8f;
        }

        BTStatus Strike()
        {
            return Swing(player.position, null);
        }

        // One wind-up + hit cycle. target == null -> hits the player, otherwise damages the blocker.
        BTStatus Swing(Vector3 faceAt, IBlocker blocker)
        {
            swingTicked = true;
            agent.isStopped = true;
            FaceTowards(faceAt);

            if (swingStart < 0f)
            {
                if (Time.time < nextSwing) return BTStatus.Running;
                swingStart = Time.time;
                if (loco != null) loco.attack = 1f;
                GameAudio.PlayAt(GameAudio.Swing, transform.position, 0.8f);
            }
            else if (Time.time - swingStart >= attackWindup)
            {
                if (blocker == null)
                {
                    PlayerStats ps = PlayerStats.Instance;
                    if (ps != null && ps.IsAlive && distToPlayer <= attackReach * 1.25f)
                    {
                        ps.TakeDamage(new DamageInfo(attackDamage, player.position + Vector3.up, (player.position - transform.position).normalized, false, gameObject));
                        if (PlayerController.Instance != null) PlayerController.Instance.AddShake(0.7f);
                    }
                    nextSwing = Time.time + attackCooldown;
                }
                else
                {
                    blocker.TakeBlockDamage(blockerDamage, gameObject);
                    nextSwing = Time.time + blockerHitInterval;
                }
                swingStart = -1f;
                if (loco != null) loco.attack = 0f;
            }
            return BTStatus.Running;
        }

        void CancelSwing()
        {
            if (swingStart >= 0f)
            {
                swingStart = -1f;
                if (loco != null) loco.attack = 0f;
            }
        }

        bool ShouldChase()
        {
            return sees || (hasChaseTarget && Time.time - lastSeenTime < chaseMemory);
        }

        Vector3 ChaseDestination() { return sees ? player.position : lastSeenPos; }

        bool IsPathBlocked()
        {
            if (currentBlocker != null && !currentBlocker.IsBlocking)
            {
                currentBlocker = null;
                pathBlocked = false;
                nextPathCheck = 0f;
            }

            if (Time.time >= nextPathCheck)
            {
                nextPathCheck = Time.time + 0.5f;
                Vector3 dest = ChaseDestination();
                NavMeshHit h;
                if (NavMesh.SamplePosition(dest, out h, 3f, agent.areaMask)) dest = h.position;
                bool found = NavMesh.CalculatePath(transform.position, dest, agent.areaMask, scratchPath);
                pathBlocked = !(found && scratchPath.status == NavMeshPathStatus.PathComplete);

                if (pathBlocked && currentBlocker == null) currentBlocker = FindBlockerToBreak(dest);
                if (!pathBlocked) currentBlocker = null;
            }
            return pathBlocked && currentBlocker != null;
        }

        // the breakable blocker that adds the smallest detour on the way to the target
        IBlocker FindBlockerToBreak(Vector3 dest)
        {
            IBlocker best = null;
            float bestCost = float.MaxValue;
            List<IBlocker> all = BlockerRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                IBlocker b = all[i];
                if (b == null || !b.IsBlocking || !b.TankCanBreak) continue;
                Vector3 bp = b.BreakPoint;
                if (Mathf.Abs(bp.y - transform.position.y) > 3f) continue;
                float cost = Vector3.Distance(transform.position, bp) + Vector3.Distance(bp, dest);
                if (cost < bestCost) { bestCost = cost; best = b; }
            }
            return best;
        }

        BTStatus SmashBlocker()
        {
            if (currentBlocker == null || !currentBlocker.IsBlocking)
            {
                currentBlocker = null;
                nextPathCheck = 0f;
                return BTStatus.Failure;
            }

            Bounds bb = currentBlocker.BlockBounds;
            Vector3 closest = bb.ClosestPoint(transform.position);
            float d = Planar(transform.position, closest);

            if (d > attackReach * 0.85f)
            {
                agent.isStopped = false;
                agent.speed = chaseSpeed;
                if (loco != null) loco.attack = 0f;
                if (Time.time >= nextRepath)
                {
                    nextRepath = Time.time + 0.3f;
                    Vector3 away = transform.position - closest;
                    away.y = 0f;
                    Vector3 want = closest + (away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward) * 1.3f;
                    NavMeshHit h;
                    if (NavMesh.SamplePosition(want, out h, 2.5f, agent.areaMask)) agent.SetDestination(h.position);
                    else agent.SetDestination(transform.position);
                }
                return BTStatus.Running;
            }

            return Swing(currentBlocker.BreakPoint, currentBlocker);
        }

        BTStatus ChasePlayer()
        {
            agent.isStopped = false;
            agent.speed = chaseSpeed;
            if (Time.time >= nextRepath)
            {
                nextRepath = Time.time + 0.25f;
                agent.SetDestination(ChaseDestination());
            }
            return BTStatus.Running;
        }

        BTStatus Investigate()
        {
            agent.speed = investigateSpeed;
            if (investigateArrivedAt < 0f)
            {
                agent.isStopped = false;
                if (Time.time >= nextRepath)
                {
                    nextRepath = Time.time + 0.5f;
                    NavMeshHit h;
                    Vector3 dest = NavMesh.SamplePosition(investigatePos, out h, 4f, agent.areaMask) ? h.position : investigatePos;
                    agent.SetDestination(dest);
                }
                bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 1.2f;
                bool invalid = !agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathInvalid;
                if (arrived || invalid) investigateArrivedAt = Time.time;
            }
            else
            {
                // look around for a few seconds
                agent.isStopped = true;
                transform.Rotate(0f, 55f * Time.deltaTime, 0f);
                if (Time.time - investigateArrivedAt > 3.5f)
                {
                    hasInvestigate = false;
                    investigateArrivedAt = -1f;
                    agent.isStopped = false;
                    return BTStatus.Success;
                }
            }
            return BTStatus.Running;
        }

        BTStatus Patrol()
        {
            agent.speed = patrolSpeed;

            if (route == null || route.Length == 0)
            {
                // no route available: wander
                if (!agent.hasPath || (!agent.pathPending && agent.remainingDistance < 1f))
                {
                    NavMeshHit h;
                    Vector3 r = transform.position + Random.insideUnitSphere * 14f;
                    if (NavMesh.SamplePosition(r, out h, 5f, agent.areaMask)) agent.SetDestination(h.position);
                }
                agent.isStopped = false;
                return BTStatus.Running;
            }

            if (patrolWaiting)
            {
                agent.isStopped = true;
                if (Time.time >= patrolWaitUntil)
                {
                    patrolWaiting = false;
                    routeIndex = (routeIndex + 1) % route.Length;
                    agent.isStopped = false;
                    agent.SetDestination(route[routeIndex]);
                    patrolStuckTimer = 0f;
                }
                return BTStatus.Running;
            }

            agent.isStopped = false;
            if (!agent.hasPath && !agent.pathPending) agent.SetDestination(route[routeIndex]);

            if (!agent.pathPending)
            {
                bool arrived = agent.remainingDistance <= agent.stoppingDistance + 0.8f;
                bool partial = agent.pathStatus != NavMeshPathStatus.PathComplete;
                patrolStuckTimer += agent.velocity.sqrMagnitude < 0.04f ? Time.deltaTime : 0f;
                if (arrived || partial || patrolStuckTimer > 5f)
                {
                    patrolWaiting = true;
                    patrolWaitUntil = Time.time + Random.Range(1.5f, 3.5f);
                }
            }
            return BTStatus.Running;
        }

        // ------------------------------------------------------------------ helpers
        int NearestRouteIndex()
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < route.Length; i++)
            {
                float d = Vector3.SqrMagnitude(route[i] - transform.position);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        void FaceTowards(Vector3 p)
        {
            Vector3 d = p - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) return;
            Quaternion want = Quaternion.LookRotation(d.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 220f * Time.deltaTime);
        }

        static float Planar(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
