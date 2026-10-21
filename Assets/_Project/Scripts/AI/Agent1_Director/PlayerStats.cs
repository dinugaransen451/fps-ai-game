using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// The player's health, ammo and throwables. The Director reads these to adapt the difficulty
    /// (originally a stand-in, now the real thing). Zombies hurt the player through IDamageable.
    /// </summary>
    public class PlayerStats : MonoBehaviour, IDamageable
    {
        public static PlayerStats Instance { get; private set; }

        public float health = 100f;
        public float maxHealth = 100f;

        [Header("Ammo (magazine + reserve)")]
        public int ammo = 12;            // rounds in the magazine
        public int fullAmmo = 12;        // magazine size
        public int reserveAmmo = 36;
        public int maxReserve = 96;

        [Header("Throwables")]
        public int grenades = 2;
        public int bottles = 4;

        public bool IsAlive { get { return health > 0f; } }
        public float HealthFraction { get { return Mathf.Clamp01(health / Mathf.Max(1f, maxHealth)); } }
        public int TotalAmmo { get { return ammo + reserveAmmo; } }

        /// <summary>0..1 - how stocked the player is (used by the Director).</summary>
        public float AmmoFraction
        {
            get { return Mathf.Clamp01((float)TotalAmmo / Mathf.Max(1f, fullAmmo * 4f)); }
        }

        public float LastDamageTime { get; private set; }
        public Vector3 LastDamageFrom { get; private set; }
        public event System.Action<float, Vector3> Damaged;
        public event System.Action Died;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive) return;
            health = Mathf.Max(0f, health - info.amount);
            LastDamageTime = Time.time;
            LastDamageFrom = info.point;
            GameAudio.Play2D(GameAudio.PlayerHurt, 0.8f);
            if (Damaged != null) Damaged(info.amount, info.point);
            if (health <= 0f && Died != null) Died();
        }

        public void Heal(float amount) { health = Mathf.Min(maxHealth, health + amount); }

        public void AddReserve(int rounds) { reserveAmmo = Mathf.Min(maxReserve, reserveAmmo + rounds); }
    }
}
