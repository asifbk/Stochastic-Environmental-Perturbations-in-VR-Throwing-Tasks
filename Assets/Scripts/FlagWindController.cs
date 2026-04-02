using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Drives the flag's Cloth simulation from WindSystem.WindDirection.
    /// Converts the world-space wind vector into cloth local space so the cloth
    /// always streams in the direction the WindVane is pointing.
    /// </summary>
    public class FlagWindController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private WindSystem windSystem;
        [SerializeField] private Cloth flagCloth;

        [Header("Force Mapping")]
        [SerializeField] [Range(0.5f, 10f)] private float windForceScale = 5f;
        [SerializeField] [Range(0f, 1f)]    private float turbulenceFraction = 0.3f;
        [SerializeField] [Range(1f, 20f)]   private float accelerationLerpSpeed = 3f;

        private Vector3 _smoothedWorld;

        private void Reset() => windSystem = FindObjectOfType<WindSystem>();

        private void Update()
        {
            if (windSystem == null || flagCloth == null) return;

            // Smooth wind in world space then convert to cloth local space.
            Vector3 target = windSystem.WindDirection * (windSystem.WindSpeedMs * windForceScale);
            _smoothedWorld = Vector3.Lerp(_smoothedWorld, target, Time.deltaTime * accelerationLerpSpeed);

            Transform t = flagCloth.transform;
            flagCloth.externalAcceleration = t.InverseTransformDirection(_smoothedWorld);

            // Turbulence — strongest perpendicular to wind for realistic ripple.
            float turb     = windSystem.WindSpeedMs * turbulenceFraction * windForceScale;
            Vector3 cross  = t.InverseTransformDirection(Vector3.Cross(windSystem.WindDirection, Vector3.up).normalized);
            Vector3 up     = t.InverseTransformDirection(Vector3.up);
            Vector3 along  = t.InverseTransformDirection(windSystem.WindDirection);

            flagCloth.randomAcceleration = cross * (turb * 1.2f) + up * (turb * 0.8f) + along * (turb * 0.3f);
        }
    }
}
