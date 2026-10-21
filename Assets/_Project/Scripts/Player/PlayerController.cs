using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZombieGame
{
    /// <summary>
    /// First-person controller (new Input System): WASD, Shift sprint (stamina), Space jump, mouse look,
    /// F flashlight. Emits footstep noise for the zombies, pushes rigid bodies (barricades, crates).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        public Camera cam;
        public Light flashlight;
        public float walkSpeed = 3.6f;
        public float sprintSpeed = 6.2f;
        public float acceleration = 22f;
        public float mouseSensitivity = 0.09f;
        public float jumpSpeed = 5f;
        public float gravity = -20f;

        [Header("Stamina")]
        public float maxStamina = 100f;
        public float sprintDrain = 24f;
        public float staminaRegen = 15f;

        public float Stamina { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsMoving { get; private set; }
        public bool Dead { get; private set; }
        public Vector3 Velocity { get { return horizontal; } }

        [Header("External control (DemoBot / cut-scenes)")]
        /// <summary>When true the keyboard and mouse are ignored and the bot drives via ExternalMove / ExternalLook.
        /// Gravity, stamina, footsteps and camera bob keep running exactly as before.</summary>
        public bool externalControl;
        /// <summary>Maximum turn rate (deg/s) applied to external look requests.</summary>
        public float externalTurnRate = 320f;

        /// <summary>How easily zombies can see the player (flashlight on / sprinting = easier).</summary>
        public float SightMultiplier
        {
            get
            {
                float m = (flashlight != null && flashlight.enabled) ? 1f : 0.6f;
                if (IsSprinting) m *= 1.25f;
                return m;
            }
        }

        public bool CanAct
        {
            get { return !Dead && GameManager.Instance != null && GameManager.Instance.state == GameManager.State.Playing; }
        }

        CharacterController cc;
        float yaw;
        float pitch;
        float vy;
        Vector3 horizontal;
        float stepDistance;
        float bobTime;
        float shake;
        float recoilPitch;
        bool staminaLocked;
        Vector3 camBase = new Vector3(0f, 1.62f, 0f);

        // external-control state (see externalControl)
        Vector3 externalIntent;       // desired world-space velocity (y ignored)
        float lookYawRate;            // requested yaw rate (deg/s, + = turn right)
        float lookPitchRate;          // requested pitch rate (deg/s, + = look up)
        float appliedYawRate;         // smoothed rates actually applied
        float appliedPitchRate;

        void Awake()
        {
            Instance = this;
            cc = GetComponent<CharacterController>();
            Stamina = maxStamina;
            yaw = transform.eulerAngles.y;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start() { SetCursorLocked(true); }

        public void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void AddShake(float amount) { shake = Mathf.Clamp(shake + amount, 0f, 1.5f); }
        public void AddRecoil(float degrees) { recoilPitch += degrees; }

        public void Die()
        {
            if (Dead) return;
            Dead = true;
            if (flashlight != null) flashlight.enabled = false;
            StartCoroutine(DeathCam());
        }

        IEnumerator DeathCam()
        {
            float t = 0f;
            Vector3 from = cam.transform.localPosition;
            Quaternion fromRot = cam.transform.localRotation;
            Quaternion toRot = Quaternion.Euler(10f, 0f, 75f);
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * 1.2f;
                cam.transform.localPosition = Vector3.Lerp(from, new Vector3(0f, 0.35f, 0f), t);
                cam.transform.localRotation = Quaternion.Slerp(fromRot, toRot, t);
                yield return null;
            }
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (Dead || !CanAct)
            {
                IsSprinting = false;
                ApplyGravityOnly();
                return;
            }

            // the DemoBot (or any IPlayerDriver) owns the player: no keyboard / mouse, but gravity,
            // stamina, footsteps and camera bob all behave exactly as in normal play.
            if (externalControl)
            {
                UpdateExternal();
                return;
            }

            if (kb == null)
            {
                IsSprinting = false;
                ApplyGravityOnly();
                return;
            }

            // ---- look
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, -85f, 85f);
            }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
                SetCursorLocked(true);

            recoilPitch = Mathf.MoveTowards(recoilPitch, 0f, Time.deltaTime * 25f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cam.transform.localRotation = Quaternion.Euler(pitch - recoilPitch, 0f, 0f);

            // ---- move
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            Vector3 input = new Vector3(x, 0f, z);
            if (input.sqrMagnitude > 1f) input.Normalize();
            IsMoving = input.sqrMagnitude > 0.01f;

            bool wantSprint = kb.leftShiftKey.isPressed && z > 0.1f && IsMoving;
            if (Stamina <= 0.5f) staminaLocked = true;
            if (Stamina > maxStamina * 0.25f) staminaLocked = false;
            IsSprinting = wantSprint && !staminaLocked;

            if (IsSprinting) Stamina = Mathf.Max(0f, Stamina - sprintDrain * Time.deltaTime);
            else Stamina = Mathf.Min(maxStamina, Stamina + staminaRegen * Time.deltaTime);

            float speed = IsSprinting ? sprintSpeed : walkSpeed;
            Vector3 targetVel = transform.TransformDirection(input) * speed;
            horizontal = Vector3.MoveTowards(horizontal, targetVel, acceleration * Time.deltaTime);

            if (cc.isGrounded && vy < 0f) vy = -2f;
            if (cc.isGrounded && kb.spaceKey.wasPressedThisFrame) vy = jumpSpeed;
            vy += gravity * Time.deltaTime;

            Vector3 move = horizontal;
            move.y = vy;
            cc.Move(move * Time.deltaTime);

            // ---- footsteps: sound + noise the zombies can hear
            if (cc.isGrounded && horizontal.sqrMagnitude > 0.5f)
            {
                stepDistance += horizontal.magnitude * Time.deltaTime;
                float stride = IsSprinting ? 2.3f : 1.8f;
                if (stepDistance >= stride)
                {
                    stepDistance = 0f;
                    GameAudio.PlayAt(GameAudio.Step, transform.position, IsSprinting ? 0.8f : 0.45f);
                    NoiseSystem.Emit(transform.position, IsSprinting ? 14f : 5f, NoiseKind.Footstep, gameObject);
                }
            }

            // ---- flashlight
            if (flashlight != null && kb.fKey.wasPressedThisFrame)
            {
                flashlight.enabled = !flashlight.enabled;
                GameAudio.Play2D(GameAudio.DryFire, 0.4f);
            }

            UpdateCameraOffset();
        }

        // ------------------------------------------------------------------ external control
        /// <summary>Ask to move in a world-space direction at 'speed' (call every frame; Vector3.zero stops).</summary>
        public void ExternalMove(Vector3 direction, float speed)
        {
            Vector3 d = direction;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) externalIntent = Vector3.zero;
            else externalIntent = d.normalized * Mathf.Max(0f, speed);
        }

        /// <summary>Ask to turn: yawRate deg/s (+ = right), pitchRate deg/s (+ = look up).
        /// The request is smoothed, so call it every frame and pass 0 to stop turning.</summary>
        public void ExternalLook(float yawRate, float pitchRate)
        {
            lookYawRate = yawRate;
            lookPitchRate = pitchRate;
        }

        /// <summary>Undo Die() so an autonomous demo can never end in a game-over screen.</summary>
        public void ReviveExternal()
        {
            if (!Dead) return;
            Dead = false;
            StopAllCoroutines();
            if (cam != null)
            {
                cam.transform.localPosition = camBase;
                cam.transform.localRotation = Quaternion.identity;
            }
            recoilPitch = 0f;
            shake = 0f;
            if (flashlight != null) flashlight.enabled = true;
            SetCursorLocked(true);
        }

        void UpdateExternal()
        {
            // ---- look (smoothed towards the requested rate)
            appliedYawRate = Mathf.Lerp(appliedYawRate, lookYawRate, Time.deltaTime * 8f);
            appliedPitchRate = Mathf.Lerp(appliedPitchRate, lookPitchRate, Time.deltaTime * 8f);
            lookYawRate = 0f;
            lookPitchRate = 0f;
            yaw = Mathf.Repeat(yaw + appliedYawRate * Time.deltaTime, 360f);
            pitch = Mathf.Clamp(pitch - appliedPitchRate * Time.deltaTime, -85f, 85f);

            recoilPitch = Mathf.MoveTowards(recoilPitch, 0f, Time.deltaTime * 25f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cam.transform.localRotation = Quaternion.Euler(pitch - recoilPitch, 0f, 0f);

            // ---- move (same acceleration / gravity / collision as normal play)
            IsMoving = externalIntent.sqrMagnitude > 0.01f;
            bool wantSprint = IsMoving && externalIntent.magnitude > walkSpeed * 1.05f;
            if (Stamina <= 0.5f) staminaLocked = true;
            if (Stamina > maxStamina * 0.25f) staminaLocked = false;
            IsSprinting = wantSprint && !staminaLocked;

            if (IsSprinting) Stamina = Mathf.Max(0f, Stamina - sprintDrain * Time.deltaTime);
            else Stamina = Mathf.Min(maxStamina, Stamina + staminaRegen * Time.deltaTime);

            Vector3 targetVel = IsSprinting ? externalIntent.normalized * sprintSpeed : externalIntent;
            horizontal = Vector3.MoveTowards(horizontal, targetVel, acceleration * Time.deltaTime);

            if (cc.isGrounded && vy < 0f) vy = -2f;
            vy += gravity * Time.deltaTime;

            Vector3 move = horizontal;
            move.y = vy;
            cc.Move(move * Time.deltaTime);

            // ---- footsteps: sound + noise the zombies can hear
            if (cc.isGrounded && horizontal.sqrMagnitude > 0.5f)
            {
                stepDistance += horizontal.magnitude * Time.deltaTime;
                float stride = IsSprinting ? 2.3f : 1.8f;
                if (stepDistance >= stride)
                {
                    stepDistance = 0f;
                    GameAudio.PlayAt(GameAudio.Step, transform.position, IsSprinting ? 0.8f : 0.45f);
                    NoiseSystem.Emit(transform.position, IsSprinting ? 14f : 5f, NoiseKind.Footstep, gameObject);
                }
            }

            UpdateCameraOffset();
        }

        void ApplyGravityOnly()
        {
            if (cc == null || !cc.enabled) return;
            if (cc.isGrounded && vy < 0f) vy = -2f;
            vy += gravity * Time.deltaTime;
            horizontal = Vector3.MoveTowards(horizontal, Vector3.zero, acceleration * Time.deltaTime);
            Vector3 move = horizontal;
            move.y = vy;
            cc.Move(move * Time.deltaTime);
        }

        void UpdateCameraOffset()
        {
            float speed = horizontal.magnitude;
            bobTime += Time.deltaTime * (IsSprinting ? 12f : 8f) * Mathf.Clamp01(speed);
            Vector3 bob = new Vector3(Mathf.Sin(bobTime * 0.5f) * 0.02f, Mathf.Abs(Mathf.Sin(bobTime * 0.5f)) * 0.035f, 0f) * Mathf.Clamp01(speed / 3f);
            Vector3 shk = shake > 0.001f ? (Vector3)(Random.insideUnitCircle * 0.05f * shake) : Vector3.zero;
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 1.6f);
            cam.transform.localPosition = camBase + bob + shk;
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody body = hit.collider.attachedRigidbody;
            if (body == null || body.isKinematic) return;
            if (hit.moveDirection.y < -0.3f) return;

            Vector3 dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            if (dir.sqrMagnitude < 0.0001f) return;
            float s = 0.6f + 3f * 40f / (body.mass + 40f);
            Vector3 v = dir.normalized * s;
            v.y = body.linearVelocity.y;
            body.linearVelocity = v;
        }

        // ------------------------------------------------------------------ factory
        public static GameObject Create(Vector3 position, float yaw)
        {
            GameObject player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = position;
            player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            CharacterController c = player.AddComponent<CharacterController>();
            c.height = 1.8f;
            c.radius = 0.32f;
            c.center = new Vector3(0f, 0.9f, 0f);
            c.stepOffset = 0.35f;
            c.slopeLimit = 50f;

            PlayerStats stats = player.AddComponent<PlayerStats>();

            GameObject camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            Camera cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 90f;
            cam.fieldOfView = 72f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.01f, 0.012f);
            camGo.AddComponent<AudioListener>();

            GameObject flashGo = new GameObject("Flashlight");
            flashGo.transform.SetParent(camGo.transform, false);
            flashGo.transform.localPosition = new Vector3(0.15f, -0.12f, 0.15f);
            Light flash = flashGo.AddComponent<Light>();
            flash.type = LightType.Spot;
            flash.range = 24f;
            flash.spotAngle = 58f;
            flash.innerSpotAngle = 28f;
            flash.intensity = 22f;
            flash.color = new Color(1f, 0.95f, 0.82f);
            flash.shadows = LightShadows.Soft;

            PlayerController pc = player.AddComponent<PlayerController>();
            pc.cam = cam;
            pc.flashlight = flash;

            PlayerWeapon weapon = player.AddComponent<PlayerWeapon>();
            weapon.cam = cam;
            weapon.BuildViewModel(camGo.transform);

            PlayerInteraction inter = player.AddComponent<PlayerInteraction>();
            inter.cam = cam;
            return player;
        }
    }
}
