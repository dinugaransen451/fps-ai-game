using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// The Director - Intelligent Systems agent (version 2).
// It adapts spawning to the player's health, ammo and time spent in one area,
// and only spawns at markers that are far from the player and hidden from them.
public class Director : MonoBehaviour
{
    [Header("Scene references (drag these in the Inspector)")]
    public Transform player;
    public PlayerStats stats;             // optional: the player's health and ammo
    public Transform spawnPointsParent;   // the SpawnPoints object
    public GameObject zombiePrefab;

    [Header("Spawn placement")]
    public float minDistanceFromPlayer = 8f;
    public float minDistanceBetweenZombies = 6f;
    public float snapDistance = 4f;

    [Header("Pacing (the Director adapts between these)")]
    public float fastestInterval = 4f;    // seconds between spawns when the player is doing well
    public float slowestInterval = 12f;   // seconds between spawns when the player is struggling
    public int maxZombies = 5;            // zombies allowed when the player is doing well

    [Header("Staying in one area")]
    public float stayRadius = 6f;         // moving less than this counts as staying
    public float stayTimeLimit = 20f;     // seconds before the Director applies pressure

    private readonly List<GameObject> zombies = new List<GameObject>();
    private Transform lastMarker;
    private float timer;
    private float stayTime;
    private Vector3 stayAnchor;

    void Start()
    {
        if (player != null) stayAnchor = player.position;
    }

    void Update()
    {
        if (player == null) return;

        UpdateStayTimer();

        timer += Time.deltaTime;
        float interval = Mathf.Lerp(slowestInterval, fastestInterval, Intensity());
        bool pressure = stayTime >= stayTimeLimit;

        if (timer < interval && !pressure) return;
        timer = 0f;
        if (pressure) stayTime = 0f;

        zombies.RemoveAll(z => z == null);
        int allowed = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, maxZombies, Intensity())));
        if (zombies.Count >= allowed) return;

        TrySpawn(pressure);
    }

    // 0 = player is struggling (low health, low ammo), 1 = player is doing well.
    float Intensity()
    {
        if (stats == null) return 1f;
        float h = Mathf.Clamp01(stats.health / Mathf.Max(1f, stats.maxHealth));
        float a = Mathf.Clamp01((float)stats.ammo / Mathf.Max(1, stats.fullAmmo));
        return 0.5f * h + 0.5f * a;
    }

    void UpdateStayTimer()
    {
        if (Vector3.Distance(player.position, stayAnchor) > stayRadius)
        {
            stayAnchor = player.position;
            stayTime = 0f;
        }
        else
        {
            stayTime += Time.deltaTime;
        }
    }

    void TrySpawn(bool pressure)
    {
        if (spawnPointsParent == null || zombiePrefab == null)
        {
            Debug.LogWarning("Director: assign Spawn Points Parent and Zombie Prefab in the Inspector.");
            return;
        }

        NavMeshHit playerHit;
        if (!NavMesh.SamplePosition(player.position, out playerHit, snapDistance, NavMesh.AllAreas))
        {
            Debug.LogWarning("Director: the player is not near the NavMesh.");
            return;
        }

        var candidates = new List<(Transform marker, Vector3 pos, float dist)>();

        foreach (Transform marker in spawnPointsParent)
        {
            NavMeshHit spawnHit;
            if (!NavMesh.SamplePosition(marker.position, out spawnHit, snapDistance, NavMesh.AllAreas))
            {
                Debug.LogWarning("Director: " + marker.name + " is not near the NavMesh, skipping it.");
                continue;
            }

            // Rule 1: not too close to the player.
            float distance = Vector3.Distance(spawnHit.position, playerHit.position);
            if (distance < minDistanceFromPlayer) continue;

            // Rule 2: hidden from the player (a wall edge blocks the line).
            NavMeshHit edgeHit;
            bool hidden = NavMesh.Raycast(playerHit.position, spawnHit.position, out edgeHit, NavMesh.AllAreas);
            if (!hidden) continue;

            // Rule 3: not crowded by another zombie.
            bool crowded = false;
            foreach (GameObject z in zombies)
            {
                if (z != null && Vector3.Distance(z.transform.position, spawnHit.position) < minDistanceBetweenZombies)
                {
                    crowded = true;
                    break;
                }
            }
            if (crowded) continue;

            candidates.Add((marker, spawnHit.position, distance));
        }

        // Rule 4: do not reuse the last marker if there is another choice.
        if (candidates.Count > 1 && lastMarker != null)
        {
            candidates.RemoveAll(c => c.marker == lastMarker);
        }

        if (candidates.Count == 0)
        {
            Debug.Log("Director: no suitable hidden spawn point available right now.");
            return;
        }

        // Normal spawn: random hidden marker. Pressure spawn: the nearest hidden marker.
        var chosen = candidates[0];
        if (pressure)
        {
            foreach (var c in candidates)
            {
                if (c.dist < chosen.dist) chosen = c;
            }
        }
        else
        {
            chosen = candidates[Random.Range(0, candidates.Count)];
        }

        GameObject zombie = Instantiate(zombiePrefab, chosen.pos, Quaternion.identity);
        ZombiePlaceholder chaser = zombie.GetComponent<ZombiePlaceholder>();
        if (chaser != null) chaser.target = player;

        zombies.Add(zombie);
        lastMarker = chosen.marker;

        Debug.Log("Director: spawned a zombie at " + chosen.marker.name +
                  " | reason: " + (pressure ? "player stayed too long" : "normal pacing") +
                  " | intensity " + Intensity().ToString("0.00"));
    }
}