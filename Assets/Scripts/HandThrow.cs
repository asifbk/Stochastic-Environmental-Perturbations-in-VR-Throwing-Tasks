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

        [Header("Hand Physics Rigidbodies")]
        [Tooltip("Rigidbody on SGHand Right / PhysicsTrackingLayer")]
        [SerializeField] private Rigidbody rightHandRigidbody;
        [Tooltip("Rigidbody on SGHand Left / PhysicsTrackingLayer")]
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

        [Header("Game System")]
        [SerializeField] private BallThrower ballThrower;

        // Ring buffers storing recent hand velocities for stable release-velocity sampling.
        private readonly Vector3[] _rightHandVelocityHistory = new Vector3[VelocityHistorySize];
        private readonly Vector3[] _leftHandVelocityHistory  = new Vector3[VelocityHistorySize];
        private int _rightHistoryIndex;
        private int _leftHistoryIndex;

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
            // Sample hand velocities every frame to fill history buffers.
            SampleHandVelocity(rightHandRigidbody, _rightHandVelocityHistory, ref _rightHistoryIndex);
            SampleHandVelocity(leftHandRigidbody,  _leftHandVelocityHistory,  ref _leftHistoryIndex);

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

        /// <summary>Writes the current hand velocity into the ring buffer.</summary>
        private static void SampleHandVelocity(Rigidbody hand, Vector3[] buffer, ref int index)
        {
            if (hand == null) return;
            buffer[index] = hand.velocity;
            index = (index + 1) % VelocityHistorySize;
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
            HandleRelease(interactable, rightHandRigidbody, _rightHandVelocityHistory, HandSide.Right);
        }

        private void OnLeftHandReleased(SG_Interactable interactable, SG_GrabScript grabScript)
        {
            HandleRelease(interactable, leftHandRigidbody, _leftHandVelocityHistory, HandSide.Left);
        }

        /// <summary>
        /// Checks whether the released interactable is a basketball, applies smoothed release
        /// velocity + backspin, and records the shot.
        /// </summary>
        private void HandleRelease(SG_Interactable interactable, Rigidbody handRigidbody, Vector3[] velocityHistory, HandSide side)
        {
            if (interactable == null) return;

            Rigidbody ballRb = interactable.GetComponent<Rigidbody>();
            if (ballRb == null || !IsBall(ballRb)) return;

            // Use the peak velocity from recent history — preserves the full force of the throw gesture.
            Vector3 smoothedVelocity = handRigidbody != null
                ? PeakVelocity(velocityHistory)
                : ballRb.velocity;

            Vector3 releaseVelocity = smoothedVelocity * velocityMultiplier;

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
