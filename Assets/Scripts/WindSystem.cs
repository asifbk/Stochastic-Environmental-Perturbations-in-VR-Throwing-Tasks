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

        /// <summary>Current wind direction as a horizontal unit vector.</summary>
        public Vector3 WindDirection
        {
            get
            {
                if (flagRotationController != null)
                    return flagRotationController.WindDirection;

                if (windVane != null)
                    return new Vector3(windVane.forward.x, 0f, windVane.forward.z).normalized;

                return Vector3.forward;
            }
        }

        /// <summary>Current wind speed in metres per second.</summary>
        public float WindSpeedMs => _currentWindSpeed;

        /// <summary>Current wind speed in kilometres per hour.</summary>
        public float WindSpeedKmh => _currentWindSpeed * 3.6f;

        /// <summary>Horizontal wind angle in degrees (0 = world +X, 90 = world +Z).</summary>
        public float WindAngleDeg =>
            Mathf.Atan2(WindDirection.z, WindDirection.x) * Mathf.Rad2Deg;

        /// <summary>Compass cardinal string for the current wind direction.</summary>
        public string WindCardinal()
        {
            float a = (WindAngleDeg % 360f + 360f) % 360f;
            string[] cardinals = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
            return cardinals[Mathf.RoundToInt(a / 45f) % 8];
        }

        private void Start() => RollNewWindSpeed();

        private void Update()
        {
            if (Time.time >= _nextChangeTime)
                RollNewWindSpeed();
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

        /// <summary>Rolls a new wind speed. Direction is now dictated by the flag rotation.</summary>
        private void RollNewWindSpeed()
        {
            _currentWindSpeed = Mathf.Max(0f, baseWindSpeed + Random.Range(-windVariation, windVariation));
            _nextChangeTime = Time.time + windChangeInterval;
        }
    }
}

