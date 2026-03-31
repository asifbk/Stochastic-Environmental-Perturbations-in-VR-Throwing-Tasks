using System;
using RCG.Benchmark.SystemC;
using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — Grab State Detector (System C: Reactive Subject / UniRx pattern).
    ///
    /// Subscribes to XRHandProximitySensor_C.ProximitySubject.
    /// Exposes its own GripStrengthSubject for downstream components to subscribe to.
    /// Holds an IDisposable from its upstream subscription — must be disposed to avoid leaks.
    /// </summary>
    public sealed class XRGrabStateDetector_C : MonoBehaviour, IMetricsProvider
    {
        public readonly SimpleSubject<float> GripStrengthSubject = new SimpleSubject<float>();

        private XRHandProximitySensor_C _sensor;
        private IDisposable             _subscription;
        private float                   _lastGripStrength = -1f;
        private int                     _totalEvals;
        private int                     _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public float GripStrength       { get; private set; }
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(XRHandProximitySensor_C sensor)
        {
            _sensor       = sensor;
            _subscription = _sensor.ProximitySubject.Subscribe(OnProximityChanged);
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
        }

        private void OnProximityChanged(float rawProximity)
        {
            _totalEvals++;
            float gripStrength = 1f - (rawProximity / 100f);

            if (Mathf.Approximately(gripStrength, _lastGripStrength))
            {
                _redundantEvals++;
                return;
            }

            _lastGripStrength = gripStrength;
            GripStrength      = gripStrength;
            GripStrengthSubject.OnNext(gripStrength);
        }
    }
}
