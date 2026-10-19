using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ScreamerAI
{
    /// <summary>
    /// Put this on any prop the Screamer may hide behind (the WreckedLocker prefab has it).
    /// The prop needs a Collider so it actually blocks the player's line of sight.
    /// </summary>
    [DisallowMultipleComponent]
    public class HideSpot : MonoBehaviour
    {
        public static readonly List<HideSpot> All = new List<HideSpot>();

        [Tooltip("How far behind the prop (on the side away from the threat) the zombie crouches.")]
        public float coverDistance = 1.15f;

        [Tooltip("Head height of the crouching zombie - the cover must block a ray at this height.")]
        public float crouchedHeadHeight = 1.1f;

        public Component Occupant { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDisable()
        {
            All.Remove(this);
        }

        public bool IsAvailableTo(Component who)
        {
            return Occupant == null || Occupant == who;
        }

        public void Claim(Component who)
        {
            Occupant = who;
        }

        public void Release(Component who)
        {
            if (Occupant == who) Occupant = null;
        }

        /// <summary>The spot behind this prop as seen from 'threatPos', snapped to the NavMesh.</summary>
        public bool TryGetCoverPoint(Vector3 threatPos, out Vector3 point)
        {
            Vector3 away = transform.position - threatPos;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away.Normalize();

            Vector3 raw = transform.position + away * coverDistance;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(raw, out hit, 1.5f, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
            point = raw;
            return false;
        }

        /// <summary>True if a crouching zombie standing at 'point' cannot be seen from 'threatEye'.</summary>
        public bool CoversPointFrom(Vector3 threatEye, Vector3 point, Transform self, Transform threat)
        {
            return LineOfSight.IsBlocked(threatEye, point + Vector3.up * crouchedHeadHeight, self, threat)
                && LineOfSight.IsBlocked(threatEye, point + Vector3.up * 0.5f, self, threat);
        }
    }
}
