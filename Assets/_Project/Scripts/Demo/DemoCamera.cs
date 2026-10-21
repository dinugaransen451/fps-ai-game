using UnityEngine;
using UnityEngine.Rendering.Universal;
using ScreamerAI;

namespace ZombieGame
{
    /// <summary>
    /// DEMO CAMERA: the multi-shot "director" used while the DemoBot plays.
    /// It creates a second camera (DemoCam), switches off only the Camera component of the
    /// player's camera (its AudioListener stays, so there is exactly one listener), and cuts
    /// between shot types every few seconds:
    ///   FirstPerson (~25%), Chase, OverShoulder, Zombie cam (6-8 m, line of sight checked)
    ///   and a ceiling Overview. SmoothDamp / Slerp inside a shot, a hard cut on shot changes,
    ///   and a raycast pull-in so the camera never ends up inside a wall.
    /// The IMGUI HUD draws on top of whichever camera renders, so it stays visible throughout.
    /// </summary>
    public class DemoCamera : MonoBehaviour
    {
        public enum ShotKind { FirstPerson, Chase, OverShoulder, Zombie, Overview }

        [Header("Shot rhythm")]
        public float minShotSeconds = 4f;      // no shot is shorter than this
        public float maxShotSeconds = 9f;
        public float firstPersonShare = 0.25f; // ~25% of the shots are first person

        Camera cam;
        Camera playerCam;
        Transform player;
        GameHUD hud;

        ShotKind kind = ShotKind.Chase;
        bool snap = true;                      // next frame: hard cut instead of smoothing
        float shotEnd;
        float forceUntil = -1f;
        ShotKind forcedKind;

        Vector3 smoothVel;
        float wantedFov = 64f;

        ZombieHealth hero;                     // the zombie currently featured
        float heroValidUntil;

        static readonly Vector3[] Corners = new Vector3[]
        {
            new Vector3(2.6f, 0f, 2.6f),
            new Vector3(37.4f, 0f, 2.6f),
            new Vector3(2.6f, 0f, 27.4f),
            new Vector3(37.4f, 0f, 27.4f),
        };

        // ------------------------------------------------------------------ setup
        void Start()
        {
            PlayerController pc = PlayerController.Instance;
            if (pc == null)
            {
                Debug.LogWarning("[DemoCamera] no player - camera director disabled.");
                enabled = false;
                return;
            }
            player = pc.transform;
            playerCam = pc.cam;

            GameObject go = new GameObject("DemoCam");
            go.transform.SetParent(transform, false);
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.01f, 0.012f);
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 90f;
            cam.fieldOfView = 64f;
            cam.depth = 10f;                   // draw after everything else

            // look like normal play: copy the player camera's clear flags/background and its
            // URP setup (post-processing, antialiasing, volume blending, render type)
            CopyRenderSettings(playerCam, cam);

            // only the rendering is taken away from the player camera - the AudioListener stays put
            if (playerCam != null) playerCam.enabled = false;

            hud = GameHUD.Instance;
            if (hud != null) hud.cam = cam;    // HUD debug labels now project from the shot camera

            AlertSystem.AlertBroadcast += OnAlertBroadcast;
            PickNextShot(true);
        }

        void OnDestroy()
        {
            AlertSystem.AlertBroadcast -= OnAlertBroadcast;
            if (playerCam != null) playerCam.enabled = true;
            if (hud != null && hud.cam == cam) hud.cam = playerCam;
            if (cam != null) Destroy(cam.gameObject);
        }

        // ------------------------------------------------------------------ rendering setup
        /// <summary>Copy the parts of the player camera that decide how the frame looks
        /// (clear flags, background and the URP additional camera data) onto DemoCam.</summary>
        static void CopyRenderSettings(Camera from, Camera to)
        {
            if (from == null || to == null) return;

            to.clearFlags = from.clearFlags;
            to.backgroundColor = from.backgroundColor;

            if (!from.TryGetComponent(out UniversalAdditionalCameraData fromData))
                return;                              // not a URP camera - keep the defaults

            UniversalAdditionalCameraData toData = to.GetComponent<UniversalAdditionalCameraData>();
            if (toData == null) toData = to.gameObject.AddComponent<UniversalAdditionalCameraData>();

            toData.renderPostProcessing = fromData.renderPostProcessing;
            toData.antialiasing = fromData.antialiasing;
            toData.antialiasingQuality = fromData.antialiasingQuality;
            toData.volumeLayerMask = fromData.volumeLayerMask;
            toData.volumeTrigger = fromData.volumeTrigger;
            toData.renderType = fromData.renderType;
        }

