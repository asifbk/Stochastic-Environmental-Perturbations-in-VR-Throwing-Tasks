//======= Copyright (c) Valve Corporation, All rights reserved. ===============
//
// Purpose: For controlling in-game objects with tracked devices, with scaling support.
//
//=============================================================================

using UnityEngine;
using Valve.VR;

namespace Valve.VR
{
    public class SteamVR_TrackedObject : MonoBehaviour
    {
        public enum EIndex
        {
            None = -1,
            Hmd = (int)OpenVR.k_unTrackedDeviceIndex_Hmd,
            Device1,
            Device2,
            Device3,
            Device4,
            Device5,
            Device6,
            Device7,
            Device8,
            Device9,
            Device10,
            Device11,
            Device12,
            Device13,
            Device14,
            Device15,
            Device16
        }

        [Header("Device Settings")]
        public EIndex index;

        [Tooltip("If not set, relative to parent")]
        public Transform origin;

        [Header("Transform Scaling")]
        [Tooltip("Amplifies or reduces movement in Unity space. 1.0 = normal, 2.0 = double movement, 0.5 = half movement.")]
        public float movementScale = 6.0f;

        [Tooltip("Optional offset applied after scaling (in local space).")]
        public Vector3 positionOffset = Vector3.zero;

        public bool isValid { get; private set; }

        private void OnNewPoses(TrackedDevicePose_t[] poses)
        {
            if (index == EIndex.None)
                return;

            var i = (int)index;

            isValid = false;
            if (poses.Length <= i)
                return;

            if (!poses[i].bDeviceIsConnected)
                return;

            if (!poses[i].bPoseIsValid)
                return;

            isValid = true;

            var pose = new SteamVR_Utils.RigidTransform(poses[i].mDeviceToAbsoluteTracking);

            // Apply movement scaling and offset
            Vector3 scaledPos = pose.pos * movementScale + positionOffset;

            if (origin != null)
            {
                transform.position = origin.TransformPoint(scaledPos);
                // transform.rotation = origin.rotation * pose.rot;


                // offset the rotation alwas by 90 degrees on X to make the controller face forward
                // transform.rotation = origin.rotation * pose.rot * Quaternion.Euler(90, 0, 0);
            }
            else
            {
                transform.localPosition = scaledPos;
                // transform.localRotation = pose.rot;
            }
        }

        SteamVR_Events.Action newPosesAction;

        SteamVR_TrackedObject()
        {
            newPosesAction = SteamVR_Events.NewPosesAction(OnNewPoses);
        }

        private void Awake()
        {
            OnEnable();
        }

        void OnEnable()
        {
            var render = SteamVR_Render.instance;
            if (render == null)
            {
                enabled = false;
                return;
            }

            newPosesAction.enabled = true;
        }

        void OnDisable()
        {
            newPosesAction.enabled = false;
            isValid = false;
        }

        public void SetDeviceIndex(int index)
        {
            if (System.Enum.IsDefined(typeof(EIndex), index))
                this.index = (EIndex)index;
        }
    }
}
