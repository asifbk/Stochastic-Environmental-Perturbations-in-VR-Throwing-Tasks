using UnityEngine;
using SG.Util;

namespace Basketball
{
    /// <summary>
    /// Calls CalibrateIMU() on all assigned SG_IMUTracking components.
    /// Wire this to a UI Button's OnClick event.
    /// </summary>
    public class IMUResetButton : MonoBehaviour
    {
        [Tooltip("SG_IMUTracking on SGHand Right.")]
        public SG_IMUTracking rightHandIMU;

        [Tooltip("SG_IMUTracking on SGHand Left.")]
        public SG_IMUTracking leftHandIMU;

        /// <summary> Resets IMU calibration on both hands. </summary>
        public void ResetIMU()
        {
            if (rightHandIMU != null)
                rightHandIMU.CalibrateIMU();

            if (leftHandIMU != null)
                leftHandIMU.CalibrateIMU();

            Debug.Log("[IMUResetButton] IMU calibration reset for both hands.");
        }
    }
}
