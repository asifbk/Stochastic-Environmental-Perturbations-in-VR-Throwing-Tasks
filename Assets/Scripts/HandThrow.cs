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

        [Header("SenseGlove Tracked Hands (for finger flexion logging)")]
        [Tooltip("SG_TrackedHand on the right SGHand. Used to capture finger flexion at release.")]
        [SerializeField] private SG_TrackedHand rightTrackedHand;
        [Tooltip("SG_TrackedHand on the left SGHand. Used to capture finger flexion at release.")]
        [SerializeField] private SG_TrackedHand leftTrackedHand;

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

        // Ring buffers — one unified buffer per hand.
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

        // Per-ball grab start times for time-on-task measurement.
        private readonly Dictionary<Rigidbody, float> _grabStartTimes = new Dictionary<Rigidbody, float>();

        /// <summary>
        /// Fired when the player releases a ball.
        /// Carries the smoothed release velocity, which hand was used, the release world position,
        /// the grab-to-release duration, and per-finger normalized flexion (Thumb→Pinky, 0=open 1=closed).
        /// Finger flexion array is null if no SG_TrackedHand is assigned.
        /// </summary>
        public event System.Action<Vector3, HandSide, Vector3, float, float[]> OnBallReleased;

        public enum HandSide { Left, Right }

        private void OnEnable()
        {
            if (rightHandGrab != null)
            {
                rightHandGrab.GrabbedObject.AddListener(OnRightHandGrabbed);
                rightHandGrab.ReleasedObject.AddListener(OnRightHandReleased);
            }

            if (leftHandGrab != null)
            {
                leftHandGrab.GrabbedObject.AddListener(OnLeftHandGrabbed);
                leftHandGrab.ReleasedObject.AddListener(OnLeftHandReleased);
            }
        }

        private void OnDisable()
        {
            if (rightHandGrab != null)
            {
                rightHandGrab.GrabbedObject.RemoveListener(OnRightHandGrabbed);
                rightHandGrab.ReleasedObject.RemoveListener(OnRightHandReleased);
            }

            if (leftHandGrab != null)
            {
                leftHandGrab.GrabbedObject.RemoveListener(OnLeftHandGrabbed);
                leftHandGrab.ReleasedObject.RemoveListener(OnLeftHandReleased);
            }
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

        // ─── Grab events ──────────────────────────────────────────────────────────

        private void OnRightHandGrabbed(SG_Interactable interactable, SG_GrabScript grabScript)
            => RecordGrabStart(interactable);

        private void OnLeftHandGrabbed(SG_Interactable interactable, SG_GrabScript grabScript)
            => RecordGrabStart(interactable);

        /// <summary>Stamps the grab start time so we can compute time-on-task at release.</summary>
        private void RecordGrabStart(SG_Interactable interactable)
        {
            if (interactable == null) return;
            Rigidbody rb = interactable.GetComponent<Rigidbody>();
            if (rb == null || !IsBall(rb)) return;
            _grabStartTimes[rb] = Time.time;
        }

        // ─── Velocity sampling ────────────────────────────────────────────────────

        /// <summary>
        /// Samples velocity for one hand into its unified buffer — exactly once per frame.
        /// Prefers the XR wrist tracker (position delta) over the Rigidbody velocity to bypass SG spring lag.
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

        /// <summary>Returns the vector with the highest magnitude from the history buffer.</summary>
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

        // ─── Release events ───────────────────────────────────────────────────────

        private void OnRightHandReleased(SG_Interactable interactable, SG_GrabScript grabScript)
        {
            HandleRelease(interactable, _rightVelocityHistory, HandSide.Right, rightTrackedHand);
        }

        private void OnLeftHandReleased(SG_Interactable interactable, SG_GrabScript grabScript)
        {
            HandleRelease(interactable, _leftVelocityHistory, HandSide.Left, leftTrackedHand);
        }

        /// <summary>
        /// Applies smoothed release velocity + backspin, records the shot, and fires OnBallReleased
        /// with position, time-on-task, and finger flexion data for the logger.
        /// </summary>
        private void HandleRelease(SG_Interactable interactable, Vector3[] velocityHistory,
                                   HandSide side, SG_TrackedHand trackedHand)
        {
            if (interactable == null) return;

            Rigidbody ballRb = interactable.GetComponent<Rigidbody>();
            if (ballRb == null || !IsBall(ballRb)) return;

            Vector3 releaseVelocity = PeakVelocity(velocityHistory) * velocityMultiplier;
            Vector3 releasePosition = ballRb.position;

            // Time-on-task: seconds from grab to release.
            float grabDuration = _grabStartTimes.TryGetValue(ballRb, out float grabStart)
                ? Time.time - grabStart
                : -1f;

            // Per-finger normalized flexion at release (Thumb=0 … Pinky=4). 0=open, 1=closed.
            float[] fingerFlexion = SampleFingerFlexion(trackedHand);

            NormaliseFlightPhysics(ballRb);
            ballRb.velocity = releaseVelocity;

            if (releaseVelocity.magnitude > 0.1f && releaseSpinMagnitude > 0f)
            {
                Vector3 throwDir  = releaseVelocity.normalized;
                Vector3 spinAxis  = Vector3.Cross(throwDir, Vector3.up).normalized;
                ballRb.angularVelocity = spinAxis * releaseSpinMagnitude;
            }

            if (ballThrower != null)
                ballThrower.RecordShot();

            OnBallReleased?.Invoke(releaseVelocity, side, releasePosition, grabDuration, fingerFlexion);
            _lastMovedTime[ballRb] = Time.time;

            Debug.Log($"[HandThrow] Ball released by {side} hand. Speed: {releaseVelocity.magnitude:F2} m/s. " +
                      $"GrabDuration: {grabDuration:F2}s.");
        }

        // ─── SenseGlove finger flexion ────────────────────────────────────────────

        /// <summary>
        /// Returns per-finger normalized flexion [Thumb, Index, Middle, Ring, Pinky] in 0–1 range
        /// from SG_HandPose.normalizedFlexion. Returns null if no tracked hand is available.
        /// </summary>
        private static float[] SampleFingerFlexion(SG_TrackedHand trackedHand)
        {
            if (trackedHand == null) return null;

            SG_HandPose pose = trackedHand.RealHandPose;
            if (pose == null || pose.normalizedFlexion == null) return null;

            const int fingerCount = 5;
            float[] flexion = new float[fingerCount];
            for (int f = 0; f < fingerCount && f < pose.normalizedFlexion.Length; f++)
                flexion[f] = pose.normalizedFlexion[f];

            return flexion;
        }

        // ─── Physics helpers ──────────────────────────────────────────────────────

        /// <summary>Forces the ball's drag, angular drag, and mass to shared baseline values.</summary>
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
