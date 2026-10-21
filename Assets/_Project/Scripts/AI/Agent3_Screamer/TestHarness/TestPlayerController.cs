using UnityEngine;
using UnityEngine.InputSystem;

namespace ScreamerAI.TestHarness
{
    /// <summary>
    /// PLACEHOLDER first-person controller so the Screamer can be tested on its own
    /// (the project uses the new Input System only). The real player controller from the
    /// team replaces this - just make sure it is tagged "Player".
    /// WASD move - Shift sprint - Mouse look - F flashlight - Esc release cursor - click to re-capture cursor.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class TestPlayerController : MonoBehaviour
    {
        public Transform cameraPivot;
        public Light flashlight;
        public float walkSpeed = 3.5f;
        public float sprintSpeed = 6.0f;
        public float mouseSensitivity = 0.1f;
        public float gravity = -20f;

        CharacterController controller;
        float yaw;
        float pitch;
        float verticalVelocity;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            yaw = transform.eulerAngles.y;
        }

        void Start()
        {
            LockCursor(true);
        }

        void OnDisable()
        {
            LockCursor(false);
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null) return;

            if (kb.escapeKey.wasPressedThisFrame) LockCursor(false);
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);

            // ---- look
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, -85f, 85f);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            // ---- move
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            Vector3 input = new Vector3(x, 0f, z);
            if (input.sqrMagnitude > 1f) input.Normalize();

            float speed = kb.leftShiftKey.isPressed ? sprintSpeed : walkSpeed;
            Vector3 move = transform.TransformDirection(input) * speed;

            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;
            move.y = verticalVelocity;
            controller.Move(move * Time.deltaTime);

            if (flashlight != null && kb.fKey.wasPressedThisFrame) flashlight.enabled = !flashlight.enabled;
        }
    }
}
