using UnityEngine;
using ScreamerAI;

namespace ZombieGame
{
    /// <summary>Shared sight check for the Tank and the Runner (cone + range + line of sight).</summary>
    public static class Senses
    {
        public static bool CanSee(Transform self, float eyeHeight, Transform target, float range, float angle, float closeRange, out float distance)
        {
            distance = 999f;
            if (target == null) return false;

            Vector3 flat = target.position - self.position;
            flat.y = 0f;
            distance = flat.magnitude;
            float vertical = Mathf.Abs(target.position.y - self.position.y);

            float mult = 1f;
            if (PlayerController.Instance != null) mult = PlayerController.Instance.SightMultiplier;
            float effRange = range * mult;

            if (distance > Mathf.Max(effRange, closeRange)) return false;
            if (vertical > 2.5f) return false;   // other floor

            bool inCone = distance <= closeRange || Vector3.Angle(self.forward, flat) <= angle * 0.5f;
            if (!inCone) return false;

            Vector3 eye = self.position + Vector3.up * eyeHeight;
            Vector3 chest = target.position + Vector3.up * 1.2f;
            return !LineOfSight.IsBlocked(eye, chest, self, target);
        }
    }
}
