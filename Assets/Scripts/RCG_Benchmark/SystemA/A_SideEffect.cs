using UnityEngine;

namespace RCG.Benchmark.SystemA
{
    /// <summary>
    /// System A — Update() Polling.
    /// Simulates a threshold-triggered side effect (e.g. play a warning sound
    /// when a value drops below 20%). Tracks rising-edge crossings only.
    /// Redundant when the threshold state has not changed.
    /// </summary>
    public sealed class A_SideEffect : MonoBehaviour, IMetricsProvider
    {
        private const float Threshold = 0.2f;

        private A_Processor _processor;
        private bool        _wasBelow;
        private int         _triggerCount;
        private int         _totalEvals;
        private int         _redundantEvals;

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public int TriggerCount         => _triggerCount;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(A_Processor processor)
        {
            _processor = processor;
            _processor._sideEffect = this;
        }

        /// <summary>Called by A_Processor.ManualTick() every frame.</summary>
        public void ManualTick(float normalized)
        {
            _totalEvals++;
            bool isBelow = normalized < Threshold;

            // Redundant: threshold state unchanged.
            if (isBelow == _wasBelow)
            {
                _redundantEvals++;
                return;
            }

            // Rising edge — value just crossed below threshold.
            if (isBelow) _triggerCount++;
            _wasBelow = isBelow;
            // Real game: AudioSource.Play(), ParticleSystem.Play(), etc. (omitted)
        }
    }
}
