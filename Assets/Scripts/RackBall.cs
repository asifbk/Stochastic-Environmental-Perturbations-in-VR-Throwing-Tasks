using System.Collections;
using UnityEngine;
using SG;

namespace Basketball
{
    /// <summary>
    /// Keeps a ball anchored to its rack slot on scene load so it does not fall off.
    /// When the player grabs the ball the anchor is released and normal physics apply.
    /// If the player releases the ball within <see cref="snapRadius"/> metres of the
    /// original slot position the ball snaps back and is re-anchored for the next throw.
    ///
    /// Uses <see cref="RigidbodyConstraints.FreezeAll"/> instead of
    /// <see cref="Rigidbody.isKinematic"/> so that SG_Grabable can always apply a
    /// release velocity without triggering the "Setting linear velocity of a kinematic
    /// body is not supported" error.
    /// </summary>
    [RequireComponent(typeof(SG_Grabable))]
    [RequireComponent(typeof(Rigidbody))]
    public class RackBall : MonoBehaviour
    {
        [Tooltip("Distance in metres from the rack slot centre within which releasing the ball " +
                 "snaps it back and re-anchors it. Increase if the player finds it hard to dock.")]
        [SerializeField] [Min(0f)] private float snapRadius = 0.3f;

        // ─── Private state ────────────────────────────────────────────────────────

        private Rigidbody   _rb;
        private SG_Grabable _grabable;

        /// <summary>Original local position relative to rack parent (set once in Awake).</summary>
        private Vector3    _anchorLocalPos;
        private Quaternion _anchorLocalRot;

        private bool _docked = true;

        // ─── Unity lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _rb       = GetComponent<Rigidbody>();
            _grabable = GetComponent<SG_Grabable>();

            // Record the rack-slot position once, before any physics can move the ball.
            _anchorLocalPos = transform.localPosition;
            _anchorLocalRot = transform.localRotation;
        }

        private void Start()
        {
            // Freeze using constraints rather than isKinematic.
            // SG_Grabable stores the isKinematic state at grab time; if the Rigidbody
            // were kinematic then, SG_Grabable would restore isKinematic=true on release
            // and then fail when trying to assign a release velocity to a kinematic body.
            // Keeping isKinematic=false at all times avoids that error.
            _rb.isKinematic  = false;
            _rb.constraints  = RigidbodyConstraints.FreezeAll;
            _rb.velocity        = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            _grabable.ObjectGrabbed.AddListener(OnGrabbed);
            _grabable.ObjectReleased.AddListener(OnReleased);
        }

        private void OnDestroy()
        {
            if (_grabable != null)
            {
                _grabable.ObjectGrabbed.RemoveListener(OnGrabbed);
                _grabable.ObjectReleased.RemoveListener(OnReleased);
            }
        }

        // ─── Event handlers ───────────────────────────────────────────────────────

        /// <summary>
        /// Called when SG_Grabable picks up the ball.
        /// Releases all constraints so SG_Grabable can move the ball via physics forces.
        /// </summary>
        private void OnGrabbed(SG_Interactable interactable, SG_GrabScript grabScript)
        {
            _docked          = false;
            _rb.constraints  = RigidbodyConstraints.None;
        }

        /// <summary>
        /// Called when the player releases the ball.
        /// Waits one frame so SG_Grabable finishes its own release processing first,
        /// then decides whether to snap back to the rack or leave as a free ball.
        /// </summary>
        private void OnReleased(SG_Interactable interactable, SG_GrabScript grabScript)
        {
            StartCoroutine(EvaluateSnapNextFrame());
        }

        // ─── Snap logic ───────────────────────────────────────────────────────────

        private IEnumerator EvaluateSnapNextFrame()
        {
            // One frame gap ensures SG_Grabable has finished restoring its physics defaults.
            yield return null;

            Vector3 anchorWorld = transform.parent != null
                ? transform.parent.TransformPoint(_anchorLocalPos)
                : _anchorLocalPos;

            if (Vector3.Distance(transform.position, anchorWorld) <= snapRadius)
                SnapToRack();
        }

        /// <summary>
        /// Teleports the ball back to its rack slot and re-freezes it.
        /// Can also be called externally to force a reset (e.g. level restart).
        /// </summary>
        public void SnapToRack()
        {
            _rb.velocity        = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.constraints     = RigidbodyConstraints.FreezeAll;
            transform.localPosition = _anchorLocalPos;
            transform.localRotation = _anchorLocalRot;
            _docked = true;
        }

        /// <summary>Whether the ball is currently docked in its rack slot.</summary>
        public bool IsDocked => _docked;
    }
}
