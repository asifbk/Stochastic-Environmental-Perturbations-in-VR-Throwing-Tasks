using System;
using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — XR Hand Proximity Sensor (System B: Manual C# Events).
    ///
    /// Fires OnProximityChanged when the proximity value changes.
    /// Downstream components subscribe in Initialize() and must manually
    /// unsubscribe in OnDestroy() to avoid dangling delegate leaks —
    /// the exact memory-leak class this paper identifies as a VR production risk.
    /// </summary>
    public sealed class XRHandProximitySensor_B : MonoBehaviour, IDataSource
    {
        public event Action<float> OnProximityChanged;

        private float _rawProximity = 100f;
        public float RawValue => _rawProximity;

        public void SetValue(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, 100f);
            if (Mathf.Approximately(_rawProximity, clamped)) return;
            _rawProximity = clamped;
            OnProximityChanged?.Invoke(_rawProximity); // synchronous cascade
        }
    }
}
