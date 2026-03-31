using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — XR Hand Proximity Sensor (System A: Update polling).
    ///
    /// Simulates raw hand-tracking output for one interactable prop:
    /// a normalised distance value (0 = contact, 100 = out of reach).
    /// In a real XR app this value comes from XRInteractionManager / SDK.
    ///
    /// System A: no notification. Downstream components poll RawValue every frame
    /// in ManualTick() — regardless of whether the hand actually moved.
    /// </summary>
    public sealed class XRHandProximitySensor : MonoBehaviour, IDataSource
    {
        private float _rawProximity = 100f;

        /// <summary>Raw hand-to-object distance (0 = contact, 100 = out of reach).</summary>
        public float RawValue => _rawProximity;

        /// <summary>
        /// Called by VRBenchmarkController on the R% of objects whose proximity changed this frame.
        /// System A: stores value only — no downstream notification.
        /// </summary>
        public void SetValue(float value)
        {
            _rawProximity = Mathf.Clamp(value, 0f, 100f);
        }
    }
}
