using SG;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Renders a dotted parabolic arc preview while the player holds a basketball.
    /// Simulates the same projectile trajectory HandThrow will apply on release:
    /// uses the current peak velocity from the wrist tracker and the same velocityMultiplier.
    /// Attach to the BallThrower GameObject alongside HandThrow.
    /// </summary>
    [RequireComponent(typeof(HandThrow))]
    public class TrajectoryPreview : MonoBehaviour
    {
        // ─── Simulation ───────────────────────────────────────────────────────────
        private const int SimulationSteps = 90;
        private const float SimulationTimeStep = 0.05f;

        // ─── Inspector ────────────────────────────────────────────────────────────
        [Header("SenseGlove Grab Scripts")]
        [Tooltip("SG_PhysicsGrab on SGHand Right/Grab Layer.")]
        [SerializeField] private SG_PhysicsGrab rightHandGrab;
        [Tooltip("SG_PhysicsGrab on SGHand Left/Grab Layer.")]
        [SerializeField] private SG_PhysicsGrab leftHandGrab;

        [Header("Wrist Trackers")]
        [Tooltip("Same Transform assigned to HandThrow.rightWristTracker.")]
        [SerializeField] private Transform rightWristTracker;
        [Tooltip("Same Transform assigned to HandThrow.leftWristTracker.")]
        [SerializeField] private Transform leftWristTracker;

        [Header("Throw Tuning")]
        [Tooltip("Must match HandThrow.velocityMultiplier so the arc is accurate.")]
        [SerializeField] [Range(0.5f, 3f)] private float velocityMultiplier = 1.0f;

        [Header("Visual")]
        [Tooltip("Material for the dotted-line arc. Assign a material with 'Sprites/Default' or a dotted shader.")]
        [SerializeField] private Material arcMaterial;
        [Tooltip("Width of the rendered arc line.")]
        [SerializeField] [Range(0.005f, 0.05f)] private float lineWidth = 0.02f;
        [Tooltip("Number of visible dot segments (every Nth point is rendered).")]
        [SerializeField] [Range(1, 10)] private int dotSpacing = 3;

        // ─── Private State ────────────────────────────────────────────────────────
        private LineRenderer _lineRenderer;

        // Per-frame position-delta velocity sampling — mirrors HandThrow's SampleVelocity.
        private Vector3 _rightLastPos;
        private Vector3 _leftLastPos;
        private bool    _rightInitialized;
        private bool    _leftInitialized;
        private Vector3 _rightCurrentVelocity;
        private Vector3 _leftCurrentVelocity;

        private readonly Vector3[] _arcPoints = new Vector3[SimulationSteps];

        // Defer arc drawing for a few frames to avoid false grab states from SenseGlove at startup.
        private bool _warmUpDone;
        private int  _warmUpFramesRemaining = 3;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _lineRenderer = gameObject.AddComponent<LineRenderer>();
            _lineRenderer.useWorldSpace   = true;
            _lineRenderer.positionCount   = 0;
            _lineRenderer.startWidth      = lineWidth;
            _lineRenderer.endWidth        = lineWidth * 0.5f;
            _lineRenderer.numCapVertices  = 4;
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows  = false;

            if (arcMaterial != null)
                _lineRenderer.material = arcMaterial;
        }

        private void Update()
        {
            // Skip the first few frames — SenseGlove may report a false grip state at startup.
            if (!_warmUpDone)
            {
                if (--_warmUpFramesRemaining <= 0) _warmUpDone = true;
                _lineRenderer.positionCount = 0;
                return;
            }

            SampleTrackerVelocity(rightWristTracker, ref _rightLastPos, ref _rightInitialized, out _rightCurrentVelocity);
            SampleTrackerVelocity(leftWristTracker,  ref _leftLastPos,  ref _leftInitialized,  out _leftCurrentVelocity);

            Rigidbody heldBallRb = GetHeldBall(out Vector3 releaseVelocity);

            if (heldBallRb == null)
            {
                _lineRenderer.positionCount = 0;
                return;
            }

            DrawArc(heldBallRb.position, releaseVelocity * velocityMultiplier);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Computes position-delta velocity for one wrist tracker, identical to HandThrow.SampleVelocity.
        /// </summary>
        private static void SampleTrackerVelocity(
            Transform tracker,
            ref Vector3 lastPos, ref bool initialized,
            out Vector3 velocity)
        {
            if (tracker == null) { velocity = Vector3.zero; return; }

            if (!initialized)
            {
                lastPos     = tracker.position;
                initialized = true;
                velocity    = Vector3.zero;
                return;
            }

            velocity = Time.deltaTime > 0f
                ? (tracker.position - lastPos) / Time.deltaTime
                : Vector3.zero;

            lastPos = tracker.position;
        }

        /// <summary>
        /// Returns the Rigidbody of the basketball currently held by either hand,
        /// and outputs the corresponding wrist velocity as the predicted release velocity.
        /// Returns null when nothing is held.
        /// </summary>
        private Rigidbody GetHeldBall(out Vector3 releaseVelocity)
        {
            releaseVelocity = Vector3.zero;

            if (rightHandGrab != null && rightHandGrab.IsGrabbing)
            {
                Rigidbody rb = FindBallInGrab(rightHandGrab);
                if (rb != null) { releaseVelocity = _rightCurrentVelocity; return rb; }
            }

            if (leftHandGrab != null && leftHandGrab.IsGrabbing)
            {
                Rigidbody rb = FindBallInGrab(leftHandGrab);
                if (rb != null) { releaseVelocity = _leftCurrentVelocity; return rb; }
            }

            return null;
        }

        /// <summary>
        /// Searches the interactables currently grabbed by the given grab script for one with a Rigidbody.
        /// </summary>
        private static Rigidbody FindBallInGrab(SG_GrabScript grabScript)
        {
            SG_Interactable[] grabbed = grabScript.GrabbedObjects();
            foreach (SG_Interactable interactable in grabbed)
            {
                if (interactable == null) continue;
                Rigidbody rb = interactable.GetComponent<Rigidbody>();
                if (rb != null) return rb;
            }
            return null;
        }

        /// <summary>
        /// Simulates the projectile arc from startPos with the given initial velocity
        /// and feeds the dotted segments into the LineRenderer.
        /// </summary>
        private void DrawArc(Vector3 startPos, Vector3 initialVelocity)
        {
            Vector3 pos = startPos;
            Vector3 vel = initialVelocity;
            float   gravity = Physics.gravity.y;

            int dotCount = 0;

            for (int i = 0; i < SimulationSteps; i++)
            {
                _arcPoints[i] = pos;

                // Simple Euler integration matching Unity's physics.
                pos.x += vel.x * SimulationTimeStep;
                pos.y += vel.y * SimulationTimeStep + 0.5f * gravity * SimulationTimeStep * SimulationTimeStep;
                pos.z += vel.z * SimulationTimeStep;
                vel.y += gravity * SimulationTimeStep;

                // Count only the dotted (every Nth) points.
                if (i % dotSpacing == 0) dotCount++;
            }

            // Build the dotted-line point list: pairs of (start, end) for each visible segment.
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
