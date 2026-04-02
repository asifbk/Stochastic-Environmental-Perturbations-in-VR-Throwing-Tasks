using System.Collections.Generic;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Placed on the HoopScoreTrigger GameObject.
    /// When a basketball tagged "Ball" enters the attraction zone it is continuously steered
    /// toward the hoop centre axis so it passes through cleanly.
    /// Activation no longer requires the ball to already be descending — any in-flight ball
    /// inside the radius is guided in, making it easier for user-study participants to score.
    /// The ball's own physics (gravity, Rigidbody) remain fully active — no teleporting.
    /// </summary>
    public class HoopMagnet : MonoBehaviour
    {
        // ─── Constants ────────────────────────────────────────────────────────────

        private const string BallTag = "Ball";

        // ─── Inspector ────────────────────────────────────────────────────────────

        [Header("Target")]
        [Tooltip("Override the attraction centre. Assign Basketball_Net for precise rim-centre targeting. " +
                 "Leave empty to use this GameObject's own position.")]
        [SerializeField] private Transform hoopTarget;

        [Header("Attraction Zone")]
        [Tooltip("Radius (metres) around the hoop centre within which balls are attracted.")]
        [SerializeField] [Range(0.1f, 3f)] private float attractionRadius = 1.2f;

        [Tooltip("Only attract balls that have risen at least this many metres above the hoop centre. " +
                 "Prevents the magnet from grabbing the ball while it is still in the player's hand. " +
                 "Set to 0 to disable the height gate.")]
        [SerializeField] [Range(0f, 2f)] private float minHeightAboveHoop = 0.1f;

        [Header("Steering Force")]
        [Tooltip("Peak lateral steering force (m/s²) applied toward the hoop axis.")]
        [SerializeField] [Range(1f, 60f)] private float steeringForce = 22f;

        [Tooltip("Vertical downward assist applied while the ball is above the hoop and inside the radius. " +
                 "Helps a slow-lofted ball tip over and fall through.")]
        [SerializeField] [Range(0f, 20f)] private float downwardAssist = 6f;

        [Tooltip("Scales both forces by proximity. X axis = 0 at radius edge, 1 at hoop centre. " +
                 "Y axis = force multiplier.")]
        [SerializeField] private AnimationCurve forceByProximity = AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f);

        [Header("Speed Damping")]
        [Tooltip("Maximum lateral speed (m/s) the ball is allowed to carry while inside the zone. " +
                 "Excess horizontal velocity is gently damped so the magnet can take effect. " +
                 "Set to a large value to disable damping.")]
        [SerializeField] [Range(1f, 20f)] private float maxLateralSpeed = 6f;

        [Tooltip("How strongly lateral speed above maxLateralSpeed is damped each physics step. 0 = no damping.")]
        [SerializeField] [Range(0f, 1f)] private float lateralDampingStrength = 0.08f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        // ─── Private State ────────────────────────────────────────────────────────

        private readonly List<Rigidbody> _attractedBalls = new List<Rigidbody>();

        // ─── Properties ───────────────────────────────────────────────────────────

        /// <summary>World-space position of the hoop centre used for all steering calculations.</summary>
        private Vector3 HoopCentre => hoopTarget != null ? hoopTarget.position : transform.position;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void FixedUpdate()
        {
            if (_attractedBalls.Count == 0) return;

            Vector3 hoopCentre = HoopCentre;

            for (int i = _attractedBalls.Count - 1; i >= 0; i--)
            {
                Rigidbody rb = _attractedBalls[i];

                if (rb == null)
                {
                    _attractedBalls.RemoveAt(i);
                    continue;
                }

                // Stop steering once ball passes well below the rim.
                if (rb.position.y < hoopCentre.y - 0.15f)
                {
                    _attractedBalls.RemoveAt(i);
                    continue;
                }

                // Height gate — don't attract while ball is still near hand level.
                if (minHeightAboveHoop > 0f && rb.position.y < hoopCentre.y + minHeightAboveHoop)
                    continue;

                // Lateral offset from ball to hoop axis (horizontal plane only).
                Vector3 ballH  = new Vector3(rb.position.x, 0f, rb.position.z);
                Vector3 hoopH  = new Vector3(hoopCentre.x,  0f, hoopCentre.z);
                Vector3 offset = hoopH - ballH;
                float   dist   = offset.magnitude;

                if (dist < 0.001f) continue;

                // Proximity ramp: 0 at radius edge → 1 at hoop axis.
                float t          = 1f - Mathf.Clamp01(dist / attractionRadius);
                float forceScale = forceByProximity.Evaluate(t);

                // ── Lateral steering ──────────────────────────────────────────────
                rb.AddForce(offset.normalized * steeringForce * forceScale, ForceMode.Acceleration);

                // ── Downward assist (above hoop only) ─────────────────────────────
                if (rb.position.y > hoopCentre.y && downwardAssist > 0f)
                    rb.AddForce(Vector3.down * downwardAssist * forceScale, ForceMode.Acceleration);

                // ── Lateral speed cap ─────────────────────────────────────────────
                if (lateralDampingStrength > 0f)
                {
                    Vector3 vel        = rb.velocity;
                    Vector3 lateralVel = new Vector3(vel.x, 0f, vel.z);
                    float   lateralSpd = lateralVel.magnitude;

                    if (lateralSpd > maxLateralSpeed)
                    {
                        Vector3 excessLateral = lateralVel - lateralVel.normalized * maxLateralSpeed;
                        rb.velocity -= excessLateral * lateralDampingStrength;
                    }
                }
            }
        }

        // ─── Trigger Detection ────────────────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(BallTag)) return;

            Rigidbody rb = other.attachedRigidbody;
            if (rb == null || rb.isKinematic || _attractedBalls.Contains(rb)) return;

            _attractedBalls.Add(rb);
            Debug.Log($"[HoopMagnet] Attracting '{rb.name}'.");
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

            Vector3 centre = HoopCentre;

            Gizmos.color = new Color(0f, 1f, 0.4f, 0.15f);
            Gizmos.DrawSphere(centre, attractionRadius);

            Gizmos.color = new Color(0f, 1f, 0.4f, 0.85f);
            Gizmos.DrawWireSphere(centre, attractionRadius);

            // Draw hoop axis line.
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(centre + Vector3.up * 0.5f, centre + Vector3.down * 0.5f);
        }
#endif
    }
}
