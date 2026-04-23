using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Simple fly camera for desktop Play Mode.
    /// - Right-click + drag   : look around (mouse look)
    /// - WASD / Arrow keys    : move horizontally
    /// - E / Q                : move up / down
    /// - Hold Left Shift      : move faster
    /// </summary>
    public class FlyCameraController : MonoBehaviour
    {
        private const string LogPrefix = "[FlyCameraController]";

        [Header("Look")]
        [Tooltip("Mouse sensitivity for horizontal and vertical look.")]
        [SerializeField] private float mouseSensitivity = 3f;

        [Header("Movement")]
        [Tooltip("Base movement speed (units per second).")]
        [SerializeField] private float moveSpeed = 10f;

        [Tooltip("Speed multiplier applied when Left Shift is held.")]
        [SerializeField] private float sprintMultiplier = 3f;

        // ── Private State ─────────────────────────────────────────────────────────

        private float _yaw;
        private float _pitch;

        // ── Unity Lifecycle ───────────────────────────────────────────────────────

        private void Start()
        {
            Vector3 angles = transform.eulerAngles;
            _yaw   = angles.y;
            _pitch = angles.x;
        }

        private void Update()
        {
            HandleLook();
            HandleMovement();
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private void HandleLook()
        {
            if (!Input.GetMouseButton(1)) return;

            _yaw   += Input.GetAxis("Mouse X") * mouseSensitivity;
            _pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            _pitch  = Mathf.Clamp(_pitch, -89f, 89f);

            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void HandleMovement()
        {
            float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? sprintMultiplier : 1f);

            float horizontal = Input.GetAxis("Horizontal");
            float vertical   = Input.GetAxis("Vertical");
            float upDown     = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);

            Vector3 move = transform.right   * horizontal
                         + transform.forward * vertical
                         + Vector3.up        * upDown;

            transform.position += move * (speed * Time.deltaTime);
        }
    }
}
