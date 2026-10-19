using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// The Director - Intelligent Systems agent (version 1.1).
// Every few seconds it looks at all spawn markers and picks one that is
// far from the player, hidden from the player, and not crowded by other zombies.
public class Director : MonoBehaviour
{
    [Header("Scene references (drag these in the Inspector)")]
    public Transform player;
    public Transform spawnPointsParent;   // the SpawnPoints object
    public GameObject zombiePrefab;

    [Header("Spawn rules")]
    public float checkInterval = 5f;               // seconds between spawn attempts
    public int maxZombies = 5;                     // limit alive at once
    public float minDistanceFromPlayer = 15f;      // lower this for testing
    public float minDistanceBetweenZombies = 6f;   // do not spawn on top of another zombie
    public float snapDistance = 4f;                // how far a marker may be from the NavMesh

    private readonly List<GameObject> zombies = new List<GameObject>();
    private Transform lastMarker;                  // the marker used last time
    private float timer;

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < checkInterval) return;
        timer = 0f;

        zombies.RemoveAll(z => z == null);
        if (zombies.Count >= maxZombies) return;

        TrySpawn();
    }

    void TrySpawn()
    {
        if (player == null || spawnPointsParent == null || zombiePrefab == null)
        {
            Debug.LogWarning("Director: assign Player, Spawn Points Parent and Zombie Prefab in the Inspector.");
            return;
        }

        // Put the player's position onto the NavMesh.
        NavMeshHit playerHit;
        if (!NavMesh.SamplePosition(player.position, out playerHit, snapDistance, NavMesh.AllAreas))
        {
            Debug.LogWarning("Director: the player is not near the NavMesh.");
            return;
        }

        var candidates = new List<(Transform marker, Vector3 pos)>();

        foreach (Transform marker in spawnPointsParent)
        {
            // Snap the marker onto the NavMesh so small height errors don't matter.
            NavMeshHit spawnHit;
            if (!NavMesh.SamplePosition(marker.position, out spawnHit, snapDistance, NavMesh.AllAreas))
            {
                Debug.LogWarning("Director: " + marker.name + " is not near the NavMesh, skipping it.");
                continue;
            }

            // Rule 1: not too close to the player.
            float distance = Vector3.Distance(spawnHit.position, playerHit.position);
            if (distance < minDistanceFromPlayer) continue;

            // Rule 2: hidden from the player.
            // NavMesh.Raycast returns true when a wall (NavMesh edge) stops the line,
            // which means the player cannot see this marker.
            NavMeshHit edgeHit;
            bool hidden = NavMesh.Raycast(playerHit.position, spawnHit.position, out edgeHit, NavMesh.AllAreas);
            if (!hidden) continue;

            // Rule 3: not crowded. Skip markers that already have a zombie nearby.
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

            candidates.Add((marker, spawnHit.position));
        }

        // Rule 4: do not use the same marker twice in a row, if there is another choice.
        if (candidates.Count > 1 && lastMarker != null)
        {
            candidates.RemoveAll(c => c.marker == lastMarker);
        }

        if (candidates.Count == 0)
        {
            Debug.Log("Director: no suitable hidden spawn point available right now.");
            return;
        }

        var chosen = candidates[Random.Range(0, candidates.Count)];
        GameObject zombie = Instantiate(zombiePrefab, chosen.pos, Quaternion.identity);

        ZombiePlaceholder chaser = zombie.GetComponent<ZombiePlaceholder>();
        if (chaser != null) chaser.target = player;

        zombies.Add(zombie);
        lastMarker = chosen.marker;
        Debug.Log("Director: spawned a zombie at " + chosen.marker.name +
                  " (" + candidates.Count + " options)");
    }
}