using System;
using UnityEngine;

namespace RCG.Benchmark.SystemC
{
    /// <summary>
    /// System C — Reactive Subject (UniRx pattern).
    /// </summary>
    public sealed class C_UIDisplay : MonoBehaviour, IMetricsProvider
    {
        private C_Processor _processor;
        private IDisposable _subscription;
        private string      _displayText = "";
        private string      _lastText    = "";
        private int         _totalEvals;
        private int         _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
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
            _displayText = $"{Mathf.RoundToInt(normalized * 100):000}%";
            if (_displayText == _lastText) { _redundantEvals++; return; }
            _lastText = _displayText;
        }
    }
}
