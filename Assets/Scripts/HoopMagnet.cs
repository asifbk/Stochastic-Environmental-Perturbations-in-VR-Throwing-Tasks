using System.Collections.Generic;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Placed on the HoopScoreTrigger GameObject.
    /// When a basketball enters the attraction radius and is moving roughly downward,
    /// a continuous steering force guides it toward the hoop centre so it passes through cleanly.
    /// The ball's own physics (gravity, Rigidbody) remain fully active — no teleporting.
    /// </summary>
    public class HoopMagnet : MonoBehaviour
    {
        // ─── Constants ────────────────────────────────────────────────────────────

        /// <summary>Minimum downward velocity (m/s) before the magnet activates for a ball.</summary>
        private const float MinDownwardSpeed = 0.3f;

        // ─── Inspector ────────────────────────────────────────────────────────────

        [Header("Attraction Zone")]
        [Tooltip("Radius (metres) around the hoop centre within which balls are attracted.")]
        [SerializeField] [Range(0.1f, 1.5f)] private float attractionRadius = 0.6f;

        [Header("Steering Force")]
        [Tooltip("Strength of the lateral force steering the ball toward the hoop centre axis.")]
        [SerializeField] [Range(1f, 30f)] private float steeringForce = 12f;

        [Tooltip("Scales steering force up as the ball gets closer to the rim centre. Keeps far balls gentle, near balls precise.")]
        [SerializeField] private AnimationCurve forceByProximity = AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f);

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        // ─── Private State ────────────────────────────────────────────────────────

        private readonly List<Rigidbody> _attractedBalls = new List<Rigidbody>();

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void FixedUpdate()
        {
            if (_attractedBalls.Count == 0) return;

            Vector3 hoopCentre = transform.position;

            for (int i = _attractedBalls.Count - 1; i >= 0; i--)
            {
                Rigidbody rb = _attractedBalls[i];

                if (rb == null)
                {
                    _attractedBalls.RemoveAt(i);
                    continue;
                }

                // Stop steering once the ball has passed below the rim.
                if (rb.position.y < hoopCentre.y - 0.05f)
                {
                    _attractedBalls.RemoveAt(i);
                    continue;
                }

                // Only steer if the ball is still moving downward.
                if (rb.velocity.y > -MinDownwardSpeed)
                    continue;

                // Project the hoop centre and ball position onto the horizontal plane.
                Vector3 ballHorizontal = new Vector3(rb.position.x, 0f, rb.position.z);
                Vector3 hoopHorizontal = new Vector3(hoopCentre.x,  0f, hoopCentre.z);

                Vector3 lateralOffset = hoopHorizontal - ballHorizontal;
                float   lateralDist   = lateralOffset.magnitude;

                if (lateralDist < 0.001f) continue;

                // Proximity t: 0 = at edge of attraction radius, 1 = directly over hoop.
                float t = 1f - Mathf.Clamp01(lateralDist / attractionRadius);
                float scaledForce = steeringForce * forceByProximity.Evaluate(t);

                // Apply a lateral impulse toward the hoop axis.
                rb.AddForce(lateralOffset.normalized * scaledForce, ForceMode.Acceleration);
            }
        }

        // ─── Trigger Detection ────────────────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (rb == null || _attractedBalls.Contains(rb)) return;

            // Only attract balls that are already descending.
            if (rb.velocity.y > -MinDownwardSpeed) return;

            _attractedBalls.Add(rb);
        }

        private void OnTriggerExit(Collider other)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (rb != null) _attractedBalls.Remove(rb);
        }

        // ─── Gizmos ───────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.25f);
            Gizmos.DrawSphere(transform.position, attractionRadius);
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, attractionRadius);
        }
#endif
    }
}
