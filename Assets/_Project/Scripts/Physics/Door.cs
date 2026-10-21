using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>
    /// Hinged door (Member 2 - Systems Engineer). The player opens/closes it with E.
    /// A closed door carves a hole in the NavMesh and is an IBlocker: the Runner pays an A* re-routing
    /// cost to cross it, the Tank smashes it, and any zombie can shoulder it open after a short delay.
    /// Built by LevelBuilder (pivot = hinge, slab = the moving leaf).
    /// </summary>
    public class Door : MonoBehaviour, IBlocker
    {
        public enum DoorState { Closed, Opening, Open, Closing, Broken }

        public Transform pivot;
        public Transform slab;
        public BoxCollider slabCollider;
        public NavMeshObstacle obstacle;

        public float openAngle = 100f;
        public float speed = 170f;
        public float health = 120f;
        public float pathPenalty = 30f;

        public DoorState State { get; private set; }

        float angle;
        float targetAngle;
        float moveSpeed;
        Quaternion baseRot;
        bool cached;

        void Awake() { Cache(); }

        void Cache()
        {
            if (cached || pivot == null) return;
            baseRot = pivot.localRotation;
            moveSpeed = speed;
            cached = true;
        }

        void OnEnable()
        {
            Cache();
            BlockerRegistry.Register(this);
            SetObstacle(State == DoorState.Closed);
        }

        void OnDisable() { BlockerRegistry.Unregister(this); }

        // ------------------------------------------------------------ IBlocker
        public bool IsBlocking { get { return (State == DoorState.Closed || State == DoorState.Closing) && angle < 25f && angle > -25f; } }
        public float PathPenalty { get { return pathPenalty; } }
        public bool TankCanBreak { get { return true; } }
        public Transform BlockTransform { get { return slab != null ? slab : transform; } }
        public Vector3 BreakPoint { get { return slabCollider != null ? slabCollider.bounds.center : transform.position; } }

        public Bounds BlockBounds
        {
            get
            {
                if (slabCollider != null)
                {
                    Bounds b = slabCollider.bounds;
                    b.Expand(new Vector3(0.3f, 0f, 0.3f));
                    return b;
                }
                return new Bounds(transform.position, Vector3.one);
            }
        }

        public void TakeBlockDamage(float amount, GameObject by)
        {
            if (State == DoorState.Broken) return;
            health -= amount;
            GameAudio.PlayAt(GameAudio.Impact, BreakPoint, 0.9f);
            if (health <= 0f) Break(by != null ? by.transform.position : BreakPoint - Vector3.forward);
        }

        // ------------------------------------------------------------ interaction
        public string Prompt
        {
            get
            {
                if (State == DoorState.Broken) return "";
                return (State == DoorState.Closed || State == DoorState.Closing) ? "[E] Open door" : "[E] Close door";
            }
        }

        public void Toggle(Vector3 actorPos)
        {
            if (State == DoorState.Broken) return;
            if (State == DoorState.Closed || State == DoorState.Closing) Open(actorPos, speed, true);
            else Close(true);
        }

        public void Open(Vector3 actorPos, float openSpeed, bool makeNoise)
        {
            if (State == DoorState.Broken || pivot == null) return;
            Cache();
            Vector3 local = pivot.parent != null ? pivot.parent.InverseTransformPoint(actorPos) : actorPos;
            Vector3 pivotLocal = pivot.localPosition;
            // side of the door plane the actor is on (door leaf lies along local X, normal is local Z)
            float side = Vector3.Dot(local - pivotLocal, baseRot * Vector3.forward);
            targetAngle = side >= 0f ? openAngle : -openAngle;
            moveSpeed = openSpeed;
            State = DoorState.Opening;
            SetObstacle(false);
            GameAudio.PlayAt(GameAudio.DoorCreak, pivot.position + Vector3.up, 0.6f);
            if (makeNoise) NoiseSystem.Emit(pivot.position, 9f, NoiseKind.Door);
            BlockerRegistry.NotifyChanged();
        }

        public void Close(bool makeNoise)
        {
            if (State == DoorState.Broken) return;
            Cache();
            targetAngle = 0f;
            moveSpeed = speed;
            State = DoorState.Closing;
            if (makeNoise) NoiseSystem.Emit(pivot.position, 7f, NoiseKind.Door);
            BlockerRegistry.NotifyChanged();
        }

        /// <summary>A zombie shoulders the door open (used by the Runner when it is stuck against it).</summary>
        public void Bash(Vector3 from)
        {
            if (State == DoorState.Broken) return;
            Open(from, speed * 2.2f, false);
            GameAudio.PlayAt(GameAudio.DoorSlam, BreakPoint, 0.8f);
            NoiseSystem.Emit(BreakPoint, 12f, NoiseKind.Door);
        }

        void Break(Vector3 from)
        {
            State = DoorState.Broken;
            SetObstacle(false);
            GameAudio.PlayAt(GameAudio.WoodBreak, BreakPoint, 1f);
            NoiseSystem.Emit(BreakPoint, 22f, NoiseKind.Impact);
            if (slab != null)
            {
                slab.SetParent(null, true);
                Rigidbody rb = slab.gameObject.AddComponent<Rigidbody>();
                rb.mass = 20f;
                Vector3 push = (slab.position - from);
                push.y = 0.2f;
                rb.AddForce(push.normalized * 6f, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * 3f, ForceMode.Impulse);
                Destroy(slab.gameObject, 8f);
            }
            GameFX.Debris(BreakPoint, new Color(0.35f, 0.24f, 0.14f), 6, 3f);
            BlockerRegistry.NotifyChanged();
        }

        void SetObstacle(bool on)
        {
            if (obstacle != null) obstacle.enabled = on;
        }

        void Update()
        {
            if (State == DoorState.Broken || pivot == null) return;
            if (State == DoorState.Opening || State == DoorState.Closing)
            {
                angle = Mathf.MoveTowards(angle, targetAngle, moveSpeed * Time.deltaTime);
                pivot.localRotation = baseRot * Quaternion.Euler(0f, angle, 0f);
                if (Mathf.Approximately(angle, targetAngle))
                {
                    if (targetAngle == 0f)
                    {
                        State = DoorState.Closed;
                        SetObstacle(true);
                        GameAudio.PlayAt(GameAudio.DoorSlam, pivot.position + Vector3.up, 0.35f);
                    }
                    else State = DoorState.Open;
                    BlockerRegistry.NotifyChanged();
                }
            }
        }
    }
}
