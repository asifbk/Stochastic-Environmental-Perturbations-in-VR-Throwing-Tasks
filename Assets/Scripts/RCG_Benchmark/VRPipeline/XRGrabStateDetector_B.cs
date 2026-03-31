using System;
using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — Grab State Detector (System B: Manual C# Events).
    ///
    /// Subscribes to XRHandProximitySensor_B.OnProximityChanged and fires
    /// OnGripStrengthChanged when the derived grip strength value actually changes.
    ///
    /// Requires explicit OnDestroy() unsubscription. A missed unsubscription in an XR scene
    /// where interactable objects are dynamically destroyed leaves a dangling delegate on
    /// the sensor — in production this manifests as MissingReferenceException mid-session.
    /// </summary>
    public sealed class XRGrabStateDetector_B : MonoBehaviour, IMetricsProvider
    {
        public event Action<float> OnGripStrengthChanged;

        private XRHandProximitySensor_B _sensor;
        private float                   _lastGripStrength = -1f;
        private int                     _totalEvals;
        private int                     _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public float GripStrength       { get; private set; }
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(XRHandProximitySensor_B sensor)
        {
            _sensor = sensor;
            _sensor.OnProximityChanged += OnProximityChanged;
        }

        private void OnDestroy()
        {
            // Required to prevent dangling delegate leak when interactable is destroyed
            if (_sensor != null) _sensor.OnProximityChanged -= OnProximityChanged;
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
            OnGripStrengthChanged?.Invoke(gripStrength);
        }
    }
}
