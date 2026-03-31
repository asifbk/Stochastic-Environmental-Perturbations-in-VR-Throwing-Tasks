using UnityEngine;

namespace RCG.Benchmark.SystemA
{
    /// <summary>
    /// System A — Update() Polling.
    /// DataSource stores a raw value. No notification of any kind on change.
    /// Downstream components discover changes by polling RawValue every frame.
    /// </summary>
    public sealed class A_DataSource : MonoBehaviour, IDataSource
    {
        private float _rawValue = 50f;

        public float RawValue => _rawValue;

        public void SetValue(float value)
        {
            _rawValue = Mathf.Clamp(value, 0f, 100f);
            // System A: no cascade. Processor reads RawValue in its own ManualTick().
        }
    }
}
