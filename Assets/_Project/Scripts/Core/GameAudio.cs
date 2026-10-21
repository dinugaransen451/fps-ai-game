using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>All sound effects are synthesised at runtime, so the game needs no audio files.</summary>
    public static class GameAudio
    {
        const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static AudioSource ui;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { clips.Clear(); ui = null; }

        delegate float Gen(float t, System.Random r);

        static AudioClip Make(string name, float seconds, Gen g, float gain = 1f, bool loop = false)
        {
            AudioClip existing;
            if (clips.TryGetValue(name, out existing) && existing != null) return existing;

            System.Random rng = new System.Random(name.GetHashCode());
            int n = Mathf.Max(1, (int)(seconds * Rate));
            float[] d = new float[n];
            float peak = 0.0001f;
            for (int i = 0; i < n; i++)
            {
                float v = g(i / (float)Rate, rng);
                d[i] = v;
                float a = Mathf.Abs(v);
                if (a > peak) peak = a;
            }
            float scale = gain / Mathf.Max(1f, peak);
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i] * scale, -1f, 1f);

            AudioClip c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            clips[name] = c;
            return c;
        }

        static float Noise(System.Random r) { return (float)(r.NextDouble() * 2.0 - 1.0); }
        static float Sin(float f, float t) { return Mathf.Sin(2f * Mathf.PI * f * t); }

        public static AudioClip Gunshot { get { return Make("gunshot", 0.45f, (t, r) => (Noise(r) * Mathf.Exp(-t * 32f) * 0.9f + Sin(70f + 90f * Mathf.Exp(-t * 20f), t) * Mathf.Exp(-t * 11f) * 0.9f), 0.9f); } }
        public static AudioClip DryFire { get { return Make("dry", 0.08f, (t, r) => Noise(r) * Mathf.Exp(-t * 90f) * 0.5f, 0.5f); } }
        public static AudioClip Reload { get { return Make("reload", 0.9f, (t, r) => ((t < 0.07f || (t > 0.55f && t < 0.62f) || (t > 0.8f && t < 0.84f)) ? Noise(r) * 0.6f * Mathf.Exp(-((t < 0.2f ? t : (t < 0.7f ? t - 0.55f : t - 0.8f)) * 70f)) : 0f), 0.6f); } }
        public static AudioClip Step { get { return Make("step", 0.12f, (t, r) => Sin(70f, t) * Mathf.Exp(-t * 35f) + Noise(r) * 0.12f * Mathf.Exp(-t * 60f), 0.35f); } }
        public static AudioClip DoorCreak { get { return Make("creak", 0.9f, (t, r) => Sin(180f + 90f * Mathf.Sin(t * 5f) + 120f * t, t) * 0.4f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.9f)) + Noise(r) * 0.05f, 0.45f); } }
        public static AudioClip DoorSlam { get { return Make("slam", 0.5f, (t, r) => Sin(60f, t) * Mathf.Exp(-t * 14f) + Noise(r) * 0.4f * Mathf.Exp(-t * 40f), 0.8f); } }
        public static AudioClip WoodBreak { get { return Make("woodbreak", 0.6f, (t, r) => Noise(r) * Mathf.Exp(-t * 9f) * (0.5f + 0.5f * Mathf.Sin(t * 90f)) + Sin(95f, t) * Mathf.Exp(-t * 12f) * 0.6f, 0.9f); } }
        public static AudioClip Glass { get { return Make("glass", 0.7f, (t, r) => Noise(r) * Mathf.Exp(-t * 12f) * (0.4f + 0.6f * Mathf.Sin(t * 8000f * (1f + t))) + Sin(3200f, t) * Mathf.Exp(-t * 25f) * 0.3f, 0.8f); } }
        public static AudioClip Explosion { get { return Make("explosion", 1.8f, (t, r) => Noise(r) * Mathf.Exp(-t * 3.2f) * 0.8f + Sin(45f - 15f * t, t) * Mathf.Exp(-t * 2.4f), 1f); } }
        public static AudioClip Pickup { get { return Make("pickup", 0.35f, (t, r) => (Sin(660f, t) * (t < 0.12f ? 1f : 0f) + Sin(990f, t) * (t >= 0.12f ? 1f : 0f)) * Mathf.Exp(-t * 7f), 0.4f); } }
        public static AudioClip HitMarker { get { return Make("hitmarker", 0.08f, (t, r) => Sin(1400f, t) * Mathf.Exp(-t * 50f), 0.35f); } }
        public static AudioClip Impact { get { return Make("impact", 0.25f, (t, r) => Noise(r) * Mathf.Exp(-t * 30f) * 0.6f + Sin(120f, t) * Mathf.Exp(-t * 20f), 0.5f); } }
        public static AudioClip PlayerHurt { get { return Make("hurt", 0.35f, (t, r) => Sin(150f - 60f * t, t) * Mathf.Exp(-t * 8f) + Noise(r) * 0.15f * Mathf.Exp(-t * 20f), 0.7f); } }
        public static AudioClip Heartbeat { get { return Make("heart", 1.0f, (t, r) => (Sin(52f, t) * Mathf.Exp(-Mathf.Abs(t - 0.05f) * 28f) + 0.7f * Sin(48f, t) * Mathf.Exp(-Mathf.Abs(t - 0.32f) * 30f)), 0.8f); } }
        public static AudioClip Growl { get { return Make("growl", 1.2f, (t, r) => (Sin(70f + 15f * Mathf.Sin(t * 9f), t) * 0.6f + Sin(142f + 25f * Mathf.Sin(t * 7f), t) * 0.35f + Noise(r) * 0.15f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.2f)), 0.6f); } }
        public static AudioClip Roar { get { return Make("roar", 1.5f, (t, r) => (Sin(48f + 10f * Mathf.Sin(t * 6f), t) * 0.7f + Sin(97f, t) * 0.4f + Noise(r) * 0.2f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.5f)), 0.85f); } }
        public static AudioClip Swing { get { return Make("swing", 0.3f, (t, r) => Noise(r) * 0.3f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.3f)), 0.35f); } }
        public static AudioClip Hum
        {
            get
            {
                // loops seamlessly: every frequency has a whole number of cycles in 6 s
                return Make("hum", 6f, (t, r) => Sin(50f, t) * 0.5f + Sin(100.1666f * 1f, t) * 0.15f + Sin(0.5f, t) * 0.2f * Sin(37f, t), 0.22f, true);
            }
        }

        public static void PlayAt(AudioClip clip, Vector3 pos, float volume = 1f)
        {
            if (clip == null) return;
            AudioSource.PlayClipAtPoint(clip, pos, volume);
        }

        public static void Play2D(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;
            if (ui == null)
            {
                GameObject go = new GameObject("UIAudio");
                Object.DontDestroyOnLoad(go);
                ui = go.AddComponent<AudioSource>();
                ui.spatialBlend = 0f;
                ui.playOnAwake = false;
            }
            ui.PlayOneShot(clip, volume);
        }

        /// <summary>Looping dark ambience attached to a host object.</summary>
        public static void StartAmbience(GameObject host)
        {
            AudioSource s = host.AddComponent<AudioSource>();
            s.clip = Hum;
            s.loop = true;
            s.spatialBlend = 0f;
            s.volume = 0.5f;
            s.Play();
        }
    }
}
