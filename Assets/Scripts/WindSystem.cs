using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Provides wind speed and direction for ball physics and flag cloth simulation.
    /// When a FlagRotationController is assigned, its Y-axis rotation is the authoritative
    /// wind direction and the random vane-snapping is disabled. Wind speed still varies
    /// randomly over time.
    /// </summary>
    public class WindSystem : MonoBehaviour
    {
        [Header("Wind Configuration")]
        [SerializeField] private float baseWindSpeed = 3f;
        [SerializeField] private float windVariation = 2f;
        [SerializeField] [Min(0.5f)] private float windChangeInterval = 4f;

        [Header("Wind Audio")]
        [Tooltip("AudioSource on this GameObject playing the looping wind clip.")]
        [SerializeField] private AudioSource windAudioSource;
        [Tooltip("Wind speed (m/s) at which the audio reaches full volume.")]
        [SerializeField] private float maxAudioWindSpeed = 8f;
        [Tooltip("Volume fade speed — higher values snap faster to the target volume.")]
        [SerializeField] [Min(0.1f)] private float volumeFadeSpeed = 2f;

        [Header("Flag Direction Source")]
        [Tooltip("When assigned, the wind direction is read directly from the flag's Y rotation. "
               + "The WindVane child and random-axis logic are not used.")]
        [SerializeField] private FlagRotationController flagRotationController;

        [Header("Wind Vane (fallback)")]
        [Tooltip("Used only when FlagRotationController is not assigned. "
               + "The WindVane pivot Transform child of the Flag.")]
        [SerializeField] private Transform windVane;

        [Header("Ball Targets")]
        [SerializeField] private Rigidbody[] ballRigidbodies;

        private float _currentWindSpeed;
        private float _nextChangeTime;

        /// <summary>Current wind direction angle in degrees. Cos(Y)→X, Sin(Y)→Z (project convention).</summary>
        private float _currentWindAngleY;

        /// <summary>Current wind direction as a horizontal unit vector.</summary>
        public Vector3 WindDirection
        {
            get
            {
                if (flagRotationController != null)
                    return flagRotationController.WindDirection;

                if (windVane != null)
                    return new Vector3(windVane.forward.x, 0f, windVane.forward.z).normalized;

                return new Vector3(
                    Mathf.Cos(_currentWindAngleY * Mathf.Deg2Rad),
                    0f,
                    Mathf.Sin(_currentWindAngleY * Mathf.Deg2Rad)).normalized;
            }
        }

        /// <summary>
        /// The target Y angle the flag should rotate toward this wind cycle.
        /// Exposed so FlagRotationController can read it for smooth auto-rotation.
        /// </summary>
        public float TargetWindAngleY => _currentWindAngleY;

        /// <summary>Current wind speed in metres per second.</summary>
        public float WindSpeedMs => _currentWindSpeed;

        /// <summary>Current wind speed in kilometres per hour.</summary>
        public float WindSpeedKmh => _currentWindSpeed * 3.6f;

        /// <summary>
        /// Horizontal wind angle in degrees using the project's axis convention:
        /// +Z = East (90°), -Z = West (270°), +X = North (0°), -X = South (180°).
        /// </summary>
        public float WindAngleDeg =>
            (Mathf.Atan2(WindDirection.z, WindDirection.x) * Mathf.Rad2Deg % 360f + 360f) % 360f;

        /// <summary>
        /// Compass cardinal for the direction the wind is blowing TOWARD.
        /// Convention: +X = North (0°), +Z = East (90°), -X = South (180°), -Z = West (270°).
        /// </summary>
        public string WindCardinal()
        {
            // WindAngleDeg already maps: 0°=N, 90°=E, 180°=S, 270°=W — index directly.
            float a = WindAngleDeg;
            string[] cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return cardinals[Mathf.RoundToInt(a / 45f) % 8];
        }

        private void Start() => RollNewWindSpeed();

        private void Update()
        {
            if (Time.time >= _nextChangeTime)
                RollNewWindSpeed();

            UpdateAudio();
        }

        private void FixedUpdate()
        {
            if (_currentWindSpeed < 0.01f || ballRigidbodies == null) return;

            // Apply a downward-biased wind force: horizontal component matches flag direction,
            // plus a small downward push so balls drift into the ground in the wind direction.
            Vector3 horizontal = WindDirection * (_currentWindSpeed * 0.08f);
            Vector3 downwardBias = Vector3.down * (_currentWindSpeed * 0.02f);
            Vector3 windForce = horizontal + downwardBias;

            foreach (Rigidbody rb in ballRigidbodies)
            {
                if (rb != null && !rb.isKinematic && !rb.IsSleeping())
                    rb.AddForce(windForce, ForceMode.Force);
            }
        }

        /// <summary>Rolls a new wind speed and direction. Direction is restricted to East (90°) or West (270°).</summary>
        private void RollNewWindSpeed()
        {
            _currentWindSpeed  = Mathf.Max(0f, baseWindSpeed + Random.Range(-windVariation, windVariation));
            _currentWindAngleY = Random.value < 0.5f ? 90f : 270f;   // East (+Z) or West (-Z)
            _nextChangeTime    = Time.time + windChangeInterval;
        }

        /// <summary>
        /// Smoothly fades the wind audio volume and adjusts pitch to match the current wind speed.
        /// Volume is normalised between 0 and maxAudioWindSpeed.
        /// Pitch scales slightly with speed (0.9 at calm, 1.1 at full) for a natural feel.
        /// </summary>
        private void UpdateAudio()
        {
            if (windAudioSource == null) return;

            float targetVolume = Mathf.Clamp01(_currentWindSpeed / Mathf.Max(0.01f, maxAudioWindSpeed));
            float targetPitch  = Mathf.Lerp(0.9f, 1.1f, targetVolume);

            windAudioSource.volume = Mathf.MoveTowards(windAudioSource.volume, targetVolume, volumeFadeSpeed * Time.deltaTime);
            windAudioSource.pitch  = targetPitch;

            if (targetVolume > 0f && !windAudioSource.isPlaying)
                windAudioSource.Play();
            else if (targetVolume <= 0f && windAudioSource.isPlaying)
                windAudioSource.Stop();
        }
    }
}

