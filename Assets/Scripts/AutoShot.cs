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
        private const int   SimulationSteps    = 120;
        private const float SimulationTimeStep = 0.04f;
        private const int   GradientKeyCount   = 8;   // Unity Gradient max is 8 color/alpha keys.

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
        private float[]      _arcSpeeds = new float[SimulationSteps];
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
            else
            {
                // Fallback: plain white Sprites/Default so vertex color gradient is visible.
                _lineRenderer.material       = new Material(Shader.Find("Sprites/Default"));
                _lineRenderer.material.color = Color.white;
            }
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

            // Notify AICoach with full release kinematics so the outcome window opens
            // immediately — this ensures a miss is caught even without a HandThrow event.
            aiCoach?.NotifyAutoShotLaunched(ballRigidbody.position, _launchVelocity);

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
        /// Simulates the projectile arc with Euler integration, renders it as a dotted line,
        /// and colours each segment red with intensity proportional to local velocity magnitude.
        /// </summary>
        private void DrawArc(Vector3 startPos, Vector3 initialVelocity)
        {
            Vector3 pos     = startPos;
            Vector3 vel     = initialVelocity;
            float   gravity = Physics.gravity.y;
            int     dotCount = 0;

            float minSpeed = float.MaxValue;
            float maxSpeed = float.MinValue;

            for (int i = 0; i < SimulationSteps; i++)
            {
                _arcPoints[i] = pos;
                _arcSpeeds[i] = vel.magnitude;

                minSpeed = Mathf.Min(minSpeed, _arcSpeeds[i]);
                maxSpeed = Mathf.Max(maxSpeed, _arcSpeeds[i]);

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

            // Sample 8 evenly-spaced speeds across the full arc to build the gradient.
            // Unity Gradient is capped at GradientKeyCount (8) color and alpha keys.
            float speedRange = Mathf.Max(maxSpeed - minSpeed, 0.001f);
            var   colorKeys  = new GradientColorKey[GradientKeyCount];
            var   alphaKeys  = new GradientAlphaKey[GradientKeyCount];

            for (int k = 0; k < GradientKeyCount; k++)
            {
                float time      = (float)k / (GradientKeyCount - 1);
                int   sampleIdx = Mathf.RoundToInt(time * (SimulationSteps - 1));
                float t         = Mathf.Clamp01((_arcSpeeds[sampleIdx] - minSpeed) / speedRange);

                // Low speed → salmon-red (green 0.55, alpha 0.35).
                // High speed → vivid deep red (green 0, alpha 1).
                colorKeys[k] = new GradientColorKey(new Color(1f, Mathf.Lerp(0.55f, 0f, t), 0f), time);
                alphaKeys[k] = new GradientAlphaKey(Mathf.Lerp(0.35f, 1f, t), time);
            }

            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            _lineRenderer.colorGradient = gradient;
        }
    }
}
