using UnityEngine;

namespace RCG.Benchmark.SystemB
{
    /// <summary>
    /// System B — Manual C# Events.
    /// Subscribes to B_Processor.OnNormalizedChanged and detects threshold crossings.
    /// </summary>
    public sealed class B_SideEffect : MonoBehaviour, IMetricsProvider
    {
        private const float Threshold = 0.2f;

        private B_Processor _processor;
        private bool        _wasBelow;
        private int         _triggerCount;
        private int         _totalEvals;
        private int         _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public int TriggerCount         => _triggerCount;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(B_Processor processor)
        {
            _processor = processor;
            _processor.OnNormalizedChanged += OnNormalizedChanged;
        }

        private void OnDestroy()
        {
            if (_processor != null) _processor.OnNormalizedChanged -= OnNormalizedChanged;
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
