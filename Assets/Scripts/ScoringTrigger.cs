using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Detects when a basketball passes downward through the hoop's inner circle.
    /// Attach to a GameObject with a trigger collider centred at the rim opening.
    /// </summary>
    public class ScoringTrigger : MonoBehaviour
    {
        /// <summary>
        /// Minimum downward speed (m/s) required to register as a valid score.
        /// Prevents counting a ball that barely grazes the trigger while rising.
        /// </summary>
        private const float DownwardVelocityThreshold = -0.5f;

        /// <summary>
        /// Fired when a ball passes through the hoop.
        /// First argument is the scoring ball's GameObject, second is its entry speed in m/s.
        /// </summary>
        public event System.Action<GameObject, float> OnScored;

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (rb == null)
            {
                return;
            }

            if (rb.velocity.y > DownwardVelocityThreshold)
            {
                return;
            }

            float entrySpeed = rb.velocity.magnitude;
            Debug.Log($"[ScoringTrigger] SCORE! '{other.gameObject.name}' passed through at {entrySpeed:F2} m/s.");
            OnScored?.Invoke(other.gameObject, entrySpeed);
        }
    }
}
