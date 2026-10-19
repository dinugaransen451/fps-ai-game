using UnityEngine;
using UnityEngine.AI;

namespace ScreamerAI
{
    /// <summary>
    /// THE SCREAMER - support-type zombie driven by a Utility AI.
    ///
    /// It never fights. Every 'thinkInterval' seconds it:
    ///   1. SENSES   - can I see the player (cone + line of sight)? do I remember where they were?
    ///   2. SCORES   - Wander / Scream / Flee / Hide, each as a product of response-curve considerations
    ///   3. CHOOSES  - highest score wins (with a small commitment bonus so it doesn't flip-flop)
    ///   4. ACTS     - moves with a NavMeshAgent; Scream broadcasts the player's last known position
    ///                 through the AlertSystem so every other zombie in earshot converges on it.
    ///
    /// The zombie only ever uses what it has perceived (last known position) - it does not cheat.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public class ScreamerBrain : MonoBehaviour
    {
        // ------------------------------------------------------------------ inspector
        [Header("References (auto-found if empty)")]
        public Transform player;
        public Animator animator;
        public ZombieProceduralMotion motion;
        public AudioSource audioSource;
        public AudioClip screamClip;

        [Header("Perception")]
        public float sightRange = 26f;
        [Range(30f, 360f)] public float sightAngle = 150f;
        public float eyeHeight = 1.7f;
        [Tooltip("Inside this radius the Screamer 'feels' the player even without line of sight (footsteps, smell).")]
        public float closeSenseRange = 4f;
        [Tooltip("How long it remembers the player after losing sight of them.")]
        public float memorySeconds = 14f;
        public float thinkInterval = 0.15f;

        [Header("Movement")]
        public float wanderSpeed = 0.9f;
        public float hideSpeed = 3.4f;
        public float fleeSpeed = 4.2f;
        public float wanderRadius = 10f;
        public float wanderPauseMin = 2f;
        public float wanderPauseMax = 6f;

        [Header("Scream / alert")]
        [Tooltip("Zombies within this distance can hear the scream (walls muffle it).")]
        public float screamRadius = 40f;
        public float screamCooldown = 10f;
        [Tooltip("Seconds after the scream starts when the alert is actually broadcast (mouth opens).")]
        public float screamBroadcastDelay = 0.55f;
        public float screamDurationFallback = 2.8f;
        [Range(0f, 1f)] public float screamVolume = 1f;

        [Header("Utility tuning - distances (metres)")]
        public float fleeStartDistance = 14f;
        public float fleeFullDistance = 4f;
        public float screamSweetDistance = 16f;
        public float screamHalfWidth = 16f;
        public float hideSweetDistance = 10f;
        public float hideHalfWidth = 14f;
        public float hideSearchRadius = 28f;
        [Tooltip("If the player gets this close while the Screamer is hiding, it bolts.")]
        public float hideBreakDistance = 4f;
        public float hiddenMaxSeconds = 18f;

        [Header("Utility tuning - allies")]
        public int allyFullCount = 4;
        public float allyComfortDistance = 14f;

        [Header("Utility tuning - weights & stability")]
        public float wanderWeight = 0.30f;
        public float screamWeight = 1.00f;
        public float fleeWeight = 1.00f;
        public float hideWeight = 0.90f;
        [Tooltip("Score bonus (x) for the action already running - stops rapid flip-flopping.")]
        public float commitBonus = 0.20f;
        public float minActionSeconds = 0.8f;

        [Header("Debug")]
        public bool drawGizmos = true;
        public bool logDecisions = false;

        // ------------------------------------------------------------------ public read-only state (HUD / other scripts)
        public ScreamerAction CurrentAction { get; private set; }
        public ActionScore[] Scores { get { return scores; } }
        public bool SeesPlayer { get; private set; }
        public bool KnowsPlayer { get; private set; }
        public float Threat { get; private set; }
        public float PerceivedDistance { get; private set; }
        public Vector3 LastKnownPlayerPosition { get { return lastKnownPos; } }
        public bool IsHidden { get; private set; }
        public bool IsScreaming { get; private set; }
        public int AlliesInEarshot { get; private set; }
        public float NearestAllyDistance { get; private set; }
        public int LastScreamHeardBy { get; private set; }
        public float ScreamCooldownRemaining { get { return Mathf.Max(0f, screamCooldown - (Time.time - lastScreamTime)); } }

        /// <summary>(oldAction, newAction)</summary>
        public event System.Action<ScreamerAction, ScreamerAction> ActionChanged;

        // ------------------------------------------------------------------ private
        static readonly int ScreamHash = Animator.StringToHash("Scream");

        NavMeshAgent agent;
        NavMeshPath pathBuffer;
        ActionScore[] scores;

        // memory
        Vector3 lastKnownPos;
        float lastSeenTime = -999f;
        bool hasLastKnown;
        bool screamedThisEncounter;

        // timing
        float nextThinkTime;
        float actionStartTime;
        float lastScreamTime = -999f;

        // wander
        Vector3 homePos;
        bool wanderMoving;
        float nextWanderTime;

        // scream
        float screamStartTime;
        float screamDuration;
        bool screamBroadcastDone;

        // flee
        float nextFleeRepath;

        // hide
        HideSpot hideSpot;
        Vector3 hidePoint;
        bool hideArrived;
        float hiddenSince;
        float nextHideRefresh;
        HideSpot candSpot;
        Vector3 candPoint;
        float candQuality;

        // ================================================================== lifecycle
        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            pathBuffer = new NavMeshPath();

            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (motion == null) motion = GetComponent<ZombieProceduralMotion>();
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 3f;
            audioSource.maxDistance = 60f;

            scores = new ActionScore[]
            {
                new ActionScore(ScreamerAction.Wander),
                new ActionScore(ScreamerAction.Scream),
                new ActionScore(ScreamerAction.Flee),
                new ActionScore(ScreamerAction.Hide)
            };

            homePos = transform.position;
            NearestAllyDistance = 999f;
            PerceivedDistance = 999f;
        }

