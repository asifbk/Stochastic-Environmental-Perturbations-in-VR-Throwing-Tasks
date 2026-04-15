using UnityEngine;
using SG;

namespace Basketball
{
    /// <summary>
    /// Constrains the Flag GameObject to rotate only on the world Y axis.
    /// When WindSystem is assigned and the flag is not being grabbed, the vane
    /// automatically rotates to face the active wind direction. Manual grab via
    /// SG_Grabable overrides the auto-rotation and sets a new wind direction.
    /// Also keeps the WindVane child aligned to the same Y so WindSystem
    /// can continue to read wind direction from windVane.forward.
    /// </summary>
    [RequireComponent(typeof(SG_Grabable))]
    public class FlagRotationController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The WindVane child Transform whose forward defines the wind direction.")]
        [SerializeField] private Transform windVane;

        [Tooltip("WindSystem to read the wind direction from for auto-rotation.")]
        [SerializeField] private WindSystem windSystem;

        [Tooltip("Fixed X Euler angle the flag pole was authored at (typically 270°).")]
        [SerializeField] private float lockedLocalEulerX = 270f;

        [Tooltip("Fixed Z Euler angle the flag pole was authored at (typically 0°).")]
        [SerializeField] private float lockedLocalEulerZ = 0f;

        [Header("Auto-Rotation")]
        [Tooltip("Degrees per second the vane rotates toward the wind direction when not grabbed.")]
        [SerializeField] [Min(1f)] private float rotationSpeed = 90f;

        private SG_Grabable _grabable;
        private float _currentYAngle;

        private void Awake()
        {
            _grabable = GetComponent<SG_Grabable>();
            _currentYAngle = transform.eulerAngles.y;
        }

        private void LateUpdate()
        {
            bool isGrabbed = _grabable != null && _grabable.IsGrabbed();

            if (isGrabbed)
            {
                // Player is manually rotating — read Y from whatever the grab applied.
                _currentYAngle = transform.eulerAngles.y;
            }
            else if (windSystem != null)
            {
                // Rotate toward the angle WindSystem rolled — reads TargetWindAngleY directly
                // to avoid the circular dependency of deriving angle back from WindDirection.
                float targetY = windSystem.TargetWindAngleY;
                _currentYAngle = Mathf.MoveTowardsAngle(_currentYAngle, targetY, rotationSpeed * Time.deltaTime);
            }

            // Lock X and Z to their authored values, keep only Y free.
            transform.eulerAngles = new Vector3(lockedLocalEulerX, _currentYAngle, lockedLocalEulerZ);

            // Keep the WindVane aligned to the same world Y so WindSystem reads correctly.
            if (windVane != null)
                windVane.rotation = Quaternion.Euler(0f, _currentYAngle, 0f);
        }

        /// <summary>
        /// Current wind direction derived from the flag's Y rotation.
        /// Convention matches the project's world axes: +X = North, +Z = East, -Z = West.
        /// A flag Y angle of 270° (pointing West) returns (0, 0, -1).
        /// </summary>
        public Vector3 WindDirection =>
            new Vector3(
                Mathf.Cos(_currentYAngle * Mathf.Deg2Rad),
                0f,
                Mathf.Sin(_currentYAngle * Mathf.Deg2Rad)
            ).normalized;
    }
}
