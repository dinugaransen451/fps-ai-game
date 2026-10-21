using UnityEngine;

namespace ScreamerAI
{
    /// <summary>Expanding ring that shows how far a scream travels (also handy for debugging in the Game view).</summary>
    public class ScreamShockwave : MonoBehaviour
    {
        const int Segments = 64;

        float maxRadius = 30f;
        float lifetime = 1.4f;
        float age;
        LineRenderer line;
        Material material;

        public static void Spawn(Vector3 position, float maxRadius)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return;

            GameObject go = new GameObject("ScreamShockwave");
            go.transform.position = position;
            ScreamShockwave s = go.AddComponent<ScreamShockwave>();
            s.maxRadius = maxRadius;

            s.material = new Material(shader);
            s.line = go.AddComponent<LineRenderer>();
            s.line.useWorldSpace = false;
            s.line.loop = true;
            s.line.positionCount = Segments;
            s.line.widthMultiplier = 0.25f;
            s.line.sharedMaterial = s.material;
            s.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.line.receiveShadows = false;

            for (int i = 0; i < Segments; i++)
            {
                float a = (float)i / Segments * Mathf.PI * 2f;
                s.line.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / lifetime);
            float eased = 1f - (1f - t) * (1f - t);
            float r = Mathf.Lerp(0.5f, maxRadius, eased);
            transform.localScale = new Vector3(r, 1f, r);

            Color c = new Color(1f, 0.15f, 0.1f, 1f - t);
            line.startColor = c;
            line.endColor = c;
            line.widthMultiplier = Mathf.Lerp(0.3f, 0.08f * Mathf.Max(1f, r * 0.15f), t);

            if (t >= 1f) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
