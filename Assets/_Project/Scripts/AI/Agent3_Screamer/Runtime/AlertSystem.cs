using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScreamerAI
{
    /// <summary>Data sent to every zombie that hears a Screamer's scream.</summary>
    public struct AlertEvent
    {
        public Vector3 TargetPosition;   // where the player was last seen - go here
        public Vector3 Origin;           // where the scream came from
        public float Intensity;          // 0..1, how clearly THIS listener heard it (distance + wall falloff)
        public float Radius;             // scream radius
        public GameObject Source;        // the Screamer
        public float Time;               // Time.time of the broadcast
    }

    /// <summary>
    /// Implement this on ANY zombie (Tank, Runner, ...) that should react to a scream.
    /// Register in OnEnable / unregister in OnDisable (or just add the ZombieAlertReceiver component).
    /// </summary>
    public interface IAlertListener
    {
        Transform AlertTransform { get; }
        void OnAlert(AlertEvent alert);
    }

    /// <summary>
    /// The alert-broadcast system. The Screamer calls AlertSystem.Broadcast(...) and every registered
    /// listener inside the scream radius is told where the player is. Walls between the Screamer and a
    /// listener muffle the scream (lower intensity) and very faint screams are not heard at all.
    /// </summary>
    public static class AlertSystem
    {
        static readonly List<IAlertListener> listeners = new List<IAlertListener>();
        static readonly List<IAlertListener> snapshot = new List<IAlertListener>();

        /// <summary>Raised after every broadcast (useful for UI, sound design or the Director AI).</summary>
        public static event Action<AlertEvent> AlertBroadcast;

        /// <summary>Intensity multiplier applied when a wall is between the scream and the listener.</summary>
        public static float WallAttenuation = 0.55f;

        /// <summary>Listeners that hear the scream quieter than this ignore it.</summary>
        public static float MinAudibleIntensity = 0.12f;

        public static int ListenerCount { get { return listeners.Count; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            listeners.Clear();
            snapshot.Clear();
            AlertBroadcast = null;
        }

        public static void Register(IAlertListener l)
        {
            if (l != null && !listeners.Contains(l)) listeners.Add(l);
        }

        public static void Unregister(IAlertListener l)
        {
            listeners.Remove(l);
        }

        /// <summary>Tell every zombie in earshot that the player is at 'target'. Returns how many zombies heard it.</summary>
        public static int Broadcast(Vector3 target, Vector3 origin, float radius, GameObject source)
        {
            snapshot.Clear();
            snapshot.AddRange(listeners);

            Transform sourceT = source != null ? source.transform : null;
            int heard = 0;

            for (int i = 0; i < snapshot.Count; i++)
            {
                IAlertListener l = snapshot[i];
                UnityEngine.Object uo = l as UnityEngine.Object;
                if (uo == null) continue;                       // destroyed
                Transform lt = l.AlertTransform;
                if (lt == null) continue;
                if (sourceT != null && lt.IsChildOf(sourceT)) continue;

                Vector3 lp = lt.position;
                float d = Vector3.Distance(origin, lp);
                if (d > radius) continue;

                float intensity = 1f - d / radius;
                if (LineOfSight.IsBlocked(origin + Vector3.up * 1.5f, lp + Vector3.up, sourceT, lt))
                    intensity *= WallAttenuation;
                if (intensity < MinAudibleIntensity) continue;

                l.OnAlert(new AlertEvent
                {
                    TargetPosition = target,
                    Origin = origin,
                    Intensity = intensity,
                    Radius = radius,
                    Source = source,
                    Time = UnityEngine.Time.time
                });
                heard++;
            }

            if (AlertBroadcast != null)
            {
                AlertBroadcast(new AlertEvent
                {
                    TargetPosition = target,
                    Origin = origin,
                    Intensity = 1f,
                    Radius = radius,
                    Source = source,
                    Time = UnityEngine.Time.time
                });
            }
            return heard;
        }

        /// <summary>How many registered zombies are within 'radius' of 'position' (excluding 'exclude').</summary>
        public static int CountInRadius(Vector3 position, float radius, Transform exclude)
        {
            int count = 0;
            for (int i = 0; i < listeners.Count; i++)
            {
                Transform t = listeners[i].AlertTransform;
                if (t == null || (exclude != null && t.IsChildOf(exclude))) continue;
                if (Vector3.Distance(position, t.position) <= radius) count++;
            }
            return count;
        }

        /// <summary>Distance to the closest registered zombie (999 if there are none).</summary>
        public static float NearestDistance(Vector3 position, Transform exclude)
        {
            float best = 999f;
            for (int i = 0; i < listeners.Count; i++)
            {
                Transform t = listeners[i].AlertTransform;
                if (t == null || (exclude != null && t.IsChildOf(exclude))) continue;
                float d = Vector3.Distance(position, t.position);
                if (d < best) best = d;
            }
            return best;
        }
    }
}
