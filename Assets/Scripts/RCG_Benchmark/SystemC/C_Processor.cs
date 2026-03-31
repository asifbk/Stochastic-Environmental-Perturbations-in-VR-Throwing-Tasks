using System;
using UnityEngine;

namespace RCG.Benchmark.SystemC
{
    /// <summary>
    /// System C — Reactive Subject (UniRx pattern).
    /// Subscribes to C_DataSource.ValueSubject in Initialize() and stores the
    /// IDisposable for cleanup in OnDestroy(). Exposes its own NormalizedSubject
    /// for downstream components to subscribe to.
    /// </summary>
    public sealed class C_Processor : MonoBehaviour, IMetricsProvider
    {
        public readonly SimpleSubject<float> NormalizedSubject = new SimpleSubject<float>();

        private C_DataSource _source;
        private IDisposable  _subscription;
        private float        _lastNormalized = -1f;
        private int          _totalEvals;
        private int          _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(C_DataSource source)
        {
            _source       = source;
            _subscription = _source.ValueSubject.Subscribe(OnValueChanged);
        }

        private void OnDestroy()
        {
            _subscription?.Dispose();
        }

        private void OnValueChanged(float rawValue)
        {
            _totalEvals++;
            float normalized = rawValue / 100f;

            if (Mathf.Approximately(normalized, _lastNormalized))
            {
                _redundantEvals++;
                return;
            }
            _lastNormalized = normalized;
            NormalizedSubject.OnNext(normalized);
        }
    }
}
