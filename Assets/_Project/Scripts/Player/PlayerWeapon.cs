using UnityEngine;
using UnityEngine.InputSystem;

namespace ZombieGame
{
    /// <summary>Hitscan pistol: LMB fire, R reload. Gunshots are loud - they pull Tanks and Runners toward you.</summary>
    public class PlayerWeapon : MonoBehaviour
    {
        public Camera cam;
        public float damage = 34f;
        public float headshotMultiplier = 2f;
        public float fireInterval = 0.22f;
        public float range = 80f;
        public float reloadTime = 1.5f;
        public float spreadDegrees = 0.6f;
        public float gunshotNoiseRadius = 45f;

        public bool Reloading { get; private set; }
        public float ReloadProgress { get { return Reloading ? Mathf.Clamp01(1f - (reloadEnd - Time.time) / reloadTime) : 1f; } }

        /// <summary>True while the DemoBot drives the gun: keyboard (R) and mouse (LMB) are ignored,
        /// but a reload that is already running still finishes normally.</summary>
        public bool externalInput;

        PlayerStats stats;
        PlayerController pc;
        Transform gun;
        Light muzzle;
        Vector3 gunRest;
        float nextFire;
        float reloadEnd;
        float muzzleOff;
        float kick;

        void Awake()
        {
            stats = GetComponent<PlayerStats>();
            pc = GetComponent<PlayerController>();
        }

        public void BuildViewModel(Transform camTransform)
        {
            GameObject root = new GameObject("PistolViewModel");
            root.transform.SetParent(camTransform, false);
            root.transform.localPosition = new Vector3(0.2f, -0.2f, 0.42f);
            gun = root.transform;
            gunRest = gun.localPosition;

            Material metal = GameMaterials.Solid(new Color(0.12f, 0.12f, 0.13f), 0.55f, 0.7f);
            Material grip = GameMaterials.Solid(new Color(0.08f, 0.07f, 0.06f), 0.2f, 0f);
            AddPart(root.transform, "Slide", new Vector3(0f, 0.04f, 0.08f), new Vector3(0.05f, 0.05f, 0.30f), metal);
            AddPart(root.transform, "Frame", new Vector3(0f, 0.0f, 0.06f), new Vector3(0.045f, 0.045f, 0.24f), metal);
            GameObject g = AddPart(root.transform, "Grip", new Vector3(0f, -0.07f, -0.02f), new Vector3(0.045f, 0.13f, 0.06f), grip);
            g.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);

            GameObject m = new GameObject("MuzzleFlash");
            m.transform.SetParent(root.transform, false);
            m.transform.localPosition = new Vector3(0f, 0.04f, 0.28f);
            muzzle = m.AddComponent<Light>();
            muzzle.type = LightType.Point;
            muzzle.color = new Color(1f, 0.75f, 0.35f);
            muzzle.range = 9f;
            muzzle.intensity = 0f;
            muzzle.shadows = LightShadows.None;
        }

        static GameObject AddPart(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = name;
            Collider c = p.GetComponent<Collider>();
            if (c != null) Destroy(c);
            p.transform.SetParent(parent, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            p.GetComponent<Renderer>().sharedMaterial = mat;
            return p;
        }

        void Update()
        {
            AnimateGun();
            if (muzzle != null && Time.time > muzzleOff) muzzle.intensity = 0f;

            if (pc == null || !pc.CanAct) return;
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (Reloading)
            {
                if (Time.time >= reloadEnd) FinishReload();
                return;
            }

            if (externalInput) return;   // the DemoBot fires / reloads through the external API below

            if (kb.rKey.wasPressedThisFrame) { StartReload(); return; }

            if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked && Time.time >= nextFire)
            {
                if (stats.ammo <= 0)
                {
                    GameAudio.Play2D(GameAudio.DryFire, 0.7f);
                    nextFire = Time.time + 0.3f;
                    if (stats.reserveAmmo > 0) StartReload();
                    return;
                }
                Fire();
            }
        }

        void StartReload()
        {
            if (Reloading || stats.ammo >= stats.fullAmmo || stats.reserveAmmo <= 0) return;
            Reloading = true;
            reloadEnd = Time.time + reloadTime;
            GameAudio.Play2D(GameAudio.Reload, 0.8f);
        }

