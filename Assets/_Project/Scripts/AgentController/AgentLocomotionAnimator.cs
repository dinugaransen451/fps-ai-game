using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>
    /// Agent Controller helper for NavMeshAgent-driven zombies (Tank): reads the agent's real velocity and
    /// feeds the walk/run animation so the feet match the movement.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class AgentLocomotionAnimator : MonoBehaviour
    {
        public float runSpeedReference = 4f;
        [Range(0f, 1f)] public float attack;

        NavMeshAgent agent;
        ZombieAnimDriver driver;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            driver = GetComponent<ZombieAnimDriver>();
            if (driver == null) driver = gameObject.AddComponent<ZombieAnimDriver>();
        }

        void Update()
        {
            float speed = (agent.enabled && agent.isOnNavMesh && !agent.isStopped) ? agent.velocity.magnitude : 0f;
            driver.Set(speed, Mathf.Clamp01((speed - 1.5f) / Mathf.Max(0.5f, runSpeedReference - 1.5f)), attack, 0f);
        }
    }
}
