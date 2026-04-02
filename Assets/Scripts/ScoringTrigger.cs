using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Detects when a basketball passes downward through the hoop's inner circle.
    /// Also detects rim hits (ball collides with the rim collider without scoring).
    /// Attach to a GameObject with a trigger collider centred at the rim opening.
    /// </summary>
    public class ScoringTrigger : MonoBehaviour
    {
        /// <summary>Minimum downward speed (m/s) required to register as a valid score.</summary>
        private const float DownwardVelocityThreshold = -0.5f;

        /// <summary>Only Rigidbodies whose root GameObject carries this tag are considered valid balls.</summary>
        [SerializeField] private string ballTag = "Ball";

        /// <summary>
        /// Assign the Basketball_Stand MeshCollider so rim hits can be detected via collision messages.
        /// The ScoringTrigger's GameObject must have a Rigidbody or forward messages from the rim collider.
        /// Alternatively leave null and use the OnRimHit event from external rim-collision detection.
        /// </summary>
        [Tooltip("The rim collider (Basketball_Stand MeshCollider). Used to detect rim-hit events.")]
        [SerializeField] private Collider rimCollider;

        /// <summary>Fired when a ball passes cleanly through the hoop. Args: ball GameObject, entry speed m/s, entry velocity vector.</summary>
        public event System.Action<GameObject, float, Vector3> OnScored;

        /// <summary>Fired when a ball hits the rim without scoring. Args: ball GameObject, impact speed m/s.</summary>
        public event System.Action<GameObject, float> OnRimHit;

        // Tracks balls that already scored this frame to avoid double-firing.
        private System.Collections.Generic.HashSet<GameObject> _scoredBalls
            = new System.Collections.Generic.HashSet<GameObject>();

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (rb == null || !rb.gameObject.CompareTag(ballTag)) return;
            if (rb.velocity.y > DownwardVelocityThreshold) return;

            float   entrySpeed    = rb.velocity.magnitude;
            Vector3 entryVelocity = rb.velocity;

            _scoredBalls.Add(rb.gameObject);
            Debug.Log($"[ScoringTrigger] SCORE! '{rb.gameObject.name}' at {entrySpeed:F2} m/s.");
            OnScored?.Invoke(rb.gameObject, entrySpeed, entryVelocity);

            // Clear the scored-ball record after a short delay so the same ball can score again next throw.
            StartCoroutine(ClearScoredBall(rb.gameObject));
        }

        /// <summary>
        /// Call this from an external collision handler on the rim collider (Basketball_Stand).
        /// Since MeshCollider is non-convex it cannot send its own OnCollisionEnter to this script,
        /// so BallRimCollisionReporter on Basketball_Stand forwards events here.
        /// </summary>
        public void ReportRimHit(GameObject ball, float impactSpeed)
        {
            if (_scoredBalls.Contains(ball)) return; // already scored — not a rim bounce
            Debug.Log($"[ScoringTrigger] RIM HIT by '{ball.name}' at {impactSpeed:F2} m/s.");
            OnRimHit?.Invoke(ball, impactSpeed);
        }

        private System.Collections.IEnumerator ClearScoredBall(GameObject ball)
        {
            yield return new WaitForSeconds(2f);
            _scoredBalls.Remove(ball);
        }
    }
}