        void FinishReload()
        {
            int need = stats.fullAmmo - stats.ammo;
            int take = Mathf.Min(need, stats.reserveAmmo);
            stats.ammo += take;
            stats.reserveAmmo -= take;
            Reloading = false;
        }

        // ------------------------------------------------------------------ external API (DemoBot)
        /// <summary>Fire one shot from script (same rules as LMB: cooldown, ammo, auto-reload on empty).</summary>
        public void FireExternal()
        {
            if (pc == null || !pc.CanAct || Reloading || Time.time < nextFire) return;
            if (stats.ammo <= 0)
            {
                GameAudio.Play2D(GameAudio.DryFire, 0.7f);
                nextFire = Time.time + 0.3f;
                if (stats.reserveAmmo > 0) StartReload();
                return;
            }
            Fire();
        }

        /// <summary>Start a reload from script (same rules as R: only when the magazine is not full).</summary>
        public void ReloadExternal()
        {
            if (pc == null || !pc.CanAct) return;
            StartReload();
        }

        void Fire()
        {
            nextFire = Time.time + fireInterval;
            stats.ammo--;
            kick = 1f;
            muzzle.intensity = 6f;
            muzzleOff = Time.time + 0.05f;
            pc.AddRecoil(1.4f);

            GameAudio.Play2D(GameAudio.Gunshot, 0.9f);
            NoiseSystem.Emit(transform.position, gunshotNoiseRadius, NoiseKind.Gunshot, gameObject);

            float spread = spreadDegrees + (pc.IsMoving ? 1.2f : 0f);
            Vector3 dir = cam.transform.forward;
            dir = Quaternion.AngleAxis(Random.Range(-spread, spread), cam.transform.up) * dir;
            dir = Quaternion.AngleAxis(Random.Range(-spread, spread), cam.transform.right) * dir;

            RaycastHit[] hits = Physics.RaycastAll(cam.transform.position, dir, range, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit h = hits[i];
                if (h.collider.transform.IsChildOf(transform)) continue;

                IDamageable target = h.collider.GetComponentInParent<IDamageable>();
                if (target != null)
                {
                    if (!target.IsAlive) continue;   // shoot through corpses
                    float top = h.collider.bounds.max.y;
                    float bottom = h.collider.bounds.min.y;
                    bool head = h.point.y > bottom + (top - bottom) * 0.8f;
                    float dmg = damage * (head ? headshotMultiplier : 1f);
                    target.TakeDamage(new DamageInfo(dmg, h.point, dir, head, gameObject));
                    if (GameHUD.Instance != null) GameHUD.Instance.ShowHitMarker(head);
                    GameAudio.Play2D(GameAudio.HitMarker, 0.7f);
                }
                else
                {
                    GameFX.BulletHole(h.point, h.normal);
                    GameFX.Debris(h.point + h.normal * 0.02f, new Color(0.4f, 0.4f, 0.38f), 2, 0.6f);
                }
                break;
            }
        }

        void AnimateGun()
        {
            if (gun == null) return;
            kick = Mathf.MoveTowards(kick, 0f, Time.deltaTime * 9f);
            Vector3 offset = new Vector3(0f, kick * 0.012f, -kick * 0.07f);
            Quaternion rot = Quaternion.Euler(-kick * 7f, 0f, 0f);

            if (Reloading)
            {
                float p = ReloadProgress;
                float dip = Mathf.Sin(p * Mathf.PI);
                offset += new Vector3(0f, -0.16f * dip, 0f);
                rot *= Quaternion.Euler(30f * dip, 0f, -20f * dip);
            }

            float sway = pc != null ? Mathf.Clamp01(pc.Velocity.magnitude / 4f) : 0f;
            offset += new Vector3(Mathf.Sin(Time.time * 4f) * 0.004f * sway, Mathf.Sin(Time.time * 8f) * 0.004f * sway, 0f);

            gun.localPosition = Vector3.Lerp(gun.localPosition, gunRest + offset, Time.deltaTime * 18f);
            gun.localRotation = Quaternion.Slerp(gun.localRotation, rot, Time.deltaTime * 18f);
        }
    }
}
