using UnityEngine;
using Valve.VR;

namespace SG
{
    /// <summary> Logs all active Vive Tracker device indices to the console every second. Remove after identifying the correct index. </summary>
    public class TrackerDebug : MonoBehaviour
    {
        private const float LogInterval = 1f;
        private float _timer;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < LogInterval)
                return;

            _timer = 0f;
            LogTrackerIndices();
        }

        private void LogTrackerIndices()
        {
            if (OpenVR.System == null)
            {
                Debug.LogWarning("[TrackerDebug] OpenVR.System is not available. Is SteamVR running?");
                return;
            }

            bool found = false;
            for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
            {
                ETrackedDeviceClass deviceClass = OpenVR.System.GetTrackedDeviceClass(i);
                if (deviceClass == ETrackedDeviceClass.GenericTracker)
                {
                    Debug.Log($"[TrackerDebug] Vive Tracker found at Device Index: {i} → set SteamVR_TrackedObject.Index to Device{i}");
                    found = true;
                }
            }

            if (!found)
                Debug.LogWarning("[TrackerDebug] No Vive Trackers detected. Check SteamVR pairing.");
        }
    }
}
