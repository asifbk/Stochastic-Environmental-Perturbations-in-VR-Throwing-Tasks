using System;
using UnityEngine;

namespace RCG.Benchmark.SystemB
{
    /// <summary>
    /// System B — Manual C# Events.
    /// Subscribes to B_DataSource.OnValueChanged in Initialize() and fires
    /// its own OnNormalizedChanged event for downstream components.
    /// Must unsubscribe in OnDestroy() to prevent memory leaks — the key
    /// developer burden this system represents.
    /// </summary>
    public sealed class B_Processor : MonoBehaviour, IMetricsProvider
    {
        public event Action<float> OnNormalizedChanged;

        private B_DataSource _source;
        private float        _lastNormalized = -1f;
        private int          _totalEvals;
        private int          _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(B_DataSource source)
        {
            _source              = source;
            _source.OnValueChanged += OnSourceValueChanged;
        }

        private void OnDestroy()
        {
            // Manual unsubscription — skipping this causes a memory leak (measured System B bug).
            if (_source != null) _source.OnValueChanged -= OnSourceValueChanged;
        }

        private void OnSourceValueChanged(float rawValue)
        {
            _totalEvals++;
            float normalized = rawValue / 100f;

            if (Mathf.Approximately(normalized, _lastNormalized))
            {
                _redundantEvals++;
                return;
            }
            _lastNormalized = normalized;
            OnNormalizedChanged?.Invoke(normalized);
        }
    }
}
