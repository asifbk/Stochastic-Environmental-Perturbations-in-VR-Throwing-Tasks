using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Drives the flag's Cloth simulation from the live WindSystem state.
    /// - externalAcceleration smoothly tracks the current wind vector.
    /// - randomAcceleration simulates natural turbulence, scaling with wind speed.
    /// - The flag pole Transform is yaw-rotated to face into the wind so the flag
    ///   always streams away from the wind source.
    /// Attach this to the same GameObject as the WindSystem, or any active object.
    /// </summary>
    public class FlagWindController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The WindSystem that drives the basketball court wind.")]
        [SerializeField] private WindSystem windSystem;

        [Tooltip("The Cloth component on Flag/Cloth model.")]
        [SerializeField] private Cloth flagCloth;

        [Tooltip("The root Flag Transform (the pole). It will be yaw-rotated to face into the wind.")]
        [SerializeField] private Transform flagPoleTransform;

        [Header("Force Mapping")]
        [Tooltip("Multiplier converting wind speed (m/s) to cloth acceleration (m/s²). "
               + "Tune until the flag waves at the right intensity.")]
        [SerializeField] [Range(0.5f, 10f)] private float windForceScale = 3.5f;

        [Tooltip("Turbulence (random acceleration) as a fraction of current wind speed. "
               + "Higher = choppier flag.")]
        [SerializeField] [Range(0f, 1f)] private float turbulenceFraction = 0.25f;

        [Header("Smoothing")]
        [Tooltip("How quickly cloth acceleration tracks a wind change. Lower = lazier response.")]
        [SerializeField] [Range(1f, 20f)] private float accelerationLerpSpeed = 4f;

        [Tooltip("How quickly the flag pole rotates to face into new wind. Lower = slower spin.")]
        [SerializeField] [Range(0.5f, 10f)] private float poleTurnSpeed = 2f;

        // Internal smoothed acceleration value fed to the Cloth component each frame.
        private Vector3 _smoothedAcceleration;

        private void Reset()
        {
            // Auto-find references when component is first added.
            windSystem = FindObjectOfType<WindSystem>();
        }

        private void Update()
        {
            if (windSystem == null || flagCloth == null) return;

            UpdateClothAcceleration();
            UpdatePoleOrientation();
        }

        /// <summary>
        /// Smoothly drives cloth.externalAcceleration from the live wind vector,
        /// and scales cloth.randomAcceleration with current wind speed for turbulence.
        /// </summary>
        private void UpdateClothAcceleration()
        {
            // Target acceleration = wind direction × speed × force scale.
            Vector3 targetAcceleration = windSystem.WindDirection
                                       * (windSystem.WindSpeedMs * windForceScale);

            // Lerp toward target for a natural gust-smoothing effect.
            _smoothedAcceleration = Vector3.Lerp(
                _smoothedAcceleration,
                targetAcceleration,
                Time.deltaTime * accelerationLerpSpeed
            );

            flagCloth.externalAcceleration = _smoothedAcceleration;

            // Turbulence scales with wind speed so a calm wind = a still flag.
            float turbulence = windSystem.WindSpeedMs * turbulenceFraction * windForceScale;
            flagCloth.randomAcceleration = new Vector3(turbulence * 0.8f, turbulence * 0.3f, turbulence * 0.8f);
        }

        /// <summary>
        /// Yaw-rotates the flag pole so it always faces away from the wind source —
        /// the flag streams downwind, matching the cloth simulation visually.
        /// Only the Y-axis is changed; tilt and roll are preserved.
        /// </summary>
        private void UpdatePoleOrientation()
        {
            if (flagPoleTransform == null || windSystem.WindSpeedMs < 0.1f) return;

            // The flag should point in the wind direction (away from source).
            Vector3 windDir = windSystem.WindDirection;
            if (windDir.sqrMagnitude < 0.001f) return;

            Quaternion targetYaw   = Quaternion.LookRotation(windDir, Vector3.up);
            Quaternion currentRot  = flagPoleTransform.rotation;

            // Only lerp the yaw component so pitch/roll set in the inspector stays intact.
            float targetY  = targetYaw.eulerAngles.y;
            float currentY = currentRot.eulerAngles.y;
            float newY     = Mathf.LerpAngle(currentY, targetY, Time.deltaTime * poleTurnSpeed);

            Vector3 euler  = currentRot.eulerAngles;
            flagPoleTransform.rotation = Quaternion.Euler(euler.x, newY, euler.z);
        }
    }
}
