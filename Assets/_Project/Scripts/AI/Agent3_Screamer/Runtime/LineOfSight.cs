using UnityEngine;

namespace ScreamerAI
{
    /// <summary>Small line-of-sight helper shared by the Screamer's senses, cover checks and the alert system.</summary>
    public static class LineOfSight
    {
        static readonly RaycastHit[] buffer = new RaycastHit[32];

        /// <summary>
        /// True if something solid lies between 'from' and 'to'.
        /// Triggers are ignored, and anything that belongs to 'ignoreA' / 'ignoreB' (and their children) is skipped,
        /// so pass the observer and the target to avoid blocking on themselves.
        /// </summary>
        public static bool IsBlocked(Vector3 from, Vector3 to, Transform ignoreA = null, Transform ignoreB = null)
        {
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 0.01f) return false;

            int n = Physics.RaycastNonAlloc(from, delta / dist, buffer, dist,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < n; i++)
            {
                Transform t = buffer[i].collider.transform;
                if (ignoreA != null && t.IsChildOf(ignoreA)) continue;
                if (ignoreB != null && t.IsChildOf(ignoreB)) continue;
                return true;
            }
            return false;
        }
    }
}
