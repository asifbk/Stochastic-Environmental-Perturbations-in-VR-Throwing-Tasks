using RCG.Benchmark.SystemC;
using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — XR Hand Proximity Sensor (System C: Reactive Subject / UniRx pattern).
    ///
    /// Exposes a SimpleSubject that downstream components subscribe to.
    /// Subscribers receive an IDisposable they must Dispose() on cleanup —
    /// the managed allocation that System C measures vs System D's zero-alloc model.
    /// </summary>
    public sealed class XRHandProximitySensor_C : MonoBehaviour, IDataSource
    {
        public readonly SimpleSubject<float> ProximitySubject = new SimpleSubject<float>();

        private float _rawProximity = 100f;
        public float RawValue => _rawProximity;

        public void SetValue(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, 100f);
            if (Mathf.Approximately(_rawProximity, clamped)) return;
            _rawProximity = clamped;
            ProximitySubject.OnNext(_rawProximity);
        }
    }
}
