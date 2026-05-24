using UnityEngine;

namespace SG
{
    /// <summary>
    /// Copies only the world-space position from a tracking target onto this GameObject's Transform every Update.
    /// Used alongside SG_IMUTracking which independently handles rotation.
    /// </summary>
    public class SG_PositionTracking : MonoBehaviour
    {
        /// <summary> The Transform whose world-space position is copied each frame. </summary>
        public Transform trackingTarget;

        private void Update()
        {
            if (trackingTarget != null)
            {
                transform.position = trackingTarget.position;
            }
        }
    }
}
