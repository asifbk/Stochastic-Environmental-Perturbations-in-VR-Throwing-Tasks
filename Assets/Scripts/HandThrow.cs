using System.Collections.Generic;
using SG;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Bridges SenseGlove hand release events to the basketball throwing pipeline.
    /// Detects when the player releases a ball from either hand and records the throw.
    /// Uses a velocity history ring buffer to compute a smooth, lag-free release velocity.
    /// Requires SG_Grabable on each ball and SG_PhysicsGrab on each SGHand.
    /// </summary>
    public class HandThrow : MonoBehaviour
    {
        // Number of frames in the velocity history buffer for each hand.
        private const int VelocityHistorySize = 10;

        [Header("SenseGlove References")]
        [SerializeField] private SG_PhysicsGrab rightHandGrab;
        [SerializeField] private SG_PhysicsGrab leftHandGrab;

        [Header("Wrist Trackers (XR controller / tracker Transforms, NOT physics Rigidbodies)")]
        [Tooltip("The actual tracked Transform of the right wrist — used for true hand velocity, bypassing spring lag.")]
        [SerializeField] private Transform rightWristTracker;
        [Tooltip("The actual tracked Transform of the left wrist.")]
        [SerializeField] private Transform leftWristTracker;

        [Header("Hand Physics Rigidbodies")]
        [Tooltip("Rigidbody on SGHand Right / PhysicsTrackingLayer — fallback if tracker not assigned.")]
        [SerializeField] private Rigidbody rightHandRigidbody;
        [Tooltip("Rigidbody on SGHand Left / PhysicsTrackingLayer — fallback if tracker not assigned.")]
        [SerializeField] private Rigidbody leftHandRigidbody;

        [Header("Ball References")]
        [SerializeField] private Rigidbody[] ballRigidbodies;
        [Tooltip("Position to respawn balls after they leave the court.")]
        [SerializeField] private Transform ballSpawnPoint;
        [Tooltip("Distance below spawnPoint at which a ball is considered out of bounds and gets reset.")]
        [SerializeField] private float outOfBoundsYThreshold = -5f;
        [Tooltip("How long a ball must be stationary before it auto-resets to spawn. Set to 0 to disable.")]
        [SerializeField] [Min(0f)] private float autoResetAfterSeconds = 6f;

        [Header("Throw Feel")]
        [Tooltip("Multiplier applied to the averaged release velocity. Tune for how 'punchy' throws feel.")]
        [SerializeField] [Range(0.5f, 3f)] private float velocityMultiplier = 1.2f;
        [Tooltip("Spin (angular velocity) magnitude applied to the ball on release for realistic backspin.")]
        [SerializeField] [Range(0f, 30f)] private float releaseSpinMagnitude = 12f;

        [Header("Flight Physics Normalisation")]
        [Tooltip("All balls are forced to this drag on release so they travel equal distances regardless of their Rigidbody settings.")]
        [SerializeField] [Min(0f)] private float flightDrag = 0f;
        [Tooltip("All balls are forced to this angular drag on release.")]
        [SerializeField] [Min(0f)] private float flightAngularDrag = 0.05f;
        [Tooltip("All balls are forced to this mass (kg) on release. Set to 0 to leave mass unchanged.")]
        [SerializeField] [Min(0f)] private float flightMass = 0.625f;

        [Header("Game System")]
        [SerializeField] private BallThrower ballThrower;

        // Ring buffers — one unified buffer per hand. Tracker position-delta is preferred;
        // Rigidbody velocity is the fallback. Only ONE is written per frame per hand,
        // so the index increments exactly once per frame and the buffer stays fully populated.
        private readonly Vector3[] _rightVelocityHistory = new Vector3[VelocityHistorySize];
        private readonly Vector3[] _leftVelocityHistory  = new Vector3[VelocityHistorySize];
        private int _rightHistoryIndex;
        private int _leftHistoryIndex;

        // Stores the last known tracker world-position to compute per-frame delta velocity.
        private Vector3 _rightTrackerLastPos;
        private Vector3 _leftTrackerLastPos;
        private bool _rightTrackerInitialized;
        private bool _leftTrackerInitialized;

        // Tracks last time each ball moved, for auto-reset.
        private readonly Dictionary<Rigidbody, float> _lastMovedTime = new Dictionary<Rigidbody, float>();
        private static readonly float VelocitySleepThreshold = 0.05f;

        /// <summary>
        /// Fired when the player releases a ball. Carries the smoothed release velocity and which hand was used.
        /// </summary>
        public event System.Action<Vector3, HandSide> OnBallReleased;

        public enum HandSide { Left, Right }

        private void OnEnable()
        {
            if (rightHandGrab != null)
                rightHandGrab.ReleasedObject.AddListener(OnRightHandReleased);

            if (leftHandGrab != null)
                leftHandGrab.ReleasedObject.AddListener(OnLeftHandReleased);
        }

        private void OnDisable()
        {
            if (rightHandGrab != null)
                rightHandGrab.ReleasedObject.RemoveListener(OnRightHandReleased);

            if (leftHandGrab != null)
                leftHandGrab.ReleasedObject.RemoveListener(OnLeftHandReleased);
        }

        private void Start()
        {
            if (ballRigidbodies != null)
            {
                foreach (Rigidbody rb in ballRigidbodies)
                {
                    if (rb != null)
                        _lastMovedTime[rb] = Time.time;
                }
            }
        }

        private void Update()
        {
            // Each hand samples exactly one source per frame — tracker (preferred) or Rigidbody (fallback).
            // This ensures the index increments once per frame and the buffer stays fully populated.
            SampleVelocity(rightWristTracker, rightHandRigidbody,
                           ref _rightTrackerLastPos, ref _rightTrackerInitialized,
                           _rightVelocityHistory, ref _rightHistoryIndex);

            SampleVelocity(leftWristTracker, leftHandRigidbody,
                           ref _leftTrackerLastPos, ref _leftTrackerInitialized,
                           _leftVelocityHistory, ref _leftHistoryIndex);

            if (ballRigidbodies == null) return;

            foreach (Rigidbody rb in ballRigidbodies)
            {
                if (rb == null || rb.isKinematic) continue;

                if (rb.velocity.magnitude > VelocitySleepThreshold)
                    _lastMovedTime[rb] = Time.time;

                if (ballSpawnPoint != null && rb.transform.position.y < outOfBoundsYThreshold)
                {
                    ResetBall(rb);
                    continue;
                }

                if (autoResetAfterSeconds > 0f
                    && _lastMovedTime.TryGetValue(rb, out float lastMoved)
                    && Time.time - lastMoved > autoResetAfterSeconds
                    && ballSpawnPoint != null)
                {
                    ResetBall(rb);
                }
            }
        }

        /// <summary>
        /// Samples velocity for one hand into its unified buffer — exactly once per frame.
        /// Prefers the XR wrist tracker (position delta) over the Rigidbody velocity to bypass SG spring lag.
        /// Initialises last-position on the first frame so the first sample is not a world-origin spike.
        /// </summary>
        private static void SampleVelocity(
            Transform tracker, Rigidbody fallbackRb,
            ref Vector3 lastTrackerPos, ref bool initialized,
            Vector3[] buffer, ref int index)
        {
            Vector3 velocity;

            if (tracker != null)
            {
                if (!initialized)
                {
                    // Seed last position to current position — prevents a huge spike on frame 1.
                    lastTrackerPos = tracker.position;
                    initialized = true;
                    velocity = Vector3.zero;
                }
                else
                {
                    velocity = (Time.deltaTime > 0f)
                        ? (tracker.position - lastTrackerPos) / Time.deltaTime
                        : Vector3.zero;
                    lastTrackerPos = tracker.position;
                }
            }
            else if (fallbackRb != null)
            {
                velocity = fallbackRb.velocity;
            }
            else
            {
                velocity = Vector3.zero;
            }

            buffer[index] = velocity;
            index = (index + 1) % buffer.Length;
        }

        /// <summary>Returns the vector with the highest magnitude from the history buffer — preserves peak throw speed.</summary>
        private static Vector3 PeakVelocity(Vector3[] buffer)
        {
            Vector3 peak = Vector3.zero;
            float peakSqr = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                float sqr = buffer[i].sqrMagnitude;
                if (sqr > peakSqr)
                {
                    peakSqr = sqr;
                    peak = buffer[i];
                }
            }
            return peak;
        }

        private void OnRightHandReleased(SG_Interactable interactable, SG_GrabScript grabScript)
        {
            HandleRelease(interactable, _rightVelocityHistory, HandSide.Right);
        }

        private void OnLeftHandReleased(SG_Interactable interactable, SG_GrabScript grabScript)
        {
            HandleRelease(interactable, _leftVelocityHistory, HandSide.Left);
        }

        /// <summary>
        /// Checks whether the released interactable is a basketball, applies smoothed release
        /// velocity + backspin, and records the shot.
        /// </summary>
        private void HandleRelease(SG_Interactable interactable, Vector3[] velocityHistory, HandSide side)
        {
            if (interactable == null) return;

            Rigidbody ballRb = interactable.GetComponent<Rigidbody>();
            if (ballRb == null || !IsBall(ballRb)) return;

            Vector3 releaseVelocity = PeakVelocity(velocityHistory) * velocityMultiplier;

            // Normalise physics properties so all balls fly identically regardless of their Rigidbody setup.
            NormaliseFlightPhysics(ballRb);

            // Apply the computed velocity directly to the ball for immediate, responsive feel.
            ballRb.velocity = releaseVelocity;

            // Add backspin for realistic basketball physics: spin axis is perpendicular to throw direction.
            if (releaseVelocity.magnitude > 0.1f && releaseSpinMagnitude > 0f)
            {
                Vector3 throwDir   = releaseVelocity.normalized;
                Vector3 spinAxis   = Vector3.Cross(throwDir, Vector3.up).normalized;
                ballRb.angularVelocity = spinAxis * releaseSpinMagnitude;
            }

            if (ballThrower != null)
                ballThrower.RecordShot();

            OnBallReleased?.Invoke(releaseVelocity, side);
            _lastMovedTime[ballRb] = Time.time;

            Debug.Log($"[HandThrow] Ball released by {side} hand. Smoothed velocity: {releaseVelocity.magnitude:F2} m/s.");
        }

        /// <summary>
        /// Forces the ball's drag, angular drag, and optionally mass to shared baseline values
        /// so that every ball follows the same flight arc for the same release velocity.
        /// </summary>
        private void NormaliseFlightPhysics(Rigidbody rb)
        {
            rb.drag        = flightDrag;
            rb.angularDrag = flightAngularDrag;

            if (flightMass > 0f)
                rb.mass = flightMass;
        }

        /// <summary>Returns true if the given Rigidbody belongs to one of the registered basketballs.</summary>
        private bool IsBall(Rigidbody rb)
        {
            if (ballRigidbodies == null) return false;
            foreach (Rigidbody b in ballRigidbodies)
            {
                if (ReferenceEquals(b, rb)) return true;
            }
            return false;
        }

        /// <summary>Teleports a ball back to the spawn point and zeroes its velocity.</summary>
        private void ResetBall(Rigidbody rb)
        {
            rb.velocity        = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.transform.position = ballSpawnPoint.position;
            rb.transform.rotation = Quaternion.identity;
            _lastMovedTime[rb] = Time.time;
        }
    }
}
