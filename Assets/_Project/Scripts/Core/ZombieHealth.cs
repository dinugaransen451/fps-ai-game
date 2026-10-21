using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    /// <summary>Hit points + death for every zombie type (Runner, Tank, Screamer).</summary>
    public class ZombieHealth : MonoBehaviour, IDamageable
    {
        public static readonly List<ZombieHealth> Alive = new List<ZombieHealth>();
        public static event System.Action<ZombieHealth> AnyDied;

        public ZombieKind kind = ZombieKind.Runner;
        public float maxHealth = 70f;
        public float health = -1f;

        public bool IsAlive { get { return !dead; } }
        public float HealthFraction { get { return Mathf.Clamp01(health / Mathf.Max(1f, maxHealth)); } }
        public event System.Action<DamageInfo> Damaged;

        bool dead;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Alive.Clear(); AnyDied = null; }

        void Awake()
        {
            if (health < 0f) health = maxHealth;
        }

        void OnEnable() { if (!Alive.Contains(this)) Alive.Add(this); }
        void OnDisable() { Alive.Remove(this); }

        public static int CountAlive(ZombieKind k)
        {
            int n = 0;
            for (int i = 0; i < Alive.Count; i++)
                if (Alive[i] != null && Alive[i].kind == k && !Alive[i].dead) n++;
            return n;
        }

        public void TakeDamage(DamageInfo info)
        {
            if (dead) return;
            health -= info.amount;
            GameFX.Blood(info.point, info.direction, info.headshot ? 8 : 4);
            if (Damaged != null) Damaged(info);
            if (health <= 0f) Die(info);
        }

        void Die(DamageInfo info)
        {
            dead = true;
            Alive.Remove(this);

            MonoBehaviour[] all = GetComponentsInChildren<MonoBehaviour>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != this) all[i].enabled = false;

            NavMeshAgent agent = GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            Collider[] cols = GetComponentsInChildren<Collider>();
            for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;

            GameAudio.PlayAt(GameAudio.Impact, transform.position, 0.8f);
            if (AnyDied != null) AnyDied(this);
            if (GameManager.Instance != null) GameManager.Instance.RegisterKill(kind, info.headshot);

            StartCoroutine(FallRoutine());
        }

        IEnumerator FallRoutine()
        {
            Quaternion from = transform.rotation;
            Quaternion to = from * Quaternion.Euler(-88f, 0f, 0f);
            float t = 0f;
            while (t < 0.7f)
            {
                t += Time.deltaTime;
                transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / 0.7f));
                yield return null;
            }
            yield return new WaitForSeconds(6f);
            float sink = 0f;
            while (sink < 2f)
            {
                sink += Time.deltaTime;
                transform.position += Vector3.down * 0.5f * Time.deltaTime;
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