        void Start()
        {
            if (player == null)
            {
                GameObject go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) player = go.transform;
            }
            if (player == null) Debug.LogWarning("[Screamer] No player assigned and no object tagged 'Player' found.", this);

            EnsureOnNavMesh();
            screamDuration = ResolveScreamDuration();

            agent.speed = wanderSpeed;
            CurrentAction = ScreamerAction.Wander;
            actionStartTime = Time.time;
            nextWanderTime = Time.time + Random.Range(0.5f, 2f);
        }

        void OnDisable()
        {
            ReleaseHideSpot();
        }

        void Update()
        {
            if (player == null || agent == null || !agent.isOnNavMesh)
            {
                DriveMotion();
                return;
            }

            if (Time.time >= nextThinkTime)
            {
                nextThinkTime = Time.time + thinkInterval;
                Sense();
                Evaluate();
            }

            switch (CurrentAction)
            {
                case ScreamerAction.Wander: TickWander(); break;
                case ScreamerAction.Scream: TickScream(); break;
                case ScreamerAction.Flee: TickFlee(); break;
                case ScreamerAction.Hide: TickHide(); break;
            }

            DriveMotion();
        }

        // ================================================================== 1. SENSE
        void Sense()
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 chest = player.position + Vector3.up * 1.1f;

            Vector3 flat = player.position - transform.position;
            flat.y = 0f;
            float dist = flat.magnitude;

            bool inRange = dist <= sightRange;
            bool inCone = dist <= closeSenseRange || Vector3.Angle(transform.forward, flat) <= sightAngle * 0.5f;
            bool visible = inRange && inCone && !LineOfSight.IsBlocked(eye, chest, transform, player);
            bool feltClose = dist <= closeSenseRange;

            SeesPlayer = visible;
            if (visible || feltClose)
            {
                lastKnownPos = player.position;
                lastSeenTime = Time.time;
                hasLastKnown = true;
            }

            float since = Time.time - lastSeenTime;
            KnowsPlayer = hasLastKnown && since < memorySeconds;
            Threat = KnowsPlayer ? Mathf.Clamp01(1f - since / memorySeconds) : 0f;
            PerceivedDistance = KnowsPlayer ? PlanarDistance(transform.position, lastKnownPos) : 999f;

            if (!KnowsPlayer) screamedThisEncounter = false;

            AlliesInEarshot = AlertSystem.CountInRadius(transform.position, screamRadius, transform);
            NearestAllyDistance = AlertSystem.NearestDistance(transform.position, transform);
        }

        // ================================================================== 2. SCORE  +  3. CHOOSE
        void Evaluate()
        {
            float T = Threat;
            float d = PerceivedDistance;
            bool screamReady = (Time.time - lastScreamTime) >= screamCooldown;
            float allyCoverage = Mathf.Clamp01((float)AlliesInEarshot / Mathf.Max(1, allyFullCount));
            float aloneness = Mathf.Clamp01(NearestAllyDistance / Mathf.Max(1f, allyComfortDistance)); // 0 = ally beside me, 1 = alone

            RefreshHideCandidate();

            // ---- WANDER : only attractive while it feels safe
            ActionScore wander = scores[(int)ScreamerAction.Wander];
            wander.Begin(wanderWeight);
            wander.Consider("Calm", 1f - T, UtilityCurves.Linear(1f - T, 0.85f, 0.15f));
            wander.Finish();

            // ---- SCREAM : sees the player, off cooldown, at a distance where calling help makes sense
            ActionScore scream = scores[(int)ScreamerAction.Scream];
            scream.Begin(screamWeight);
            scream.Consider("SeesPlayer", SeesPlayer ? 1f : 0f, SeesPlayer ? 1f : 0f);
            scream.Consider("CooldownReady", screamReady ? 1f : 0f, screamReady ? 1f : 0f);
            scream.Consider("DistanceSweetSpot", d, UtilityCurves.Bell(d, screamSweetDistance, screamHalfWidth));
            scream.Consider("AlliesCanHear", allyCoverage, UtilityCurves.Linear(allyCoverage, 0.6f, 0.4f));
            scream.Finish();

            // ---- FLEE : the closer the (remembered) player, the stronger; allies nearby make it braver
            ActionScore flee = scores[(int)ScreamerAction.Flee];
            flee.Begin(fleeWeight);
            float closeness = 1f - Mathf.Clamp01((d - fleeFullDistance) / Mathf.Max(0.01f, fleeStartDistance - fleeFullDistance));
            flee.Consider("Proximity", closeness, UtilityCurves.Power(closeness, 1.5f));
            flee.Consider("Threat", T, UtilityCurves.Linear(T));
            flee.Consider("AloneFactor", aloneness, UtilityCurves.Linear(aloneness, 0.5f, 0.5f));
            bool stayPut = IsHidden && d > hideBreakDistance;
            flee.Consider("StayHiddenUnlessCornered", IsHidden ? 0f : 1f, stayPut ? 0.35f : 1f);
            flee.Finish();

            // ---- HIDE : knows about the player, cover exists, mid distance, has already called for help
            ActionScore hide = scores[(int)ScreamerAction.Hide];
            hide.Begin(hideWeight);
            hide.Consider("Threat", T, UtilityCurves.Linear(T));
            hide.Consider("CoverAvailable", candQuality, candQuality);
            hide.Consider("DistanceBand", d, UtilityCurves.Bell(d, hideSweetDistance, hideHalfWidth));
            hide.Consider("ScreamedAlready", screamedThisEncounter ? 1f : 0f, screamedThisEncounter ? 1f : 0.35f);
            float patience = IsHidden ? 1f - Mathf.Clamp01((Time.time - hiddenSince) / Mathf.Max(1f, hiddenMaxSeconds)) : 1f;
            hide.Consider("Patience", patience, patience);
            hide.Finish();

            // ---- pick the winner
            if (IsScreaming) return;   // a scream can't be interrupted

            ScreamerAction best = CurrentAction;
            float bestValue = -1f;
            for (int i = 0; i < scores.Length; i++)
            {
                float v = scores[i].Score;
                if (scores[i].Action == CurrentAction) v *= (1f + commitBonus);
                if (v > bestValue)
                {
                    bestValue = v;
                    best = scores[i].Action;
                }
            }

            if (best != CurrentAction)
            {
                bool tooSoon = (Time.time - actionStartTime) < minActionSeconds;
                bool currentStillWorthIt = scores[(int)CurrentAction].Score > 0.02f;
                if (tooSoon && currentStillWorthIt) return;
                SwitchTo(best);
            }
        }

        // ================================================================== 4. ACT
        void SwitchTo(ScreamerAction next)
        {
            ScreamerAction old = CurrentAction;
            ExitAction(old);
            CurrentAction = next;
            actionStartTime = Time.time;
            EnterAction(next);

            if (logDecisions) Debug.Log("[Screamer] " + old + " -> " + next + "  (threat " + Threat.ToString("F2") + ", dist " + PerceivedDistance.ToString("F1") + ")", this);
            if (ActionChanged != null) ActionChanged(old, next);
        }

        void EnterAction(ScreamerAction a)
        {
            switch (a)
            {
                case ScreamerAction.Wander:
                    agent.isStopped = false;
                    agent.speed = wanderSpeed;
                    wanderMoving = false;
                    nextWanderTime = Time.time + 0.5f;
                    break;
                case ScreamerAction.Scream:
                    StartScream();
                    break;
                case ScreamerAction.Flee:
                    agent.isStopped = false;
                    agent.speed = fleeSpeed;
                    nextFleeRepath = 0f;
                    break;
                case ScreamerAction.Hide:
                    agent.isStopped = false;
                    agent.speed = hideSpeed;
                    hideArrived = false;
                    hideSpot = null;
                    break;
            }
        }

        void ExitAction(ScreamerAction a)
        {
            if (a == ScreamerAction.Hide)
            {
                ReleaseHideSpot();
                IsHidden = false;
                hideArrived = false;
                agent.isStopped = false;
            }
        }

        // ------------------------------------------------------------------ wander
        void TickWander()
        {
            if (!wanderMoving)
            {
                if (Time.time >= nextWanderTime)
                {
                    Vector3 p;
                    if (TryRandomPoint(homePos, wanderRadius, out p))
                    {
                        agent.SetDestination(p);
                        wanderMoving = true;
                    }
                    else
                    {
                        nextWanderTime = Time.time + 1f;
                    }
                }
            }
            else if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f)
            {
                wanderMoving = false;
                nextWanderTime = Time.time + Random.Range(wanderPauseMin, wanderPauseMax);
            }
        }

        // ------------------------------------------------------------------ scream
        void StartScream()
        {
            IsScreaming = true;
            screamStartTime = Time.time;
            screamBroadcastDone = false;

            agent.isStopped = true;
            agent.velocity = Vector3.zero;

            if (animator != null) animator.SetTrigger(ScreamHash);
            if (motion != null) motion.ClipPlaying = true;
            if (audioSource != null && screamClip != null) audioSource.PlayOneShot(screamClip, screamVolume);
        }

        void TickScream()
        {
            if (!IsScreaming) return;

            FaceTowards(lastKnownPos, 360f);
            float t = Time.time - screamStartTime;

            if (!screamBroadcastDone && t >= screamBroadcastDelay)
            {
                screamBroadcastDone = true;
                LastScreamHeardBy = AlertSystem.Broadcast(lastKnownPos, transform.position, screamRadius, gameObject);
                ScreamShockwave.Spawn(transform.position + Vector3.up * 1.2f, Mathf.Min(screamRadius, 40f));
                if (logDecisions) Debug.Log("[Screamer] SCREAM! " + LastScreamHeardBy + " zombies heard it.", this);
            }

            if (t >= screamDuration) EndScream();
        }

        void EndScream()
        {
            IsScreaming = false;
            lastScreamTime = Time.time;
            screamedThisEncounter = true;
            agent.isStopped = false;
            if (motion != null) motion.ClipPlaying = false;
            nextThinkTime = 0f;   // re-evaluate immediately
        }

        // ------------------------------------------------------------------ flee
        void TickFlee()
        {
            bool arrived = !agent.pathPending && agent.remainingDistance < 1f;
            if (Time.time >= nextFleeRepath || arrived)
            {
                nextFleeRepath = Time.time + 0.6f;
                Vector3 p;
                if (TryPickFleePoint(out p)) agent.SetDestination(p);
            }
        }

        bool TryPickFleePoint(out Vector3 result)
        {
            result = transform.position;
            Vector3 me = transform.position;
            Vector3 threat = lastKnownPos;
            Vector3 threatEye = threat + Vector3.up * 1.6f;
            float myDist = PlanarDistance(me, threat);
            Vector3 toThreat = threat - me;
            toThreat.y = 0f;
            toThreat.Normalize();

            const int samples = 12;
            const float step = 11f;
            float bestScore = float.NegativeInfinity;
            bool found = false;
            float jitter = Random.Range(0f, 360f / samples);

            for (int i = 0; i < samples; i++)
            {
                float angle = jitter + i * (360f / samples);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(me + dir * step, out hit, 3f, NavMesh.AllAreas)) continue;
                Vector3 c = hit.position;

                if (!agent.CalculatePath(c, pathBuffer) || pathBuffer.status != NavMeshPathStatus.PathComplete) continue;
                Vector3[] corners = pathBuffer.corners;
                if (corners.Length < 2) continue;

                float length = PathLength(corners);
                float gained = PlanarDistance(c, threat) - myDist;                 // how much farther from the player
                float hiddenBonus = LineOfSight.IsBlocked(threatEye, c + Vector3.up * 1.2f, player, transform) ? 2f : 0f;
                float allyBonus = AlertSystem.NearestDistance(c, transform) < 8f ? 1.5f : 0f;

                Vector3 firstLeg = corners[1] - me;
                firstLeg.y = 0f;
                float towardPlayer = Mathf.Max(0f, Vector3.Dot(firstLeg.normalized, toThreat));   // don't run past the player

                float score = gained + hiddenBonus + allyBonus - towardPlayer * 6f - 0.05f * length;
                if (score > bestScore)
                {
                    bestScore = score;
                    result = c;
                    found = true;
                }
            }
            return found;
        }

        // ------------------------------------------------------------------ hide
        void RefreshHideCandidate()
        {
            if (Time.time < nextHideRefresh) return;
            nextHideRefresh = Time.time + 0.4f;

            candSpot = null;
            candQuality = 0f;
            if (!KnowsPlayer || !agent.isOnNavMesh) return;

            Vector3 threat = lastKnownPos;
            Vector3 threatEye = threat + Vector3.up * 1.6f;
            float myDist = PlanarDistance(transform.position, threat);
            float bestCost = float.MaxValue;

            for (int i = 0; i < HideSpot.All.Count; i++)
            {
                HideSpot spot = HideSpot.All[i];
                if (spot == null || !spot.IsAvailableTo(this)) continue;
                if (PlanarDistance(transform.position, spot.transform.position) > hideSearchRadius) continue;

                Vector3 pt;
                if (!spot.TryGetCoverPoint(threat, out pt)) continue;
                if (!spot.CoversPointFrom(threatEye, pt, transform, player)) continue;

                float spotToThreat = PlanarDistance(pt, threat);
                if (spotToThreat < 3f) continue;

                if (!agent.CalculatePath(pt, pathBuffer) || pathBuffer.status != NavMeshPathStatus.PathComplete) continue;
                float cost = PathLength(pathBuffer.corners);
                if (spotToThreat < myDist - 0.5f) cost += 12f;     // hiding by running towards the player is a bad plan

                if (cost < bestCost)
                {
                    bestCost = cost;
                    candSpot = spot;
                    candPoint = pt;
                }
            }

            if (candSpot != null)
                candQuality = Mathf.Clamp(1f - bestCost / (hideSearchRadius * 1.6f), 0.15f, 1f);

            // the cover we are using got exposed and a better one exists -> drop it so TickHide picks the new one
            if (hideSpot != null && candSpot != null && candSpot != hideSpot)
            {
                bool stillCovered = hideSpot.CoversPointFrom(threatEye, hidePoint, transform, player);
                if (!stillCovered)
                {
                    ReleaseHideSpot();
                    IsHidden = false;
                    hideArrived = false;
                    agent.isStopped = false;
                }
            }
        }

        void TickHide()
        {
            if (hideSpot == null)
            {
                if (candSpot == null) return;
                hideSpot = candSpot;
                hidePoint = candPoint;
                hideSpot.Claim(this);
                hideArrived = false;
                IsHidden = false;
                agent.isStopped = false;
                agent.speed = hideSpeed;
                agent.SetDestination(hidePoint);
                return;
            }

            if (!hideArrived)
            {
                if (!agent.pathPending && PlanarDistance(transform.position, hidePoint) < 0.7f)
                {
                    hideArrived = true;
                    IsHidden = true;
                    hiddenSince = Time.time;
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }
            }
            else
            {
                FaceTowards(lastKnownPos, 120f);   // cower, watching where the player was
            }
        }

        void ReleaseHideSpot()
        {
            if (hideSpot != null) hideSpot.Release(this);
            hideSpot = null;
        }

        // ================================================================== animation bridge
        void DriveMotion()
        {
            if (motion == null) return;

            float speed = (agent != null && agent.isOnNavMesh && !agent.isStopped) ? agent.velocity.magnitude : 0f;
            motion.Speed = speed;
            motion.RunBlend = Mathf.Clamp01((speed - 1.5f) / Mathf.Max(0.1f, fleeSpeed - 1.5f));

            float crouch = 0f;
            if (CurrentAction == ScreamerAction.Hide) crouch = IsHidden ? 1f : 0.55f;
            motion.Crouch = crouch;
        }

        // ================================================================== helpers
        void EnsureOnNavMesh()
        {
            if (agent.isOnNavMesh) return;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 6f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            else
                Debug.LogWarning("[Screamer] Not on a NavMesh. Bake a NavMesh (NavMeshSurface) that covers the Screamer.", this);
        }

        float ResolveScreamDuration()
        {
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                float longest = 0f;
                AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
                for (int i = 0; i < clips.Length; i++) longest = Mathf.Max(longest, clips[i].length);
                if (longest > 0.5f) return longest;
            }
            return screamDurationFallback;
        }

        bool TryRandomPoint(Vector3 centre, float radius, out Vector3 result)
        {
            Vector3 rnd = centre + Random.insideUnitSphere * radius;
            rnd.y = centre.y;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(rnd, out hit, 3f, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
            result = centre;
            return false;
        }

        void FaceTowards(Vector3 worldPoint, float degreesPerSecond)
        {
            Vector3 dir = worldPoint - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            Quaternion target = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, degreesPerSecond * Time.deltaTime);
        }

        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static float PathLength(Vector3[] corners)
        {
            float len = 0f;
            for (int i = 1; i < corners.Length; i++) len += Vector3.Distance(corners[i - 1], corners[i]);
            return len;
        }

        // ================================================================== gizmos
        void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            Vector3 p = transform.position;
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(p, closeSenseRange);

            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.8f);
            float half = sightAngle * 0.5f;
            Vector3 left = Quaternion.Euler(0f, -half, 0f) * transform.forward * sightRange;
            Vector3 right = Quaternion.Euler(0f, half, 0f) * transform.forward * sightRange;
            Gizmos.DrawLine(p + Vector3.up * eyeHeight, p + Vector3.up * eyeHeight + left);
            Gizmos.DrawLine(p + Vector3.up * eyeHeight, p + Vector3.up * eyeHeight + right);

            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.35f);
            Gizmos.DrawWireSphere(p, screamRadius);

            if (Application.isPlaying && hasLastKnown)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(lastKnownPos + Vector3.up * 0.2f, 0.5f);
                Gizmos.DrawLine(p + Vector3.up, lastKnownPos + Vector3.up * 0.2f);
            }
            if (Application.isPlaying && hideSpot != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(hidePoint + Vector3.up * 0.2f, 0.4f);
            }
        }
    }
}
