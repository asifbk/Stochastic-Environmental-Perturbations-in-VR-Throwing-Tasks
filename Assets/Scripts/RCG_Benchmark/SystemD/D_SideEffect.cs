using RCG.Core;
using UnityEngine;

namespace RCG.Benchmark.SystemD
{
    /// <summary>
    /// System D — Reactive Component Graph (RCG).
    /// Reacts to D_Processor.Normalized via [ReactsTo].
    /// Invoked only when Normalized changes — never runs redundantly.
    /// </summary>
    public sealed class D_SideEffect : RCGBehaviour, IMetricsProvider
    {
        private const float Threshold = 0.2f;

        private D_Processor _processor;
        private bool        _wasBelow;
        private int         _triggerCount;
        private int         _totalEvals;
        private int         _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public int TriggerCount         => _triggerCount;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(D_Processor processor)
        {
            _processor = processor;
        }

        [ReactsTo("_processor", "Normalized")]
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
