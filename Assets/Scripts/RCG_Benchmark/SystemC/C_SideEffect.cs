using System;
using UnityEngine;

namespace RCG.Benchmark.SystemC
{
    /// <summary>
    /// System C — Reactive Subject (UniRx pattern).
    /// </summary>
    public sealed class C_SideEffect : MonoBehaviour, IMetricsProvider
    {
        private const float Threshold = 0.2f;

        private C_Processor _processor;
        private IDisposable _subscription;
        private bool        _wasBelow;
        private int         _triggerCount;
        private int         _totalEvals;
        private int         _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public int TriggerCount         => _triggerCount;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(C_Processor processor)
        {
            _processor    = processor;
            _subscription = _processor.NormalizedSubject.Subscribe(OnNormalizedChanged);
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
        }

        private void OnNormalizedChanged(float normalized)
        {
            _totalEvals++;
            bool isBelow = normalized < Threshold;
            if (isBelow == _wasBelow) { _redundantEvals++; return; }
            if (isBelow) _triggerCount++;
            _wasBelow = isBelow;
        }
    }
}
