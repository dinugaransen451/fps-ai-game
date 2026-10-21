using UnityEngine;
using UnityEngine.InputSystem;

namespace ZombieGame
{
    /// <summary>E = open/close doors, Q = throw a bottle (distraction), G = throw a grenade.</summary>
    public class PlayerInteraction : MonoBehaviour
    {
        public Camera cam;
        public float useRange = 2.8f;
        public float throwSpeed = 13f;

        public string Prompt { get; private set; }

        PlayerStats stats;
        PlayerController pc;
        float nextThrow;

        void Awake()
        {
            stats = GetComponent<PlayerStats>();
            pc = GetComponent<PlayerController>();
            Prompt = "";
        }

        void Update()
        {
            Prompt = "";
            if (pc == null || !pc.CanAct || cam == null) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            RaycastHit hit;
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, useRange, ~0, QueryTriggerInteraction.Ignore))
            {
                Door door = hit.collider.GetComponentInParent<Door>();
                if (door != null)
                {
                    Prompt = door.Prompt;
                    if (kb.eKey.wasPressedThisFrame) door.Toggle(transform.position);
                }
                else if (hit.collider.GetComponentInParent<Barricade>() != null)
                {
                    Prompt = "Walk into the barricade to push it";
                }
            }

            if (Time.time >= nextThrow)
            {
                if (kb.qKey.wasPressedThisFrame && stats.bottles > 0)
                {
                    stats.bottles--;
                    Throw(Throwable.ThrowType.Bottle);
                }
                else if (kb.gKey.wasPressedThisFrame && stats.grenades > 0)
                {
                    stats.grenades--;
                    Throw(Throwable.ThrowType.Grenade);
                }
            }
        }

        void Throw(Throwable.ThrowType type)
        {
            nextThrow = Time.time + 0.7f;
            Vector3 origin = cam.transform.position + cam.transform.forward * 0.5f;
            Vector3 vel = cam.transform.forward * throwSpeed + Vector3.up * 2f + pc.Velocity * 0.5f;
            Throwable.Spawn(type, origin, vel, gameObject);
            GameAudio.Play2D(GameAudio.Swing, 0.6f);
        }

        // ------------------------------------------------------------------ external API (DemoBot)
        /// <summary>Open / close a door from script (no aim or distance check - the caller is responsible).</summary>
        public void UseDoorExternal(Door door)
        {
            if (door == null) return;
            door.Toggle(transform.position);
        }

        /// <summary>Throw a bottle or grenade from script (respects the inventory count and the throw cooldown).</summary>
        public void ThrowExternal(Throwable.ThrowType type)
        {
            if (stats == null || Time.time < nextThrow) return;
            if (type == Throwable.ThrowType.Bottle)
            {
                if (stats.bottles <= 0) return;
                stats.bottles--;
            }
            else
            {
                if (stats.grenades <= 0) return;
                stats.grenades--;
            }
            Throw(type);
        }
    }
}
