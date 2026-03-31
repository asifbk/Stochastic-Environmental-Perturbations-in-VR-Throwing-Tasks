using RCG.Core;
using UnityEngine;

namespace RCG.Benchmark.SystemD
{
    /// <summary>
    /// System D — Reactive Component Graph (RCG).
    /// Reacts to D_Processor.Normalized via [ReactsTo] — no Update(), no subscription code.
    /// OnNormalizedChanged() is only called when Normalized actually changed.
    /// </summary>
    public sealed class D_UIDisplay : RCGBehaviour, IMetricsProvider
    {
        private D_Processor _processor;
        private string      _displayText = "";
        private string      _lastText    = "";
        private int         _totalEvals;
        private int         _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(D_Processor processor)
        {
            _processor = processor;
        }

        [ReactsTo("_processor", "Normalized")]
        private void OnNormalizedChanged(float normalized)
        {
            _totalEvals++;
            _displayText = $"{Mathf.RoundToInt(normalized * 100):000}%";
            if (_displayText == _lastText) { _redundantEvals++; return; }
            _lastText = _displayText;
        }
    }
}
