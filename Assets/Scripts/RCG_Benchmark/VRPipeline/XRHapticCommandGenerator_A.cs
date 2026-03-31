using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — Haptic Command Generator (System A: Update polling).
    ///
    /// Generates a haptic pulse command when grip strength crosses the grab threshold
    /// (rising edge only). In a real XR app this would call
    /// XRBaseController.SendHapticImpulse() or the SenseGlove SDK haptic API.
    ///
    /// The threshold check runs every frame in polling mode even when the hand has not
    /// moved — demonstrating that the haptic pipeline re-evaluates its trigger condition
    /// at 90 Hz even though haptic events are sparse (low R).
    /// </summary>
    public sealed class XRHapticCommandGenerator_A : MonoBehaviour, IMetricsProvider
    {
        private const float GrabThreshold     = 0.25f;
        private const float HapticAmplitude   = 0.8f;
        private const float HapticDurationSec = 0.05f;

        private XRGrabStateDetector_A _detector;
        private bool                  _wasGrabbed;
        private int                   _hapticCommandCount;
        private int                   _totalEvals;
        private int                   _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;

        /// <summary>Number of haptic pulse commands issued this run.</summary>
        public int HapticCommandCount   => _hapticCommandCount;

        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(XRGrabStateDetector_A detector)
        {
            _detector        = detector;
            _detector._haptic = this;
        }

        /// <summary>
        /// Called every frame for ALL objects. Threshold re-evaluated even when
        /// the hand has not moved — the redundant work polling forces.
        /// </summary>
        public void ManualTick(float gripStrength)
        {
            _totalEvals++;
            bool isGrabbed = gripStrength > GrabThreshold;

            if (isGrabbed == _wasGrabbed)
            {
                _redundantEvals++;
                return;
            }

            // Rising edge: hand just closed on object
            if (isGrabbed)
            {
                _hapticCommandCount++;
                // Real XR: controller.SendHapticImpulse(HapticAmplitude, HapticDurationSec)
                // Omitted to isolate communication-layer cost
            }

            _wasGrabbed = isGrabbed;
        }
    }
}
