using UnityEngine;
using SG;

namespace Basketball
{
    /// <summary>
    /// Constrains the Flag GameObject to rotate only on the world Y axis.
    /// Works by stripping X and Z from the local Euler angles every frame,
    /// preserving whatever Y the SG_Grabable interaction produces.
    /// Also keeps the WindVane child aligned to the same Y so WindSystem
    /// can continue to read wind direction from windVane.forward.
    /// </summary>
    [RequireComponent(typeof(SG_Grabable))]
    public class FlagRotationController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The WindVane child Transform whose forward defines the wind direction.")]
        [SerializeField] private Transform windVane;

        [Tooltip("Fixed X Euler angle the flag pole was authored at (typically 270°).")]
        [SerializeField] private float lockedLocalEulerX = 270f;

        [Tooltip("Fixed Z Euler angle the flag pole was authored at (typically 0°).")]
        [SerializeField] private float lockedLocalEulerZ = 0f;

        private SG_Grabable _grabable;
        private float _currentYAngle;

        private void Awake()
        {
            _grabable = GetComponent<SG_Grabable>();
            _currentYAngle = transform.eulerAngles.y;
        }

        private void LateUpdate()
        {
            // Extract Y from whatever rotation the grab interaction applied.
            _currentYAngle = transform.eulerAngles.y;

            // Lock X and Z to their authored values, keep only Y free.
            transform.eulerAngles = new Vector3(lockedLocalEulerX, _currentYAngle, lockedLocalEulerZ);

            // Keep the WindVane aligned to the same world Y so WindSystem reads correctly.
            if (windVane != null)
                windVane.rotation = Quaternion.Euler(0f, _currentYAngle, 0f);
        }

        /// <summary>Current wind direction derived from this flag's Y rotation.</summary>
        public Vector3 WindDirection =>
            new Vector3(
                Mathf.Sin(_currentYAngle * Mathf.Deg2Rad),
                0f,
                Mathf.Cos(_currentYAngle * Mathf.Deg2Rad)
            ).normalized;
    }
}
