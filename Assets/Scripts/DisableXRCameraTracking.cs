using UnityEngine;
using UnityEngine.XR;

namespace Basketball
{
    /// <summary>
    /// Disables Unity's automatic XR head-tracking on the attached Camera.
    /// Prevents the XR subsystem from moving this camera to follow the HMD.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class DisableXRCameraTracking : MonoBehaviour
    {
        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            XRDevice.DisableAutoXRCameraTracking(_camera, true);
        }
    }
}
