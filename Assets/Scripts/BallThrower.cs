using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Throws a basketball toward the hoop using projectile motion when the P key is pressed.
    /// Assign the ball's Rigidbody, the hoop target Transform, and an optional throw origin Transform.
    /// </summary>
    public class BallThrower : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody ballRigidbody;
        [SerializeField] private Transform hoopTarget;
        [SerializeField] private Transform throwOriginTransform;

        [Header("Launch Settings")]
        [SerializeField] [Range(20f, 75f)] private float launchAngleDegrees = 52f;

        private Vector3 ThrowOrigin =>
            throwOriginTransform != null ? throwOriginTransform.position : ballRigidbody.transform.position;

        /// <summary>Total number of throw attempts since the session started.</summary>
        public int TotalShots { get; private set; }

        /// <summary>Fired immediately after a throw is successfully launched.</summary>
        public event System.Action OnShotFired;

        /// <summary>
        /// Increments the shot counter and fires OnShotFired without performing a physics throw.
        /// Used by HandThrow to keep the scoreboard in sync when the player throws manually.
        /// </summary>
        public void RecordShot()
        {
            TotalShots++;
            OnShotFired?.Invoke();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.P))
            {
                ThrowBall();
            }
        }

        /// <summary>
        /// Resets the ball to the throw origin and launches it toward the hoop using projectile motion.
        /// </summary>
        public void ThrowBall()
        {
            if (ballRigidbody == null || hoopTarget == null)
            {
                Debug.LogWarning("[BallThrower] Ball Rigidbody or Hoop Target is not assigned.");
                return;
            }

            // Reset ball physics state
            ballRigidbody.velocity = Vector3.zero;
            ballRigidbody.angularVelocity = Vector3.zero;
            ballRigidbody.transform.position = ThrowOrigin;
            ballRigidbody.transform.rotation = Quaternion.identity;

            Vector3 launchVelocity = ComputeLaunchVelocity(ThrowOrigin, hoopTarget.position, launchAngleDegrees);

            if (launchVelocity == Vector3.zero)
            {
                Debug.LogWarning("[BallThrower] Could not compute a valid launch velocity. Try adjusting the launch angle.");
                return;
            }

            ballRigidbody.AddForce(launchVelocity, ForceMode.VelocityChange);

            RecordShot();
        }

        /// <summary>
        /// Computes the initial velocity vector to reach the target from the origin at the given launch angle,
        /// accounting for gravity via projectile motion equations.
        /// </summary>
        private Vector3 ComputeLaunchVelocity(Vector3 origin, Vector3 target, float angleDegrees)
        {
            const float Gravity = 9.81f;

            Vector3 toTarget = target - origin;
            Vector3 horizontalDelta = new Vector3(toTarget.x, 0f, toTarget.z);
            float horizontalDistance = horizontalDelta.magnitude;
            float verticalDelta = toTarget.y;

            if (horizontalDistance < 0.001f)
            {
                return Vector3.zero;
            }

            float angleRad = angleDegrees * Mathf.Deg2Rad;
            float tanAngle = Mathf.Tan(angleRad);
            float cosAngle = Mathf.Cos(angleRad);

            float denominator = 2f * cosAngle * cosAngle * (horizontalDistance * tanAngle - verticalDelta);

            if (denominator <= 0f)
            {
                return Vector3.zero;
            }

            float speedSquared = Gravity * horizontalDistance * horizontalDistance / denominator;
            float speed = Mathf.Sqrt(speedSquared);

            Vector3 horizontalDirection = horizontalDelta.normalized;
            Vector3 velocity = horizontalDirection * (speed * cosAngle)
                             + Vector3.up * (speed * Mathf.Sin(angleRad));

            return velocity;
        }
    }
}
