using UnityEngine;

namespace RCG.Benchmark.SystemB
{
    /// <summary>
    /// System B — Manual C# Events.
    /// Subscribes to B_Processor.OnNormalizedChanged and performs a string-format
    /// "render" operation — representative of real UI update work.
    /// </summary>
    public sealed class B_UIDisplay : MonoBehaviour, IMetricsProvider
    {
        private B_Processor _processor;
        private string      _displayText = "";
        private string      _lastText    = "";
        private int         _totalEvals;
        private int         _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
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
            _displayText = $"{Mathf.RoundToInt(normalized * 100):000}%";

            if (_displayText == _lastText) { _redundantEvals++; return; }
            _lastText = _displayText;
        }
    }
}
