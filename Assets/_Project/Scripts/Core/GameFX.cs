using UnityEngine;

namespace ZombieGame
{
    /// <summary>Cheap runtime effects: blood bursts, bullet holes, debris, flashes.</summary>
    public static class GameFX
    {
        static int holeCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { holeCount = 0; }

        public static void Blood(Vector3 point, Vector3 dir, int count = 5)
        {
            Material mat = GameMaterials.Solid(new Color(0.35f, 0.02f, 0.02f), 0.6f);
            for (int i = 0; i < count; i++)
            {
                GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "BloodDrop";
                Object.Destroy(c.GetComponent<Collider>());
                c.GetComponent<Renderer>().sharedMaterial = mat;
                c.transform.position = point;
                c.transform.localScale = Vector3.one * Random.Range(0.03f, 0.07f);
                Rigidbody rb = c.AddComponent<Rigidbody>();
                rb.mass = 0.05f;
                rb.linearVelocity = dir.normalized * Random.Range(1.5f, 4f) + Random.insideUnitSphere * 1.8f + Vector3.up;
                Object.Destroy(c, 1.2f);
            }
        }

        public static void BulletHole(Vector3 point, Vector3 normal)
        {
            if (holeCount > 60) return;
            holeCount++;
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "BulletHole";
            Object.Destroy(q.GetComponent<Collider>());
            q.GetComponent<Renderer>().sharedMaterial = GameMaterials.Decal(SurfaceKind.BulletHole);
            q.transform.position = point + normal * 0.012f;
            q.transform.rotation = Quaternion.LookRotation(-normal);
            q.transform.localScale = Vector3.one * 0.09f;
            Object.Destroy(q, 40f);
        }

        public static void Debris(Vector3 point, Color color, int count, float force)
        {
            Material mat = GameMaterials.Solid(color, 0.3f);
            for (int i = 0; i < count; i++)
            {
                GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "Debris";
                c.GetComponent<Renderer>().sharedMaterial = mat;
                c.transform.position = point + Random.insideUnitSphere * 0.15f;
                c.transform.rotation = Random.rotation;
                c.transform.localScale = new Vector3(Random.Range(0.04f, 0.14f), Random.Range(0.03f, 0.06f), Random.Range(0.1f, 0.5f));
                Rigidbody rb = c.AddComponent<Rigidbody>();
                rb.mass = 0.3f;
                rb.AddExplosionForce(force, point, 2f, 0.3f, ForceMode.Impulse);
                Object.Destroy(c, Random.Range(4f, 7f));
            }
        }

        public static void Flash(Vector3 point, Color color, float intensity, float range, float duration)
        {
            GameObject go = new GameObject("FlashLight");
            go.transform.position = point;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            go.AddComponent<FadeLight>().duration = duration;
        }
    }

    public class FadeLight : MonoBehaviour
    {
        public float duration = 0.2f;
        Light l;
        float start;
        float t;

        void Awake() { l = GetComponent<Light>(); }
        void Start() { start = l != null ? l.intensity : 0f; }

        void Update()
        {
            t += Time.deltaTime;
            if (l != null) l.intensity = Mathf.Lerp(start, 0f, t / Mathf.Max(0.01f, duration));
            if (t >= duration) Destroy(gameObject);
        }
    }
}
