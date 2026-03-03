// Stub to satisfy compilation when the VIVE OpenXR package is not installed.
using System;
using UnityEngine;

namespace VIVE.OpenXR.CompositionLayer
{
    /// <summary>
    /// Stub for CompositionLayer — replace with the real VIVE OpenXR package if needed.
    /// </summary>
    public class CompositionLayer : MonoBehaviour
    {
        /// <summary>Returns the external surface object handle.</summary>
        public IntPtr GetExternalSurfaceObj() => IntPtr.Zero;
    }
}