        // ------------------------------------------------------------------ triggers
        /// <summary>Force a specific shot type for 'seconds' (used by the storyboard, e.g. the opening shot).</summary>
        public void ForceShot(ShotKind k, float seconds)
        {
            forcedKind = k;
            forceUntil = Time.time + Mathf.Max(0.1f, seconds);
            CutTo(k);
        }

        /// <summary>A scripted zombie just appeared - feature it in the zombie cam.</summary>
        public void OnZombieSpawned(ZombieHealth z)
        {
            if (z == null) return;
            hero = z;
            heroValidUntil = Time.time + 9f;
            if (Time.time >= forceUntil) CutTo(ShotKind.Zombie);
        }

        void OnAlertBroadcast(AlertEvent e)
        {
            // the Screamer screamed: cut to it (or to where the noise came from)
            if (e.Source != null)
            {
                ZombieHealth z = e.Source.GetComponent<ZombieHealth>();
                if (z != null && z.IsAlive)
                {
                    hero = z;
                    heroValidUntil = Time.time + 9f;
                    if (Time.time >= forceUntil) CutTo(ShotKind.Zombie);
                }
            }
        }

        /// <summary>Hard cut to 'k' and hold it for at least a few seconds.</summary>
        void CutTo(ShotKind k)
        {
            if (kind != k) { kind = k; snap = true; }
            ApplyShotKind();
            shotEnd = Time.time + 5f;
        }

        void ApplyShotKind()
        {
            switch (kind)
            {
                case ShotKind.FirstPerson: wantedFov = 72f; break;
                case ShotKind.Chase: wantedFov = 64f; break;
                case ShotKind.OverShoulder: wantedFov = 68f; break;
                case ShotKind.Zombie: wantedFov = 60f; break;
                default: wantedFov = 80f; break;      // Overview: wide security-cam look
            }
        }

        // ------------------------------------------------------------------ shot selection
        void PickNextShot(bool first)
        {
            ShotKind previous = kind;

            if (Time.time < forceUntil)
            {
                kind = forcedKind;
            }
            else
            {
                bool haveZombie = ValidHero();
                float r = Random.value * 100f;
                if (r < firstPersonShare * 100f) kind = ShotKind.FirstPerson;
                else if (r < firstPersonShare * 100f + 30f) kind = ShotKind.Chase;
                else if (r < firstPersonShare * 100f + 50f) kind = ShotKind.OverShoulder;
                else if (r < firstPersonShare * 100f + 65f && haveZombie) kind = ShotKind.Zombie;
                else kind = ShotKind.Overview;

                // fall back when the zombie shot is unavailable but the roll asked for it
                if (kind == ShotKind.Zombie && !haveZombie) kind = ShotKind.Chase;
            }

            if (kind != previous || first) snap = true;
            ApplyShotKind();

            float len = Random.Range(minShotSeconds, maxShotSeconds);
            if (kind == ShotKind.FirstPerson) len = Mathf.Min(len, 6.5f);   // FP is disorienting for long
            if (kind == ShotKind.Overview) len = Mathf.Min(len, 7f);
            if (kind == ShotKind.Zombie && hero != null) heroValidUntil = Time.time + len + 1f;  // keep it for the shot
            shotEnd = Time.time + len;
        }

