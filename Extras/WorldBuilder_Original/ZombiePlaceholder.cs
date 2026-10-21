using UnityEngine;
using UnityEngine.AI;

// Temporary zombie for testing the Director.
// It just walks toward the player. Your teammates' real zombies replace it later.
[RequireComponent(typeof(NavMeshAgent))]
public class ZombiePlaceholder : MonoBehaviour
{
    public Transform target;
    public float repathInterval = 0.5f;

    private NavMeshAgent agent;
    private float timer;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void Update()
    {
        if (target == null || !agent.isOnNavMesh) return;

        timer += Time.deltaTime;
        if (timer < repathInterval) return;
        timer = 0f;

        agent.SetDestination(target.position);
    }
}
