using UnityEngine;

namespace RCG.Benchmark.SystemA
{
    /// <summary>
    /// System A — Update() Polling.
    /// Simulates a UI text update: formats a string and stores it locally.
    /// The string allocation (format + concat) is the representative "UI work"
    /// and is intentional — it is the GC cost being measured.
    /// </summary>
    public sealed class A_UIDisplay : MonoBehaviour, IMetricsProvider
    {
        private A_Processor _processor;
        private string _displayText  = "";
        private string _lastText     = "";
        private int    _totalEvals;
        private int    _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(A_Processor processor)
        {
            _processor = processor;
            _processor._uiDisplay = this;
        }

        /// <summary>Called by A_Processor.ManualTick() every frame.</summary>
        public void ManualTick(float normalized)
        {
            _totalEvals++;
            _displayText = $"{Mathf.RoundToInt(normalized * 100):000}%";

            if (_displayText == _lastText)
            {
                _redundantEvals++;
                return;
            }
            _lastText = _displayText;
            // Real game: assign to TextMeshPro.text (omitted to isolate logic cost)
        }
    }
}
