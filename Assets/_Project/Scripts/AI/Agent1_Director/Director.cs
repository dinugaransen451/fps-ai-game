using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>
    /// THE DIRECTOR (Member 1 - IS agent, not a physical zombie): the "game master".
    /// It watches the player's health, ammo and how long they stay in one area, then decides
    ///   WHEN to spawn (pacing), WHERE (hidden, far, uncrowded markers) and WHICH of the 3 zombie types.
    /// Every decision is written to Log (shown in the F1 debug HUD) so it can be explained in the viva.
    /// </summary>
    public class Director : MonoBehaviour
    {
        [Header("Scene references")]
        public Transform player;
        public PlayerStats stats;
        public Transform spawnPointsParent;
        public Camera playerCamera;
        public GameAssets assets;

        [Header("Spawn placement rules")]
        public float minDistanceFromPlayer = 10f;
        public float minDistanceBetweenZombies = 5f;
        public float snapDistance = 4f;

        [Header("Pacing (the Director adapts between these)")]
        public float fastestInterval = 5f;     // seconds between spawns when the player is doing well
        public float slowestInterval = 14f;    // seconds between spawns when the player is struggling
        public int maxZombies = 6;             // zombies allowed alive at once when the player is doing well
        public int initialZombies = 3;

        [Header("Demo override")]
        /// <summary>While &gt; 0 the Director always waits this many seconds between spawns, no matter what
        /// it thinks of the player. The DemoBot sets it so the 3-minute storyboard keeps a fixed rhythm;
        /// 0 (or less) = normal adaptive pacing.</summary>
        public float demoPacingOverride;

        [Header("Quota")]
        public int totalToSpawn = 26;

        [Header("Per-type caps")]
        public int maxRunners = 5;
        public int maxTanks = 2;
        public int maxScreamers = 1;

        [Header("Staying in one area")]
        public float stayRadius = 6f;
        public float stayTimeLimit = 20f;

        public int SpawnedTotal { get; private set; }
        public readonly List<string> Log = new List<string>();

        readonly List<GameObject> spawned = new List<GameObject>();
        Transform lastMarker;
        float timer;
        float stayTime;
        Vector3 stayAnchor;
        bool started;

        public float StayTime { get { return stayTime; } }

        void Start()
        {
            if (player != null) stayAnchor = player.position;
            timer = fastestInterval - 2f;   // first wave arrives shortly after the initial population
            SpawnInitial();
            started = true;
        }

        void Update()
        {
            if (!started || player == null) return;
            if (GameManager.Instance != null && GameManager.Instance.state != GameManager.State.Playing) return;
            if (stats != null && !stats.IsAlive) return;

            UpdateStayTimer();

            timer += Time.deltaTime;
            float interval = demoPacingOverride > 0f ? demoPacingOverride
                                                     : Mathf.Lerp(slowestInterval, fastestInterval, Intensity);
            bool pressure = stayTime >= stayTimeLimit;
            if (timer < interval && !pressure) return;
            timer = 0f;
            if (pressure) stayTime = 0f;

            if (SpawnedTotal >= totalToSpawn) return;

            spawned.RemoveAll(z => z == null);
            int alive = ZombieHealth.Alive.Count;
            int allowed = Mathf.Max(2, Mathf.RoundToInt(Mathf.Lerp(2f, maxZombies, Intensity)));
            if (alive >= allowed) return;

            TrySpawn(pressure);
        }

        /// <summary>0 = player is struggling (low health, low ammo), 1 = player is doing well.</summary>
        public float Intensity
        {
            get
            {
                if (stats == null) return 1f;
                return 0.5f * stats.HealthFraction + 0.5f * stats.AmmoFraction;
            }
        }

        void UpdateStayTimer()
        {
            if (Vector3.Distance(player.position, stayAnchor) > stayRadius)
            {
                stayAnchor = player.position;
                stayTime = 0f;
            }
            else stayTime += Time.deltaTime;
        }

        // ------------------------------------------------------------------ spawning
        struct Candidate
        {
            public Transform marker;
            public Vector3 pos;
            public float dist;
        }

        void SpawnInitial()
        {
            List<Candidate> c = GatherCandidates(false, 16f);
            ZombieKind[] kinds = { ZombieKind.Runner, ZombieKind.Tank, ZombieKind.Runner, ZombieKind.Screamer };
            for (int i = 0; i < initialZombies && c.Count > 0 && i < kinds.Length; i++)
            {
                int idx = Random.Range(0, c.Count);
                Candidate pick = c[idx];
                c.RemoveAt(idx);
                // keep the initial group spread out
                c.RemoveAll(x => Vector3.Distance(x.pos, pick.pos) < minDistanceBetweenZombies * 2f);
                DoSpawn(kinds[i], pick, "initial population");
            }
        }

        void TrySpawn(bool pressure)
        {
            List<Candidate> candidates = GatherCandidates(true, minDistanceFromPlayer);

            if (candidates.Count > 1 && lastMarker != null)
                candidates.RemoveAll(c => c.marker == lastMarker);   // do not reuse the last marker

            if (candidates.Count == 0)
            {
                AddLog("no suitable hidden spawn point right now");
                return;
            }

            // pressure spawn: the nearest hidden marker. Normal spawn: random hidden marker.
            Candidate chosen = candidates[0];
            if (pressure)
            {
                for (int i = 1; i < candidates.Count; i++)
                    if (candidates[i].dist < chosen.dist) chosen = candidates[i];
            }
            else chosen = candidates[Random.Range(0, candidates.Count)];

            string weights;
            ZombieKind kind = ChooseKind(pressure, out weights);
            DoSpawn(kind, chosen, (pressure ? "player stayed too long" : "normal pacing") + " | " + weights);
        }

        List<Candidate> GatherCandidates(bool requireHidden, float minDist)
        {
            List<Candidate> list = new List<Candidate>();
            if (spawnPointsParent == null) return list;

            NavMeshHit playerHit;
            Vector3 playerPos = player.position;
            if (NavMesh.SamplePosition(player.position, out playerHit, snapDistance, NavMesh.AllAreas)) playerPos = playerHit.position;

            Vector3 eye = playerCamera != null ? playerCamera.transform.position : player.position + Vector3.up * 1.6f;

            foreach (Transform marker in spawnPointsParent)
            {
                NavMeshHit spawnHit;
                if (!NavMesh.SamplePosition(marker.position, out spawnHit, snapDistance, NavMesh.AllAreas)) continue;

                // Rule 1: not too close to the player
                float distance = Vector3.Distance(spawnHit.position, playerPos);
                if (distance < minDist) continue;

                // Rule 2: hidden from the player (something solid blocks the line of sight)
                if (requireHidden)
                {
                    RaycastHit rh;
                    bool blocked = Physics.Linecast(eye, spawnHit.position + Vector3.up * 1.5f, out rh, ~0, QueryTriggerInteraction.Ignore);
                    if (!blocked) continue;
                }

                // Rule 3: not crowded by another zombie
                bool crowded = false;
                for (int i = 0; i < ZombieHealth.Alive.Count; i++)
                {
                    ZombieHealth z = ZombieHealth.Alive[i];
                    if (z != null && Vector3.Distance(z.transform.position, spawnHit.position) < minDistanceBetweenZombies) { crowded = true; break; }
                }
                if (crowded) continue;

                Candidate c = new Candidate();
                c.marker = marker;
                c.pos = spawnHit.position;
                c.dist = distance;
                list.Add(c);
            }
            return list;
        }

        // Which zombie? Weighted by what the player is doing (explained in the viva).
        ZombieKind ChooseKind(bool pressure, out string weights)
        {
            int runners = ZombieHealth.CountAlive(ZombieKind.Runner);
            int tanks = ZombieHealth.CountAlive(ZombieKind.Tank);
            int screamers = ZombieHealth.CountAlive(ZombieKind.Screamer);
            int alive = runners + tanks + screamers;
            float intensity = Intensity;
            float ammo = stats != null ? stats.AmmoFraction : 1f;

            float wRunner = runners < maxRunners ? 1f : 0f;

            // Tanks soak up ammo, so only send them while the player is doing well and has bullets
            float wTank = 0f;
            if (tanks < maxTanks && ammo > 0.25f) wTank = Mathf.Lerp(0f, 0.6f, Mathf.InverseLerp(0.45f, 0.85f, intensity));

            // The Screamer calls the others, so it needs an audience; campers get one sooner
            float wScreamer = 0f;
            if (screamers < maxScreamers && alive >= 2) wScreamer = 0.35f;

            if (pressure)
            {
                wScreamer = screamers < maxScreamers ? wScreamer * 3f + 0.3f : 0f;
                wTank *= 1.5f;
            }

            weights = "R" + wRunner.ToString("0.0") + " T" + wTank.ToString("0.0") + " S" + wScreamer.ToString("0.0")
                      + " | intensity " + intensity.ToString("0.00");

            float total = wRunner + wTank + wScreamer;
            if (total <= 0.001f) return ZombieKind.Runner;

            float r = Random.value * total;
            if (r < wRunner) return ZombieKind.Runner;
            if (r < wRunner + wTank) return ZombieKind.Tank;
            return ZombieKind.Screamer;
        }

        GameObject DoSpawn(ZombieKind kind, Candidate c, string reason)
        {
            Vector3 toPlayer = player.position - c.pos;
            float yaw = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg;
            GameObject z = ZombieFactory.Spawn(kind, c.pos, yaw, player, assets);
            spawned.Add(z);
            lastMarker = c.marker;
            SpawnedTotal++;
            AddLog(kind + " @ " + c.marker.name + " | " + reason);
            return z;
        }

        // ------------------------------------------------------------------ scripted spawning (demo)
        /// <summary>
        /// Spawn one zombie of 'kind' at the best marker whose name contains 'markerNameContains'
        /// (pass null/empty for any marker). Used by the DemoBot storyboard: the choice is deterministic -
        /// same floor as the player, hidden (solid between the player's eye and the marker) and about 20 m away.
        /// Returns the spawned GameObject, or null if there was no usable marker (the caller can retry).
        /// </summary>
        public GameObject ForceSpawn(ZombieKind kind, string markerNameContains)
        {
            if (spawnPointsParent == null || player == null)
            {
                AddLog("force spawn " + kind + " failed: no scene references");
                return null;
            }

            NavMeshHit playerHit;
            Vector3 playerPos = player.position;
            if (NavMesh.SamplePosition(player.position, out playerHit, snapDistance, NavMesh.AllAreas)) playerPos = playerHit.position;
            Vector3 eye = playerCamera != null ? playerCamera.transform.position : player.position + Vector3.up * 1.6f;

            Candidate best = new Candidate();
            bool found = false;
            float bestScore = float.MaxValue;

            foreach (Transform marker in spawnPointsParent)
            {
                if (!string.IsNullOrEmpty(markerNameContains) &&
                    marker.name.IndexOf(markerNameContains, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                NavMeshHit spawnHit;
                if (!NavMesh.SamplePosition(marker.position, out spawnHit, snapDistance, NavMesh.AllAreas)) continue;

                float distance = Vector3.Distance(spawnHit.position, playerPos);

                // hidden = something solid blocks the line of sight from the player's eye
                RaycastHit rh;
                bool hidden = Physics.Linecast(eye, spawnHit.position + Vector3.up * 1.5f, out rh, ~0, QueryTriggerInteraction.Ignore);

                // not crowded by another zombie
                bool crowded = false;
                for (int i = 0; i < ZombieHealth.Alive.Count; i++)
                {
                    ZombieHealth z = ZombieHealth.Alive[i];
                    if (z != null && Vector3.Distance(z.transform.position, spawnHit.position) < minDistanceBetweenZombies) { crowded = true; break; }
                }
                if (crowded) continue;

                // score: same floor (big bonus), hidden (big bonus), ~20 m from the player
                float floorPenalty = Mathf.Abs(spawnHit.position.y - playerPos.y) > 2f ? 60f : 0f;
                float score = floorPenalty + (hidden ? 0f : 40f) + Mathf.Abs(distance - 20f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best.marker = marker;
                    best.pos = spawnHit.position;
                    best.dist = distance;
                    found = true;
                }
            }

            if (!found)
            {
                AddLog("force spawn " + kind + " (" + (string.IsNullOrEmpty(markerNameContains) ? "any" : markerNameContains) + ") failed: no usable marker");
                return null;
            }

            return DoSpawn(kind, best, "scripted demo spawn");
        }

        void AddLog(string line)
        {
            Log.Add("[" + Time.time.ToString("0") + "s] " + line);
            if (Log.Count > 8) Log.RemoveAt(0);
            Debug.Log("Director: " + line);
        }
    }
}
