using System.Collections.Generic;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// Throwable objects (Member 2): a bottle that shatters loudly (distraction) and a grenade that explodes
    /// after a fuse (area damage + big noise + breaks barricades/doors).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Throwable : MonoBehaviour
    {
        public enum ThrowType { Bottle, Grenade }

        public ThrowType type = ThrowType.Bottle;
        public float fuse = 2.4f;
        public float explosionRadius = 6.5f;
        public float explosionDamage = 150f;
        public GameObject thrower;

        float born;
        bool done;

        void Start() { born = Time.time; }

        void Update()
        {
            if (done || type != ThrowType.Grenade) return;
            if (Time.time - born >= fuse) Explode();
        }

        void OnCollisionEnter(Collision c)
        {
            if (done) return;
            float speed = c.relativeVelocity.magnitude;
            if (type == ThrowType.Bottle)
            {
                if (speed > 2.2f) Shatter(c.GetContact(0).point);
                else NoiseSystem.Emit(transform.position, 6f, NoiseKind.Impact, thrower);
            }
            else if (speed > 1.5f)
            {
                GameAudio.PlayAt(GameAudio.Impact, transform.position, 0.5f);
                NoiseSystem.Emit(transform.position, 10f, NoiseKind.Impact, thrower);
            }
        }

        void Shatter(Vector3 point)
        {
            done = true;
            GameAudio.PlayAt(GameAudio.Glass, point, 1f);
            NoiseSystem.Emit(point, 30f, NoiseKind.Glass, thrower);
            GameFX.Debris(point, new Color(0.3f, 0.6f, 0.35f), 7, 2.5f);
            Destroy(gameObject);
        }

        void Explode()
        {
            done = true;
            Vector3 p = transform.position;
            GameAudio.PlayAt(GameAudio.Explosion, p, 1f);
            NoiseSystem.Emit(p, 50f, NoiseKind.Explosion, thrower);
            GameFX.Flash(p + Vector3.up * 0.3f, new Color(1f, 0.6f, 0.2f), 40f, 14f, 0.35f);
            GameFX.Debris(p, new Color(0.2f, 0.2f, 0.2f), 10, 7f);

            Collider[] cols = Physics.OverlapSphere(p, explosionRadius, ~0, QueryTriggerInteraction.Ignore);
            HashSet<IDamageable> hitDamageable = new HashSet<IDamageable>();
            HashSet<IBlocker> hitBlockers = new HashSet<IBlocker>();
            for (int i = 0; i < cols.Length; i++)
            {
                Collider col = cols[i];
                if (col == null) continue;
                Vector3 closest = col.ClosestPoint(p);
                float falloff = 1f - Mathf.Clamp01(Vector3.Distance(p, closest) / explosionRadius);

                IDamageable dmg = col.GetComponentInParent<IDamageable>();
                if (dmg != null && dmg.IsAlive && hitDamageable.Add(dmg))
                {
                    bool isPlayer = col.GetComponentInParent<PlayerStats>() != null;
                    float amount = explosionDamage * falloff * (isPlayer ? 0.3f : 1f);
                    Vector3 dir = (closest - p).normalized;
                    dmg.TakeDamage(new DamageInfo(amount, closest, dir, false, thrower));
                    if (isPlayer && PlayerController.Instance != null) PlayerController.Instance.AddShake(0.6f);
                }

                IBlocker blocker = col.GetComponentInParent<IBlocker>();
                if (blocker != null && hitBlockers.Add(blocker))
                    blocker.TakeBlockDamage(explosionDamage * falloff * 0.9f, thrower);

                Rigidbody rb = col.attachedRigidbody;
                if (rb != null && !rb.isKinematic) rb.AddExplosionForce(900f * falloff, p, explosionRadius, 0.5f, ForceMode.Impulse);
            }

            if (PlayerController.Instance != null)
            {
                float d = Vector3.Distance(p, PlayerController.Instance.transform.position);
                if (d < 25f) PlayerController.Instance.AddShake(Mathf.Clamp01(1f - d / 25f));
            }
            Destroy(gameObject);
        }

        public static Throwable Spawn(ThrowType t, Vector3 position, Vector3 velocity, GameObject thrower)
        {
            GameObject go;
            if (t == ThrowType.Bottle)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "Bottle";
                go.transform.localScale = new Vector3(0.07f, 0.13f, 0.07f);
                go.GetComponent<Renderer>().sharedMaterial = GameMaterials.Solid(new Color(0.2f, 0.5f, 0.25f), 0.9f);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Grenade";
                go.transform.localScale = Vector3.one * 0.13f;
                go.GetComponent<Renderer>().sharedMaterial = GameMaterials.Solid(new Color(0.12f, 0.2f, 0.1f), 0.4f, 0.3f);
            }
            go.transform.position = position;
            go.transform.rotation = Random.rotation;

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = t == ThrowType.Bottle ? 0.4f : 0.6f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.insideUnitSphere * 8f;

            Throwable th = go.AddComponent<Throwable>();
            th.type = t;
            th.thrower = thrower;
            return th;
        }
    }
}
