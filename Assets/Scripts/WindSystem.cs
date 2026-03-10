using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Simulates ambient wind by applying a time-varying horizontal force to registered ball Rigidbodies.
    /// Speed and direction update at a configurable interval to replicate natural wind gusts.
    /// </summary>
    public class WindSystem : MonoBehaviour
    {
        [Header("Wind Configuration")]
        [SerializeField] private float baseWindSpeed = 3f;
        [SerializeField] private float windVariation = 2f;
        [SerializeField] [Min(0.5f)] private float windChangeInterval = 4f;

        [Header("Ball Targets")]
        [SerializeField] private Rigidbody[] ballRigidbodies;

        private float _currentWindSpeed;
        private float _currentWindAngleDeg;
        private Vector3 _currentWindDirection;
        private float _nextChangeTime;

        /// <summary>Current normalised wind direction in world-space XZ plane.</summary>
        public Vector3 WindDirection => _currentWindDirection;

        /// <summary>Current wind speed in metres per second.</summary>
        public float WindSpeedMs => _currentWindSpeed;

        /// <summary>Current wind speed in kilometres per hour.</summary>
        public float WindSpeedKmh => _currentWindSpeed * 3.6f;

        /// <summary>Horizontal wind angle in degrees (0 = world +X, 90 = world +Z).</summary>
        public float WindAngleDeg => _currentWindAngleDeg;

        private void Start()
        {
            ApplyNewWind();
        }

        private void Update()
        {
            if (Time.time >= _nextChangeTime)
            {
                ApplyNewWind();
            }
        }

        private void FixedUpdate()
        {
            if (_currentWindSpeed < 0.01f || ballRigidbodies == null)
            {
                return;
            }

            // Scale wind to a light but noticeable force (basketball mass ~0.62 kg)
            Vector3 windForce = _currentWindDirection * (_currentWindSpeed * 0.08f);

            foreach (Rigidbody rb in ballRigidbodies)
            {
                if (rb != null && !rb.isKinematic && !rb.IsSleeping())
                {
                    rb.AddForce(windForce, ForceMode.Force);
                }
            }
        }

        /// <summary>
        /// Randomises wind speed and direction within configured bounds.
        /// </summary>
        private void ApplyNewWind()
        {
            _currentWindSpeed = Mathf.Max(0f, baseWindSpeed + Random.Range(-windVariation, windVariation));
            _currentWindAngleDeg = Random.Range(0f, 360f);

            float angleRad = _currentWindAngleDeg * Mathf.Deg2Rad;
            _currentWindDirection = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));

            _nextChangeTime = Time.time + windChangeInterval;
        }

        /// <summary>Returns a compass cardinal string for the current wind angle.</summary>
        public string WindCardinal()
        {
            string[] cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int index = Mathf.RoundToInt(_currentWindAngleDeg / 45f) % 8;
            return cardinals[index];
        }
    }
}
