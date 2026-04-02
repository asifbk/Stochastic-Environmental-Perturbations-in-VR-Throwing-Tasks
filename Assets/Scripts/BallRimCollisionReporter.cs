using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Attach to Basketball_Stand (which owns the rim MeshCollider).
    /// Forwards ball collision events to ScoringTrigger so rim-hit outcomes can be logged.
    /// Non-convex MeshColliders cannot call OnTriggerEnter themselves, so this bridge is needed.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BallRimCollisionReporter : MonoBehaviour
    {
        [Tooltip("The ScoringTrigger on HoopScoreTrigger that receives rim-hit reports.")]
        [SerializeField] private ScoringTrigger scoringTrigger;

        [Tooltip("Only GameObjects with this tag are treated as balls.")]
        [SerializeField] private string ballTag = "Ball";

        /// <summary>Minimum impact speed (m/s) to register as a meaningful rim hit.</summary>
        private const float MinImpactSpeed = 0.5f;

        private void OnCollisionEnter(Collision collision)
        {
            if (scoringTrigger == null) return;
            if (!collision.gameObject.CompareTag(ballTag)) return;

            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed < MinImpactSpeed) return;

            scoringTrigger.ReportRimHit(collision.gameObject, impactSpeed);
        }
    }
}
