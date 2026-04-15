using UnityEngine;
using UnityEngine.InputSystem;

namespace Basketball
{
    /// <summary>
    /// Press T to fire the assigned basketball on a perfect parabolic arc toward the net.
    /// A dotted trajectory arc is drawn for a short preview duration before launch.
    /// Attach this component to any GameObject in the scene (e.g. BallThrower).
    /// </summary>
    public class AutoShot : MonoBehaviour
    {
        // ─── Constants ────────────────────────────────────────────────────────────
        private const int SimulationSteps    = 120;
        private const float SimulationTimeStep = 0.04f;

        // ─── Inspector ────────────────────────────────────────────────────────────
        [Header("References")]
        [Tooltip("The basketball Rigidbody to launch.")]
        [SerializeField] private Rigidbody ballRigidbody;

        [Tooltip("The Transform representing the hoop target (centre of the net). Use HoopScoreTrigger for exact centering.")]
        [SerializeField] private Transform netTarget;

        [Tooltip("The MeshCollider on Basketball_Stand (the rim). Disabled during flight so the ball passes cleanly through the hoop.")]
        [SerializeField] private Collider rimCollider;

        [Tooltip("AICoach reference — notified just before launch so scoring events are treated as intentional.")]
        [SerializeField] private AICoach aiCoach;

        [Header("Trajectory Tuning")]
        [Tooltip("Total flight time in seconds. Increasing this raises the arc height.")]
        [SerializeField] [Range(0.3f, 3f)] private float flightTime = 1.0f;

        [Tooltip("Seconds the arc preview is shown before the ball is launched.")]
        [SerializeField] [Range(0f, 3f)] private float previewDuration = 0.6f;

        [Tooltip("Extra seconds after flightTime before the rim collider is re-enabled. Allows the ball to fully clear the hoop.")]
        [SerializeField] [Range(0f, 2f)] private float rimReenableDelay = 0.5f;

        [Header("Arc Visual")]
        [Tooltip("Material used for the dotted arc line. Leave empty to use a plain white default.")]
        [SerializeField] private Material arcMaterial;

        [Tooltip("Width of the arc line in metres.")]
        [SerializeField] [Range(0.005f, 0.05f)] private float lineWidth = 0.02f;

        [Tooltip("Render every Nth simulated point as a dot segment.")]
        [SerializeField] [Range(1, 10)] private int dotSpacing = 3;

        // ─── Private State ────────────────────────────────────────────────────────
        private LineRenderer _lineRenderer;
        private Vector3      _launchVelocity;
        private float        _previewTimer = -1f;   // < 0 means idle
        private bool         _launched;
        private Vector3[]    _arcPoints = new Vector3[SimulationSteps];
        private Vector3      _lockedBallPosition;   // ball world-pos when T is pressed
        private float        _rimReenableAt = -1f;  // Time.time value at which rim collider is re-enabled

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _lineRenderer = gameObject.AddComponent<LineRenderer>();
            _lineRenderer.useWorldSpace    = true;
            _lineRenderer.positionCount    = 0;
            _lineRenderer.startWidth       = lineWidth;
            _lineRenderer.endWidth         = lineWidth * 0.5f;
            _lineRenderer.numCapVertices   = 4;
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows   = false;

            if (arcMaterial != null)
                _lineRenderer.material = arcMaterial;
        }

        private void Update()
        {
            if (Keyboard.current == null) return;

            // ── T pressed: begin preview ──────────────────────────────────────────
            if (Keyboard.current.tKey.wasPressedThisFrame)
                BeginPreview();

            // ── Counting down preview ─────────────────────────────────────────────
            if (_previewTimer >= 0f && !_launched)
            {
                // Redraw arc from locked position every frame so it stays stable.
                DrawArc(_lockedBallPosition, _launchVelocity);

                _previewTimer += Time.deltaTime;

                if (_previewTimer >= previewDuration)
                    Launch();
            }

            // ── Re-enable rim collider after ball has cleared the hoop ────────────
            if (_rimReenableAt >= 0f && Time.time >= _rimReenableAt)
            {
                if (rimCollider != null)
                    rimCollider.enabled = true;

                _rimReenableAt = -1f;
                Debug.Log("[AutoShot] Rim collider re-enabled.");
            }
        }

