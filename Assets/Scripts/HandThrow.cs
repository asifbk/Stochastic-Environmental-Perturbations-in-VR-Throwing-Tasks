using System.Collections.Generic;
using SG;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Bridges SenseGlove hand release events to the basketball throwing pipeline.
    /// Detects when the player releases a ball from either hand and records the throw.
    /// Requires SG_Grabable on each ball and SG_PhysicsGrab on each SGHand.
    /// </summary>
    public class HandThrow : MonoBehaviour
    {
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

        [Header("Game System")]
        [SerializeField] private BallThrower ballThrower;

        // Tracks last time each ball moved, for auto-reset
        private readonly Dictionary<Rigidbody, float> _lastMovedTime = new Dictionary<Rigidbody, float>();
        private static readonly float VelocitySleepThreshold = 0.05f;

        /// <summary>
        /// Fired when the player releases a ball. Carries the releasing hand's velocity and which hand was used.
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
            if (ballRigidbodies == null) return;

            foreach (Rigidbody rb in ballRigidbodies)
            {
                if (rb == null || rb.isKinematic) continue;

                // Track last time ball was moving
                if (rb.velocity.magnitude > VelocitySleepThreshold)
                    _lastMovedTime[rb] = Time.time;

                // Out-of-bounds reset
                if (ballSpawnPoint != null && rb.transform.position.y < outOfBoundsYThreshold)
                {
                    ResetBall(rb);
                    continue;
                }

                // Auto-reset after idle
                if (autoResetAfterSeconds > 0f
                    && _lastMovedTime.TryGetValue(rb, out float lastMoved)
                    && Time.time - lastMoved > autoResetAfterSeconds
                    && ballSpawnPoint != null)
                {
                    ResetBall(rb);
                }
            }
        }

        private void OnRightHandReleased(SG_Interactable interactable, SG_GrabScript grabScript)
        {
            HandleRelease(interactable, rightHandRigidbody, HandSide.Right);
        }

        private void OnLeftHandReleased(SG_Interactable interactable, SG_GrabScript grabScript)
        {
            HandleRelease(interactable, leftHandRigidbody, HandSide.Left);
        }

        /// <summary>
        /// Checks whether the released interactable is a basketball and records the shot.
        /// </summary>
        private void HandleRelease(SG_Interactable interactable, Rigidbody handRigidbody, HandSide side)
        {
            if (interactable == null) return;

            Rigidbody ballRb = interactable.GetComponent<Rigidbody>();
            if (ballRb == null || !IsBall(ballRb)) return;

            // The hand Rigidbody carries the wrist velocity at the moment of release.
            Vector3 releaseVelocity = handRigidbody != null ? handRigidbody.velocity : ballRb.velocity;

            if (ballThrower != null)
                ballThrower.RecordShot();

            OnBallReleased?.Invoke(releaseVelocity, side);
            _lastMovedTime[ballRb] = Time.time;

            Debug.Log($"[HandThrow] Ball released by {side} hand at {releaseVelocity.magnitude:F2} m/s.");
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
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.transform.position = ballSpawnPoint.position;
            rb.transform.rotation = Quaternion.identity;
            _lastMovedTime[rb] = Time.time;
        }
    }
}
