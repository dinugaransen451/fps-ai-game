using UnityEngine;

namespace ZombieGame
{
    /// <summary>Walk over it to collect. Glows softly so it can be found in the dark.</summary>
    public class Pickup : MonoBehaviour
    {
        public enum PickupType { Ammo, Health, Grenade, Bottles }

        public PickupType type = PickupType.Ammo;
        public int amount = 18;
        public float collectRadius = 1.4f;

        Transform visual;
        Vector3 basePos;

        void Start()
        {
            visual = transform.childCount > 0 ? transform.GetChild(0) : transform;
            basePos = visual.localPosition;
        }

        void Update()
        {
            if (visual != null)
            {
                visual.localPosition = basePos + Vector3.up * (Mathf.Sin(Time.time * 2f + transform.position.x) * 0.05f);
                visual.Rotate(0f, 45f * Time.deltaTime, 0f, Space.World);
            }

            PlayerStats s = PlayerStats.Instance;
            if (s == null || !s.IsAlive) return;
            Vector3 d = s.transform.position - transform.position;
            if (Mathf.Abs(d.y) > 2f) return;
            d.y = 0f;
            if (d.sqrMagnitude > collectRadius * collectRadius) return;

            bool taken = false;
            string msg = "";
            switch (type)
            {
                case PickupType.Ammo:
                    if (s.reserveAmmo >= s.maxReserve) return;
                    s.AddReserve(amount);
                    msg = "+" + amount + " ammo";
                    taken = true;
                    break;
                case PickupType.Health:
                    if (s.health >= s.maxHealth - 0.5f) return;
                    s.Heal(amount);
                    msg = "+" + amount + " health";
                    taken = true;
                    break;
                case PickupType.Grenade:
                    s.grenades += amount;
                    msg = "+" + amount + " grenade";
                    taken = true;
                    break;
                case PickupType.Bottles:
                    s.bottles += amount;
                    msg = "+" + amount + " bottles";
                    taken = true;
                    break;
            }

            if (taken)
            {
                GameAudio.Play2D(GameAudio.Pickup, 0.8f);
                if (GameHUD.Instance != null) GameHUD.Instance.Toast(msg, 1.6f);
                Destroy(gameObject);
            }
        }

        public static Pickup Create(Transform parent, PickupType t, Vector3 pos, int amount)
        {
            GameObject root = new GameObject("Pickup_" + t);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            Color col;
            Vector3 size;
            switch (t)
            {
                case PickupType.Health: col = new Color(0.9f, 0.9f, 0.9f); size = new Vector3(0.38f, 0.22f, 0.28f); break;
                case PickupType.Grenade: col = new Color(0.2f, 0.32f, 0.15f); size = new Vector3(0.3f, 0.3f, 0.3f); break;
                case PickupType.Bottles: col = new Color(0.25f, 0.6f, 0.3f); size = new Vector3(0.28f, 0.28f, 0.28f); break;
                default: col = new Color(0.25f, 0.4f, 0.2f); size = new Vector3(0.4f, 0.22f, 0.26f); break;
            }

            GameObject vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vis.name = "Visual";
            Collider c = vis.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);
            vis.transform.SetParent(root.transform, false);
            vis.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            vis.transform.localScale = size;
            vis.GetComponent<Renderer>().sharedMaterial = GameMaterials.Solid(col, 0.3f);

            if (t == PickupType.Health)
            {
                GameObject cross = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cross.name = "Cross";
                Collider cc = cross.GetComponent<Collider>();
                if (cc != null) DestroyImmediate(cc);
                cross.transform.SetParent(vis.transform, false);
                cross.transform.localPosition = new Vector3(0f, 0.52f, 0f);
                cross.transform.localScale = new Vector3(0.5f, 0.1f, 0.2f);
                cross.GetComponent<Renderer>().sharedMaterial = GameMaterials.Solid(new Color(0.8f, 0.05f, 0.05f), 0.3f);
            }

            GameObject lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            Light l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 3.2f;
            l.intensity = 1.1f;
            l.shadows = LightShadows.None;
            l.color = t == PickupType.Health ? new Color(1f, 0.3f, 0.3f) : (t == PickupType.Ammo ? new Color(1f, 0.85f, 0.3f) : new Color(0.4f, 1f, 0.5f));

            Pickup p = root.AddComponent<Pickup>();
            p.type = t;
            p.amount = amount;
            return p;
        }
    }
}
