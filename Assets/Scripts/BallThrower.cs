using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Basketball
{
    /// <summary>
    /// Manages all basketballs in the scene.
    /// Press P at runtime to instantly reset every ball to its starting position and rotation.
    /// Exposes RecordShot() so HandThrow can keep the scoreboard in sync.
    /// </summary>
    public class BallThrower : MonoBehaviour
    {
        [Header("Balls")]
        [Tooltip("Assign all basketball Rigidbodies here.")]
        [SerializeField] private Rigidbody[] ballRigidbodies;

        [Header("Reset")]
        [Tooltip("All balls teleport to this point when P is pressed. If unassigned, balls return to their Start() positions.")]
        [SerializeField] private Transform resetPoint;
        [Tooltip("Horizontal spacing between balls when reset to the same point (metres).")]
        [SerializeField] private float resetSpacing = 0.4f;

        // Snapshots of each ball's position and rotation taken at Start().
        private Vector3[]    _initialPositions;
        private Quaternion[] _initialRotations;

        /// <summary>Total number of throw attempts since the session started.</summary>
        public int TotalShots { get; private set; }

        /// <summary>Fired immediately after RecordShot is called.</summary>
        public event System.Action OnShotFired;

        private void Start()
        {
            if (ballRigidbodies == null) return;

            _initialPositions = new Vector3[ballRigidbodies.Length];
            _initialRotations = new Quaternion[ballRigidbodies.Length];

            for (int i = 0; i < ballRigidbodies.Length; i++)
            {
                if (ballRigidbodies[i] == null) continue;
                _initialPositions[i] = ballRigidbodies[i].transform.position;
                _initialRotations[i] = ballRigidbodies[i].transform.rotation;
            }
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
                ResetAllBalls();
#else
            if (Input.GetKeyDown(KeyCode.P))
                ResetAllBalls();
#endif
        }

        /// <summary>
        /// Teleports every ball back to resetPoint (side-by-side) or to their Start() positions if no point is set.
        /// All momentum is zeroed.
        /// </summary>
        public void ResetAllBalls()
        {
            if (ballRigidbodies == null) return;

            for (int i = 0; i < ballRigidbodies.Length; i++)
            {
                Rigidbody rb = ballRigidbodies[i];
                if (rb == null) continue;

                rb.velocity        = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                Vector3    targetPos = _initialPositions[i];
                Quaternion targetRot = _initialRotations[i];

                if (resetPoint != null)
                {
                    float offset = (i - (ballRigidbodies.Length - 1) * 0.5f) * resetSpacing;
                    targetPos = resetPoint.position + resetPoint.right * offset;
                    targetRot = resetPoint.rotation;
                }

                // Use rb.position (not transform.position) so Unity correctly
                // resets the interpolation buffer on interpolated Rigidbodies.
                rb.position = targetPos;
                rb.rotation = targetRot;
            }

            Debug.Log("[BallThrower] All balls reset.");
        }

        /// <summary>
        /// Increments the shot counter and fires OnShotFired.
        /// Called by HandThrow when the player releases a ball.
        /// </summary>
        public void RecordShot()
        {
            TotalShots++;
            OnShotFired?.Invoke();
        }
    }
}
