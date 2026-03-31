using UnityEngine;

namespace RCG.Benchmark.SystemA
{
    /// <summary>
    /// System A — Update() Polling.
    /// Reads DataSource.RawValue every ManualTick(), normalises it, then drives
    /// UIDisplay and SideEffect manually — reproducing the Update() chain
    /// that every developer writes when polling-based design is used.
    /// </summary>
    public sealed class A_Processor : MonoBehaviour, IMetricsProvider, IManualTickable
    {
        private A_DataSource _source;
        internal A_UIDisplay  _uiDisplay;
        internal A_SideEffect _sideEffect;

        private float _lastNormalized = -1f;
        private int   _totalEvals;
        private int   _redundantEvals;

        public float Normalized { get; private set; }

        // ── IMetricsProvider ─────────────────────────────────────────────────
        public int TotalEvaluations    => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public void ResetMetrics()     { _totalEvals = 0; _redundantEvals = 0; }

        // ── Setup ─────────────────────────────────────────────────────────────
        public void Initialize(A_DataSource source)
        {
            _source = source;
        }

        // ── IManualTickable ───────────────────────────────────────────────────
        /// <summary>
        /// Called by BenchmarkController every frame for ALL entities (not just dirty ones).
        /// This is the defining characteristic of polling: every component checks every frame.
        /// </summary>
        public void ManualTick()
        {
            _totalEvals++;

            float normalized = _source.RawValue / 100f;

            // Redundant if output is identical to last frame.
            if (Mathf.Approximately(normalized, _lastNormalized))
            {
                _redundantEvals++;
                // Polling still drives downstream even when value is unchanged
                // because it cannot know whether the downstream read the value yet.
                _uiDisplay?.ManualTick(normalized);
                _sideEffect?.ManualTick(normalized);
                return;
            }

            _lastNormalized = normalized;
            Normalized      = normalized;
            _uiDisplay?.ManualTick(normalized);
            _sideEffect?.ManualTick(normalized);
        }
    }
}
