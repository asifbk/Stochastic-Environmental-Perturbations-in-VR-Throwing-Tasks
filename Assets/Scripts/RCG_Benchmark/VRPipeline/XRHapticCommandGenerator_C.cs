using System;
using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — Haptic Command Generator (System C: Reactive Subject / UniRx pattern).
    /// </summary>
    public sealed class XRHapticCommandGenerator_C : MonoBehaviour, IMetricsProvider
    {
        private const float GrabThreshold = 0.25f;

        private XRGrabStateDetector_C _detector;
        private IDisposable           _subscription;
        private bool                  _wasGrabbed;
        private int                   _hapticCommandCount;
        private int                   _totalEvals;
        private int                   _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public int HapticCommandCount   => _hapticCommandCount;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(XRGrabStateDetector_C detector)
        {
            _detector     = detector;
            _subscription = _detector.GripStrengthSubject.Subscribe(OnGripStrengthChanged);
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
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
        }
    }
}
