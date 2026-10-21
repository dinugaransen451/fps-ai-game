using System.Collections.Generic;
using UnityEngine;

namespace ScreamerAI
{
    /// <summary>The four actions the Screamer's Utility AI chooses between.
    /// The numeric values are used as indices into ScreamerBrain.Scores.</summary>
    public enum ScreamerAction
    {
        Wander = 0,   // calm idle shambling around its home area
        Scream = 1,   // "alert" - broadcast the player's position to every other zombie in earshot
        Flee = 2,     // run away from the player (the Screamer never fights)
        Hide = 3      // crouch behind a HideSpot prop, out of the player's line of sight
    }

    /// <summary>Response curves that turn a raw input (distance, threat, ...) into a 0..1 "utility" value.</summary>
    public static class UtilityCurves
    {
        /// <summary>y = slope * x + shift, clamped to 0..1.</summary>
        public static float Linear(float x, float slope = 1f, float shift = 0f)
        {
            return Mathf.Clamp01(slope * x + shift);
        }

        /// <summary>y = x^exponent (x clamped to 0..1). exponent &gt; 1 = slow start, steep finish.</summary>
        public static float Power(float x, float exponent = 2f)
        {
            return Mathf.Pow(Mathf.Clamp01(x), exponent);
        }

        /// <summary>S-curve. Good for "switch on around this value" considerations.</summary>
        public static float Logistic(float x, float steepness = 10f, float midpoint = 0.5f)
        {
            return 1f / (1f + Mathf.Exp(-steepness * (x - midpoint)));
        }

        /// <summary>Triangle peaking at 'centre' (=1) and falling to 0 at centre +/- halfWidth. Used for "sweet spot" distances.</summary>
        public static float Bell(float x, float centre, float halfWidth)
        {
            if (halfWidth <= 0.0001f) return 0f;
            return Mathf.Clamp01(1f - Mathf.Abs(x - centre) / halfWidth);
        }

        /// <summary>1 - x.</summary>
        public static float Inverse(float x)
        {
            return 1f - Mathf.Clamp01(x);
        }
    }

    /// <summary>One scored input of an action (kept so the debug HUD can show *why* an action won).</summary>
    public struct ConsiderationValue
    {
        public string Label;
        public float Input;    // raw, normalised input
        public float Output;   // after the response curve (0..1)
    }

    /// <summary>
    /// The score of one action = product of all its consideration outputs, using the
    /// "compensation factor" from Dave Mark's Infinite Axis Utility System so that an
    /// action with many considerations is not unfairly punished by multiplication.
    /// </summary>
    public class ActionScore
    {
        public readonly ScreamerAction Action;
        public float Weight = 1f;
        public float Score;
        public readonly List<ConsiderationValue> Considerations = new List<ConsiderationValue>(6);

        public ActionScore(ScreamerAction action)
        {
            Action = action;
        }

        public void Begin(float weight)
        {
            Weight = weight;
            Score = 0f;
            Considerations.Clear();
        }

        public void Consider(string label, float input, float output)
        {
            Considerations.Add(new ConsiderationValue { Label = label, Input = input, Output = Mathf.Clamp01(output) });
        }

        public void Finish()
        {
            int n = Considerations.Count;
            if (n == 0) { Score = 0f; return; }

            float modification = 1f - 1f / n;
            float product = 1f;
            for (int i = 0; i < n; i++)
            {
                float v = Considerations[i].Output;
                float makeUp = (1f - v) * modification;
                v += makeUp * v;
                product *= v;
            }
            Score = product * Weight;
        }
    }
}
