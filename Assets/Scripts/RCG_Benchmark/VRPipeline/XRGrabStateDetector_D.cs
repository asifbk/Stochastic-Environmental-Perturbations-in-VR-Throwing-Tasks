using RCG.Core;
using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — Grab State Detector (System D: Reactive Component Graph).
    ///
    /// [ReactsTo("_sensor", "ProximityValue")] wires OnProximityChanged() to
    /// XRHandProximitySensor_D.ProximityValue. RCGResolver calls this method only
    /// when ProximityValue actually changed — no polling, no subscription lifecycle.
    ///
    /// Setting GripStrength.Value marks it dirty, appending it to the same
    /// PropagateAll() traversal in correct topological order — XRProximityHUD_D
    /// and XRHapticCommandGenerator_D receive their values in the same frame.
    /// </summary>
    public sealed class XRGrabStateDetector_D : RCGBehaviour, IMetricsProvider
    {
        /// <summary>Derived observable. Downstream [ReactsTo] declarations reference this.</summary>
        public readonly Observable<float> GripStrength = new Observable<float>(0f);

        private XRHandProximitySensor_D _sensor;
        private float                   _lastGripStrength = -1f;
        private int                     _totalEvals;
        private int                     _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        /// <summary>Call before Start() fires so RCGBehaviour can resolve [ReactsTo].</summary>
        public void Initialize(XRHandProximitySensor_D sensor)
        {
            _sensor = sensor;
        }

        [ReactsTo("_sensor", "ProximityValue")]
        private void OnProximityChanged(float rawProximity)
        {
            _totalEvals++;
            float gripStrength = 1f - (rawProximity / 100f);

            if (Mathf.Approximately(gripStrength, _lastGripStrength))
            {
                _redundantEvals++;
                return;
            }

            _lastGripStrength    = gripStrength;
            GripStrength.Value   = gripStrength;
            // Setting GripStrength.Value marks it dirty → appended to RCGResolver dirty list
            // → XRProximityHUD_D and XRHapticCommandGenerator_D notified in the same PropagateAll() call
        }
    }
}
