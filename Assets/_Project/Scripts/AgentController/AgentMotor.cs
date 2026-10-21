using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// AGENT CONTROLLER (Member 4). Takes the path data computed by the AI (an array of coordinates) and turns it
    /// into smooth movement: pure-pursuit look-ahead on the polyline, acceleration / braking, rotation limited by a
    /// turn rate (no snapping, no teleporting), slowing for sharp turns and on arrival, gravity + slopes through a
    /// CharacterController, stuck detection, and walk/run animation that matches the real speed.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class AgentMotor : MonoBehaviour
    {
        [Header("Speed")]
        public float maxSpeed = 4f;
        public float acceleration = 14f;
        public float deceleration = 24f;

        [Header("Turning")]
        public float turnSpeed = 480f;          // degrees per second
        public float slowTurnAngle = 80f;       // speed drops towards 30% when facing this far off

        [Header("Path following")]
        public float lookAhead = 1.4f;
        public float waypointRadius = 0.45f;
        public float arriveDistance = 0.45f;
        public float slowDownDistance = 1.8f;
        public bool slowAtEnd = true;

        [Header("Physics")]
        public float gravity = -25f;

        public Vector3[] PathArray { get { return path; } }
        public bool HasPath { get { return path != null && path.Length > 0 && !arrived; } }
        public bool Arrived { get { return arrived; } }
        public bool IsBlocked { get; private set; }
        public float CurrentSpeed { get { return speed; } }
        public int SegmentIndex { get { return seg; } }
        public float RunBlendReference = 4.5f;

        public event System.Action PathCompleted;

        CharacterController cc;
        ZombieAnimDriver anim;
        Vector3[] path;
        int seg;
        bool arrived = true;
        float speed;
        float vy;
        float attackPose;
        float stuckTimer;
        float blockedAccum;
        Vector3 lastCheckPos;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            anim = GetComponent<ZombieAnimDriver>();
            if (anim == null) anim = gameObject.AddComponent<ZombieAnimDriver>();
            lastCheckPos = transform.position;
        }

        /// <summary>Give the motor a new path (array of world points). The first point should be near the agent.</summary>
        public void SetPath(System.Collections.Generic.IList<Vector3> points)
        {
            if (points == null || points.Count == 0) { Stop(); return; }
            path = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++) path[i] = points[i];
            seg = 0;
            arrived = false;
        }

        public void Stop()
        {
            path = null;
            arrived = true;
        }

        public void SetAttackPose(float value) { attackPose = Mathf.Clamp01(value); }

        /// <summary>Turn on the spot towards a point (used while attacking). Returns the remaining angle.</summary>
        public float FaceTowards(Vector3 worldPos, float dt)
        {
            Vector3 d = worldPos - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) return 0f;
            Quaternion target = Quaternion.LookRotation(d.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
            return Quaternion.Angle(transform.rotation, target);
        }

        static float Planar(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        void Update()
        {
            if (!cc.enabled) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float targetSpeed = 0f;
            Vector3 pos = transform.position;

            if (!arrived && path != null && path.Length > 0)
            {
                int last = path.Length - 1;

                // advance along the polyline
                while (seg < last)
                {
                    Vector3 a = path[seg];
                    Vector3 b = path[seg + 1];
                    Vector3 ab = b - a; ab.y = 0f;
                    Vector3 bp = pos - b; bp.y = 0f;
                    if (Planar(pos, b) < waypointRadius || Vector3.Dot(ab, bp) > 0f) seg++;
                    else break;
                }

                float distToEnd = Planar(pos, path[last]);
                if (seg >= last && distToEnd < arriveDistance)
                {
                    arrived = true;
                    if (PathCompleted != null) PathCompleted();
                }
                else
                {
                    Vector3 aim = LookAheadPoint(pos);
                    Vector3 dir = aim - pos;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        dir.Normalize();
                        Quaternion want = Quaternion.LookRotation(dir, Vector3.up);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * dt);

                        float off = Vector3.Angle(transform.forward, dir);
                        float turnFactor = Mathf.Lerp(1f, 0.3f, Mathf.Clamp01(off / Mathf.Max(1f, slowTurnAngle)));
                        float endFactor = slowAtEnd ? Mathf.Clamp01(distToEnd / Mathf.Max(0.1f, slowDownDistance)) : 1f;
                        endFactor = Mathf.Max(endFactor, 0.3f);
                        targetSpeed = maxSpeed * turnFactor * endFactor;
                    }
                }
            }

            float rate = targetSpeed > speed ? acceleration : deceleration;
            speed = Mathf.MoveTowards(speed, targetSpeed, rate * dt);

            if (cc.isGrounded && vy < 0f) vy = -2f;
            vy += gravity * dt;
            Vector3 move = transform.forward * speed;
            move.y = vy;
            cc.Move(move * dt);

            UpdateStuck(dt, targetSpeed);
            if (anim != null) anim.Set(speed, Mathf.Clamp01((speed - 1.5f) / Mathf.Max(0.5f, RunBlendReference - 1.5f)), attackPose, 0f);
        }

        Vector3 LookAheadPoint(Vector3 pos)
        {
            int last = path.Length - 1;
            Vector3 cur = pos;
            float remaining = lookAhead;
            int i = Mathf.Min(seg + 1, last);
            while (i <= last)
            {
                Vector3 next = path[i];
                float d = Planar(cur, next);
                if (d >= remaining && d > 0.0001f)
                {
                    Vector3 dir = next - cur;
                    dir.y = 0f;
                    return cur + dir.normalized * remaining;
                }
                remaining -= d;
                cur = next;
                i++;
            }
            return path[last];
        }

        void UpdateStuck(float dt, float targetSpeed)
        {
            stuckTimer += dt;
            if (stuckTimer < 0.25f) return;

            float moved = Planar(transform.position, lastCheckPos);
            float expected = targetSpeed * stuckTimer;
            if (targetSpeed > 1f && moved < expected * 0.2f) blockedAccum += stuckTimer;
            else blockedAccum = Mathf.Max(0f, blockedAccum - stuckTimer * 2f);

            IsBlocked = blockedAccum >= 0.5f;
            lastCheckPos = transform.position;
            stuckTimer = 0f;
        }

        public void ClearBlocked()
        {
            blockedAccum = 0f;
            IsBlocked = false;
        }

        void OnDrawGizmosSelected()
        {
            if (path == null) return;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < path.Length - 1; i++) Gizmos.DrawLine(path[i] + Vector3.up * 0.2f, path[i + 1] + Vector3.up * 0.2f);
        }
    }
}
