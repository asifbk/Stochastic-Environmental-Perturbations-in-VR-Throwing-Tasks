using RCG.Core;
using UnityEngine;

namespace RCG.Benchmark.SystemD
{
    /// <summary>
    /// System D — Reactive Component Graph (RCG).
    ///
    /// [ReactsTo("_source", "Value")] wires OnValueChanged() to D_DataSource.Value.
    /// When RCGResolver.PropagateAll() is called, it invokes OnValueChanged() only
    /// if D_DataSource.Value actually changed — no polling, no manual subscription.
    ///
    /// OnValueChanged() sets Normalized.Value, which marks Normalized dirty and
    /// registers it in the same PropagateAll() call. D_UIDisplay and D_SideEffect
    /// then react to Normalized in the same frame, in correct topological order.
    /// </summary>
    public sealed class D_Processor : RCGBehaviour, IMetricsProvider
    {
        /// <summary>
        /// Derived Observable. D_UIDisplay and D_SideEffect declare [ReactsTo] on this.
        /// </summary>
        public readonly Observable<float> Normalized = new Observable<float>(0.5f);

        private D_DataSource _source;
        private float        _lastNormalized = -1f;
        private int          _totalEvals;
        private int          _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        /// <summary>Called by BenchmarkController before Start() fires.</summary>
        public void Initialize(D_DataSource source)
        {
            _source = source;
            // RCGBehaviour.Start() will wire [ReactsTo] using _source.
        }

        [ReactsTo("_source", "Value")]
        private void OnValueChanged(float rawValue)
        {
            _totalEvals++;
            float normalized = rawValue / 100f;

            if (Mathf.Approximately(normalized, _lastNormalized))
            {
                _redundantEvals++;
                return;
            }
            _lastNormalized    = normalized;
            Normalized.Value   = normalized;
            // Setting Normalized.Value marks it dirty → appended to RCGResolver dirty list
            // → processed in the SAME PropagateAll() call after this method returns.
        }
    }
}
