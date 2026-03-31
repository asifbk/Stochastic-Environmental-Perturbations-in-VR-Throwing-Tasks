using RCG.Core;
using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — Haptic Command Generator (System D: Reactive Component Graph).
    ///
    /// [ReactsTo("_detector", "GripStrength")] — called only on grip strength change.
    /// No polling, no IDisposable, no OnDestroy() required.
    /// The haptic threshold check costs zero CPU when the hand has not moved.
    /// </summary>
    public sealed class XRHapticCommandGenerator_D : RCGBehaviour, IMetricsProvider
    {
        private const float GrabThreshold = 0.25f;

        private XRGrabStateDetector_D _detector;
        private bool                  _wasGrabbed;
        private int                   _hapticCommandCount;
        private int                   _totalEvals;
        private int                   _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public int HapticCommandCount   => _hapticCommandCount;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(XRGrabStateDetector_D detector)
        {
            _detector = detector;
        }

        [ReactsTo("_detector", "GripStrength")]
        private void OnGripStrengthChanged(float gripStrength)
        {
            _totalEvals++;
            bool isGrabbed = gripStrength > GrabThreshold;

            if (isGrabbed == _wasGrabbed)
            {
                _redundantEvals++;
                return;
            }

            if (isGrabbed) _hapticCommandCount++;
            _wasGrabbed = isGrabbed;
            // Real XR: controller.SendHapticImpulse(amplitude, duration)
        }
    }
}