        // ─── Core ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Calculates the required launch velocity, freezes the ball in place, and starts the preview timer.
        /// </summary>
        private void BeginPreview()
        {
            if (ballRigidbody == null || netTarget == null)
            {
                Debug.LogWarning("[AutoShot] ballRigidbody or netTarget is not assigned.");
                return;
            }

            _lockedBallPosition = ballRigidbody.position;
            _launchVelocity     = CalculateLaunchVelocity(_lockedBallPosition, netTarget.position, flightTime);

            // Freeze the ball during preview so it does not drift.
            ballRigidbody.velocity        = Vector3.zero;
            ballRigidbody.angularVelocity = Vector3.zero;
            ballRigidbody.isKinematic     = true;

            _previewTimer = 0f;
            _launched     = false;

            Debug.Log($"[AutoShot] Preview started. Launch velocity: {_launchVelocity} ({_launchVelocity.magnitude:F2} m/s).");
        }

        /// <summary>
        /// Unfreezes the ball, disables the rim collider for the duration of the flight,
        /// and applies the computed launch velocity.
        /// </summary>
        private void Launch()
        {
            _launched     = true;
            _previewTimer = -1f;
            _lineRenderer.positionCount = 0;

            // Notify AICoach that this is an intentional throw before the ball is in flight.
            aiCoach?.NotifyIntentionalThrow();

            // Disable rim so the ball passes cleanly through the hoop centre.
            if (rimCollider != null)
            {
                rimCollider.enabled = false;
                _rimReenableAt = Time.time + flightTime + rimReenableDelay;
            }

            ballRigidbody.isKinematic     = false;
            ballRigidbody.velocity        = _launchVelocity;
            ballRigidbody.angularVelocity = Vector3.zero;

            Debug.Log("[AutoShot] Ball launched. Rim collider temporarily disabled.");
        }

        // ─── Physics Math ─────────────────────────────────────────────────────────

        /// <summary>
        /// Solves for the initial velocity vector that carries the ball from <paramref name="from"/>
        /// to <paramref name="to"/> in exactly <paramref name="t"/> seconds under Unity's gravity.
        /// Formula: v0 = (Δs - ½·g·t²) / t
        /// </summary>
        /// <param name="from">World-space launch position.</param>
        /// <param name="to">World-space target position.</param>
        /// <param name="t">Desired flight time in seconds.</param>
        public static Vector3 CalculateLaunchVelocity(Vector3 from, Vector3 to, float t)
        {
            Vector3 displacement = to - from;
            Vector3 velocityXZ   = new Vector3(displacement.x, 0f, displacement.z) / t;
            float   velocityY    = (displacement.y - 0.5f * Physics.gravity.y * t * t) / t;
            return velocityXZ + Vector3.up * velocityY;
        }

        // ─── Arc Visualisation ────────────────────────────────────────────────────

        /// <summary>
        /// Simulates the projectile arc with Euler integration and renders it as a dotted line.
        /// </summary>
        private void DrawArc(Vector3 startPos, Vector3 initialVelocity)
        {
            Vector3 pos     = startPos;
            Vector3 vel     = initialVelocity;
            float   gravity = Physics.gravity.y;
            int     dotCount = 0;

            for (int i = 0; i < SimulationSteps; i++)
            {
                _arcPoints[i] = pos;

                pos.x += vel.x * SimulationTimeStep;
                pos.y += vel.y * SimulationTimeStep + 0.5f * gravity * SimulationTimeStep * SimulationTimeStep;
                pos.z += vel.z * SimulationTimeStep;
                vel.y += gravity * SimulationTimeStep;

                if (i % dotSpacing == 0) dotCount++;
            }

            _lineRenderer.positionCount = dotCount * 2;
            int idx = 0;

            for (int i = 0; i < SimulationSteps - 1; i++)
            {
                if (i % dotSpacing != 0) continue;
                _lineRenderer.SetPosition(idx++, _arcPoints[i]);
                _lineRenderer.SetPosition(idx++, _arcPoints[i + 1]);
            }
        }
    }
}
