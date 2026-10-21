using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    public enum ZombieKind { Runner, Tank, Screamer }

    /// <summary>Everything a hit needs to know.</summary>
    public struct DamageInfo
    {
        public float amount;
        public Vector3 point;
        public Vector3 direction;
        public bool headshot;
        public GameObject source;

        public DamageInfo(float amount, Vector3 point, Vector3 direction, bool headshot, GameObject source)
        {
            this.amount = amount;
            this.point = point;
            this.direction = direction;
            this.headshot = headshot;
            this.source = source;
        }
    }

    /// <summary>Implemented by the player and by every zombie.</summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeDamage(DamageInfo info);
    }

    /// <summary>
    /// Anything that can physically block a path: closed doors and barricades (Member 2's physics objects).
    /// The Tank breaks them, the Runner re-routes around them (extra A* cost), the Director ignores them.
    /// </summary>
    public interface IBlocker
    {
        bool IsBlocking { get; }
        Bounds BlockBounds { get; }
        float PathPenalty { get; }
        bool TankCanBreak { get; }
        Vector3 BreakPoint { get; }
        Transform BlockTransform { get; }
        void TakeBlockDamage(float amount, GameObject by);
    }

    /// <summary>Registry of all blockers. NavGrid listens to Changed to refresh its re-routing costs.</summary>
    public static class BlockerRegistry
    {
        public static readonly List<IBlocker> All = new List<IBlocker>();
        public static event System.Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            Changed = null;
        }

        public static void Register(IBlocker b)
        {
            if (b != null && !All.Contains(b)) All.Add(b);
            NotifyChanged();
        }

        public static void Unregister(IBlocker b)
        {
            All.Remove(b);
            NotifyChanged();
        }

        public static void NotifyChanged()
        {
            if (Changed != null) Changed();
        }

        /// <summary>Closest blocking blocker whose bounds are within 'radius' of 'pos' (null if none).</summary>
        public static IBlocker FindNear(Vector3 pos, float radius)
        {
            IBlocker best = null;
            float bestSqr = radius * radius;
            for (int i = 0; i < All.Count; i++)
            {
                IBlocker b = All[i];
                if (b == null || !b.IsBlocking) continue;
                float sqr = b.BlockBounds.SqrDistance(pos);
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = b;
                }
            }
            return best;
        }
    }

    public enum NoiseKind { Footstep, Gunshot, Glass, Explosion, Door, Impact }

    public struct NoiseEvent
    {
        public Vector3 position;
        public float radius;
        public NoiseKind kind;
        public float time;
        public GameObject source;
    }

    /// <summary>
    /// Sound propagation for the zombies. The player's footsteps, gunshots, doors and thrown objects all emit
    /// noise here; Tanks and Runners subscribe and investigate. Walls between the noise and the listener muffle it.
    /// </summary>
    public static class NoiseSystem
    {
        public static event System.Action<NoiseEvent> NoiseEmitted;
        public static NoiseEvent Last;
        public static float WallMuffle = 0.6f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            NoiseEmitted = null;
            Last = default(NoiseEvent);
        }

        public static void Emit(Vector3 position, float radius, NoiseKind kind, GameObject source = null)
        {
            NoiseEvent e = new NoiseEvent();
            e.position = position;
            e.radius = radius;
            e.kind = kind;
            e.time = Time.time;
            e.source = source;
            Last = e;
            if (NoiseEmitted != null) NoiseEmitted(e);
        }

        /// <summary>Can a listener standing at 'listenerPos' hear this noise?</summary>
        public static bool CanHear(NoiseEvent e, Vector3 listenerPos, float sensitivity = 1f)
        {
            float radius = e.radius * sensitivity;
            float d = Vector3.Distance(e.position, listenerPos);
            if (d > radius) return false;
            bool blocked = ScreamerAI.LineOfSight.IsBlocked(e.position + Vector3.up * 0.5f, listenerPos + Vector3.up * 1.4f);
            if (blocked && d > radius * WallMuffle) return false;
            return true;
        }
    }
}
