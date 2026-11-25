using UnityEngine;
using UnityEngine.InputSystem;

namespace CsgBrushPlus.Sandbox
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(PlayerInput))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [InspectorName("Камера игрока")]
        [Tooltip("Дочерняя камера. Контроллер наклоняет её вверх и вниз.")]
        [SerializeField] private Transform view;

        [InspectorName("Скорость ходьбы")]
        [Tooltip("Метры в секунду. Ноль отключает ходьбу.")]
        [SerializeField, Min(0f)] private float moveSpeed = 3f;

        [InspectorName("Высота прыжка")]
        [Tooltip("Высота в метрах при свободном прыжке. Ноль отключает прыжок.")]
        [SerializeField, Min(0f)] private float jumpHeight = 0.7f;

        [InspectorName("Чувствительность мыши")]
        [Tooltip("Градусы поворота на пиксель движения мыши. Ноль отключает обзор мышью.")]
        [SerializeField, Min(0f)] private float mouseSensitivity = 0.12f;

        [InspectorName("Скорость обзора геймпадом")]
        [Tooltip("Градусы в секунду при полном отклонении правого стика. Ноль отключает обзор стиком.")]
        [SerializeField, Min(0f)] private float stickLookSpeed = 140f;

        [InspectorName("Глубина возврата")]
        [Tooltip("Метры ниже точки старта до автоматического возврата. Минимум 1 м.")]
        [SerializeField, Min(1f)] private float respawnDepth = 5f;

        private const float Gravity = -18f;
        private const float GroundedSpeed = -2f;
        private const float MaxPitch = 85f;

        private CharacterController body;
        private InputAction move;
        private InputAction lookMouse;
        private InputAction lookStick;
        private InputAction jump;
        private InputAction respawn;
        private InputAction releaseCursor;
        private InputAction captureCursor;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private Quaternion startViewRotation;
        private float pitch;
        private float verticalSpeed;
        private bool cursorCaptured;

        private void Start()
        {
            body = GetComponent<CharacterController>();
            var actions = GetComponent<PlayerInput>().actions.FindActionMap("Exploration", true);
            move = actions.FindAction("Move", true);
            lookMouse = actions.FindAction("LookMouse", true);
            lookStick = actions.FindAction("LookStick", true);
            jump = actions.FindAction("Jump", true);
            respawn = actions.FindAction("Respawn", true);
            releaseCursor = actions.FindAction("ReleaseCursor", true);
            captureCursor = actions.FindAction("CaptureCursor", true);
            startPosition = transform.position;
            startRotation = transform.rotation;
            startViewRotation = view.localRotation;

            if (Application.isFocused)
                SetCursorCaptured(true);
        }

        private void Update()
        {
            if (releaseCursor.WasPressedThisFrame())
                SetCursorCaptured(false);
            else if (captureCursor.WasPressedThisFrame() && Application.isFocused && Cursor.lockState != CursorLockMode.Locked)
                SetCursorCaptured(true);

            if (respawn.WasPressedThisFrame() || transform.position.y < startPosition.y - respawnDepth)
            {
                ReturnToStart();
                return;
            }

            float deltaTime = Time.deltaTime;
            UpdateLook(deltaTime);
            UpdateMovement(deltaTime);
        }

        private void UpdateLook(float deltaTime)
        {
            Vector2 look = lookStick.ReadValue<Vector2>() * (stickLookSpeed * deltaTime);
            if (Cursor.lockState == CursorLockMode.Locked)
                look += lookMouse.ReadValue<Vector2>() * mouseSensitivity;

            transform.Rotate(Vector3.up, look.x, Space.World);
            pitch = Mathf.Clamp(pitch - look.y, -MaxPitch, MaxPitch);
            view.localRotation = startViewRotation * Quaternion.Euler(pitch, 0f, 0f);
        }

        private void UpdateMovement(float deltaTime)
        {
            if (body.isGrounded && verticalSpeed < 0f)
                verticalSpeed = GroundedSpeed;
            if (body.isGrounded && jumpHeight > 0f && jump.WasPressedThisFrame())
                verticalSpeed = Mathf.Sqrt(-2f * Gravity * jumpHeight);

            Vector2 input = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
            Vector3 planar = (transform.right * input.x + transform.forward * input.y) * moveSpeed;
            verticalSpeed += Gravity * deltaTime;
            var contacts = body.Move((planar + Vector3.up * verticalSpeed) * deltaTime);
            if ((contacts & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
                verticalSpeed = 0f;
        }

        private void ReturnToStart()
        {
            body.enabled = false;
            transform.SetPositionAndRotation(startPosition, startRotation);
            view.localRotation = startViewRotation;
            body.enabled = true;
            pitch = 0f;
            verticalSpeed = 0f;
        }

        private void OnDisable()
        {
            if (cursorCaptured)
                SetCursorCaptured(false);
        }

        private void SetCursorCaptured(bool captured)
        {
            cursorCaptured = captured;
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }
    }
}
