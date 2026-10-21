using UnityEngine;

namespace ZombieGame
{
    /// <summary>Horror lighting: random dropouts and buzzing flicker. Optionally drives an emissive fixture too.</summary>
    public class FlickerLight : MonoBehaviour
    {
        public enum Mode { Steady, Buzz, Failing, Dead, Pulse }

        public Mode mode = Mode.Buzz;
        public Light lightSource;
        public Renderer fixture;
        public float baseIntensity = 2.6f;
        public Color emissionColor = new Color(0.85f, 0.95f, 1f);

        float seed;
        float blackoutUntil;
        float nextEvent;
        Material fixtureMat;

        void Awake()
        {
            if (lightSource == null) lightSource = GetComponent<Light>();
            seed = Random.value * 100f;
            nextEvent = Time.time + Random.Range(1f, 6f);
            if (fixture != null) fixtureMat = fixture.material;   // runtime instance
        }

        void Update()
        {
            if (lightSource == null) return;
            float k = 1f;
            float t = Time.time + seed;

            switch (mode)
            {
                case Mode.Steady:
                    k = 1f;
                    break;
                case Mode.Buzz:
                    k = 0.9f + (Mathf.PerlinNoise(t * 14f, 0.3f) - 0.5f) * 0.25f;
                    if (Time.time >= nextEvent)
                    {
                        blackoutUntil = Time.time + Random.Range(0.05f, 0.25f);
                        nextEvent = Time.time + Random.Range(3f, 12f);
                    }
                    break;
                case Mode.Failing:
                    k = Mathf.PerlinNoise(t * 9f, 1.7f) > 0.45f ? 1f : 0.05f;
                    if (Time.time >= nextEvent)
                    {
                        blackoutUntil = Time.time + Random.Range(0.4f, 1.6f);
                        nextEvent = Time.time + Random.Range(2f, 7f);
                    }
                    break;
                case Mode.Dead:
                    k = 0f;
                    break;
                case Mode.Pulse:
                    k = 0.55f + 0.45f * Mathf.Sin(t * 2.2f);
                    break;
            }

            if (Time.time < blackoutUntil) k = 0.02f;

            lightSource.intensity = baseIntensity * k;
            if (fixtureMat != null)
            {
                fixtureMat.SetColor("_EmissionColor", emissionColor * (2f * k));
            }
        }
    }
}
