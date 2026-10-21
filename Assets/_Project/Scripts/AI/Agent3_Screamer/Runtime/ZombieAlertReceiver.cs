using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace ScreamerAI
{
    /// <summary>
    /// DROP-IN ALERT LISTENER for any other zombie (Tank, Runner, test dummies ...).
    /// Add it next to a NavMeshAgent and the zombie will walk to the position the Screamer broadcast.
    ///
    /// Teammates with their own AI: untick 'driveMovement' and subscribe to 'onAlerted'
    /// (or read LastAlertPosition / IsAlerted) so your Behavior Tree / A* decides how to react.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class ZombieAlertReceiver : MonoBehaviour, IAlertListener
    {
        public enum State { Idle, Alerted, Investigating, Returning }

        [Tooltip("ON = this component moves the agent itself (good for dummies). OFF = you handle movement yourself.")]
        public bool driveMovement = true;
        public float alertMoveSpeed = 3.4f;
        public float returnSpeed = 1.0f;
        public float investigateSeconds = 6f;

        [Tooltip("Fired when a scream is heard. Argument = where the player was last seen.")]
        public UnityEvent<Vector3> onAlerted;

        public State CurrentState { get; private set; }
        public bool IsAlerted { get { return CurrentState == State.Alerted || CurrentState == State.Investigating; } }
        public Vector3 LastAlertPosition { get; private set; }
        public float LastAlertIntensity { get; private set; }
        public float LastAlertTime { get; private set; }

        public Transform AlertTransform { get { return transform; } }

        NavMeshAgent agent;
        Vector3 home;
        float investigateUntil;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            home = transform.position;
        }

        void Start()
        {
            if (!agent.isOnNavMesh)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(transform.position, out hit, 6f, NavMesh.AllAreas)) agent.Warp(hit.position);
            }
        }

        void OnEnable()
        {
            AlertSystem.Register(this);
        }

        void OnDisable()
        {
            AlertSystem.Unregister(this);
        }

        public void OnAlert(AlertEvent alert)
        {
            LastAlertPosition = alert.TargetPosition;
            LastAlertIntensity = alert.Intensity;
            LastAlertTime = Time.time;
            CurrentState = State.Alerted;

            if (onAlerted != null) onAlerted.Invoke(alert.TargetPosition);

            if (driveMovement && agent != null && agent.isOnNavMesh)
            {
                NavMeshHit hit;
                Vector3 dest = NavMesh.SamplePosition(alert.TargetPosition, out hit, 5f, NavMesh.AllAreas) ? hit.position : alert.TargetPosition;
                agent.isStopped = false;
                agent.speed = alertMoveSpeed;
                agent.SetDestination(dest);
            }
        }

        void Update()
        {
            if (!driveMovement || agent == null || !agent.isOnNavMesh) return;

            switch (CurrentState)
            {
                case State.Alerted:
                    if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 1.2f)
                    {
                        CurrentState = State.Investigating;
                        investigateUntil = Time.time + investigateSeconds;
                    }
                    break;

                case State.Investigating:
                    if (Time.time >= investigateUntil)
                    {
                        CurrentState = State.Returning;
                        agent.speed = returnSpeed;
                        agent.SetDestination(home);
                    }
                    break;

                case State.Returning:
                    if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f)
                        CurrentState = State.Idle;
                    break;
            }
        }
    }
}
