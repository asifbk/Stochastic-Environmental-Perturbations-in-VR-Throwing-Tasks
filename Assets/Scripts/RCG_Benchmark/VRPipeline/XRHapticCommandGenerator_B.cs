using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — Haptic Command Generator (System B: Manual C# Events).
    ///
    /// Subscribes to XRGrabStateDetector_B.OnGripStrengthChanged and fires
    /// a haptic command on grab threshold rising edge. Requires manual
    /// unsubscription to prevent delegate leaks on interactable destruction.
    /// </summary>
    public sealed class XRHapticCommandGenerator_B : MonoBehaviour, IMetricsProvider
    {
        private const float GrabThreshold = 0.25f;

        private XRGrabStateDetector_B _detector;
        private bool                  _wasGrabbed;
        private int                   _hapticCommandCount;
        private int                   _totalEvals;
        private int                   _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public int HapticCommandCount   => _hapticCommandCount;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(XRGrabStateDetector_B detector)
        {
            _detector = detector;
            _detector.OnGripStrengthChanged += OnGripStrengthChanged;
        }

        private void OnDestroy()
        {
            if (_detector != null) _detector.OnGripStrengthChanged -= OnGripStrengthChanged;
        }

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
