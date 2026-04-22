using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Forces the XR tracking origin to floor level on startup so that the HMD pose
/// drives only the Camera child transform, not the [CameraRig] root.
/// </summary>
public class XRTrackingOriginSetup : MonoBehaviour
{
    private void Start()
    {
        var inputSubsystems = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(inputSubsystems);

        foreach (XRInputSubsystem subsystem in inputSubsystems)
        {
            bool success = subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);

            if (success)
                Debug.Log("[XRTrackingOriginSetup] Tracking origin set to Floor.");
            else
                Debug.LogWarning("[XRTrackingOriginSetup] Failed to set tracking origin to Floor on: " + subsystem.SubsystemDescriptor.id);
        }
    }
}
