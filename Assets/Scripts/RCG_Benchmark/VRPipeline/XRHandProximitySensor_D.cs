using RCG.Core;
using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — XR Hand Proximity Sensor (System D: Reactive Component Graph).
    ///
    /// Exposes an Observable[float] field. Setting its Value marks it dirty and
    /// registers it with RCGResolver — zero allocation, zero per-frame cost until
    /// RCGResolver.PropagateAll() is called by VRBenchmarkController.
    ///
    /// Downstream components declare [ReactsTo("_sensor", "ProximityValue")] —
    /// no manual subscription, no IDisposable, no OnDestroy() boilerplate required.
    /// </summary>
    public sealed class XRHandProximitySensor_D : MonoBehaviour, IDataSource
    {
        /// <summary>
        /// Observable hand proximity. Downstream [ReactsTo] declarations reference
        /// this field by name.
        /// </summary>
        public readonly Observable<float> ProximityValue = new Observable<float>(100f);

        public float RawValue => ProximityValue.Value;

        public void SetValue(float value)
        {
            ProximityValue.Value = Mathf.Clamp(value, 0f, 100f);
            // Marks dirty → enqueued in RCGResolver._dirtyNodes.
            // No cascade until PropagateAll() is called.
        }
    }
}