        bool ValidHero()
        {
            if (hero == null || !hero.IsAlive) return false;
            if (player == null) return false;
            if (Vector3.Distance(hero.transform.position, player.position) > 32f) return false;
            if (Time.time > heroValidUntil)
            {
                // keep it only while it is still the most interesting zombie on screen
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ per-frame
        void LateUpdate()
        {
            if (cam == null || player == null || playerCam == null) return;

            if (Time.time >= shotEnd && Time.time >= forceUntil) PickNextShot(false);

            Vector3 desired;
            Vector3 lookAt;
            Vector3 head = player.position + Vector3.up * 1.55f;
            Vector3 fwd = player.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);   // player's right

            switch (kind)
            {
                case ShotKind.FirstPerson:
                    cam.transform.SetPositionAndRotation(playerCam.transform.position, playerCam.transform.rotation);
                    cam.fieldOfView = 72f;
                    snap = false;
                    return;

                case ShotKind.OverShoulder:
                    desired = head - fwd * 1.15f + right * 0.55f;
                    desired = PullIn(head, desired);
                    lookAt = player.position + Vector3.up * 1.45f + fwd * 9f;
                    break;

                case ShotKind.Zombie:
                    if (hero == null || !hero.IsAlive || Time.time > heroValidUntil)
                    {
                        // the featured zombie is gone (or its window closed) - cut away
                        kind = ShotKind.Chase;
                        ApplyShotKind();
                    }
                    if (kind == ShotKind.Zombie)
                    {
                        Vector3 chest = hero.transform.position + Vector3.up * 1.3f;
                        Vector3 toPlayer = player.position - hero.transform.position;
                        toPlayer.y = 0f;
                        toPlayer = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : Vector3.back;
                        Vector3 side = new Vector3(toPlayer.z, 0f, -toPlayer.x);
                        float dist = 7f;                                  // 6-8 m as specced
                        desired = chest - toPlayer * dist + side * 1.4f + Vector3.up * 0.3f;
                        desired = PullIn(chest, desired);                 // keep the LOS open
                        lookAt = chest;
                        break;
                    }
                    goto default;      // fell back to the chase shot

                case ShotKind.Overview:
                    desired = OverviewAnchor();
                    lookAt = player.position + Vector3.up * 1.2f;
                    break;

                default:    // Chase
                    desired = head - fwd * 3.0f + right * 0.6f;
                    desired = PullIn(head, desired);
                    lookAt = head + fwd * 4f;
                    break;
            }

            if (snap)
            {
                cam.transform.position = desired;
                smoothVel = Vector3.zero;
                snap = false;
            }
            else
            {
                float smooth = kind == ShotKind.Zombie ? 0.3f : 0.45f;
                cam.transform.position = Vector3.SmoothDamp(cam.transform.position, desired, ref smoothVel, smooth);
            }

            Vector3 flat = lookAt - cam.transform.position;
            if (flat.sqrMagnitude > 0.0004f)
            {
                Quaternion want = Quaternion.LookRotation(flat.normalized, Vector3.up);
                cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, want, 1f - Mathf.Exp(-7f * Time.unscaledDeltaTime));
            }
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, wantedFov, 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime));
        }

        /// <summary>Nearest ceiling corner with a clear line to the player (falls back to the closest corner).</summary>
        Vector3 OverviewAnchor()
        {
            float floorY = player.position.y > 2f ? LevelBuilder.FloorH : 0f;
            int best = 0;
            float bestScore = float.MaxValue;
            for (int i = 0; i < Corners.Length; i++)
            {
                Vector3 c = Corners[i];
                c.y = floorY + 3.25f;               // just under the 3.7 m ceiling
                bool clear = CanSeePlayer(c);
                float score = Vector3.Distance(c, player.position) + (clear ? 0f : 500f);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            Vector3 pick = Corners[best];
            pick.y = floorY + 3.25f;
            return pick;
        }

        bool CanSeePlayer(Vector3 from)
        {
            Vector3 to = player.position + Vector3.up * 1.2f;
            RaycastHit hit;
            if (!Physics.Linecast(from, to, out hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.transform == player || hit.transform.IsChildOf(player);
        }

        /// <summary>Keep the shot camera out of geometry: pull it in front of whatever is in the way.</summary>
        Vector3 PullIn(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float d = dir.magnitude;
            if (d < 0.05f) return to;

            RaycastHit hit;
            if (Physics.Linecast(from, to, out hit, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == player || (player != null && hit.transform.IsChildOf(player))) return to;
                float keep = Mathf.Max(1.0f, hit.distance - 0.35f);
                return from + dir.normalized * keep;
            }
            return to;
        }
    }
}
