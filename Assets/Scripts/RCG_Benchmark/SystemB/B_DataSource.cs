using System;
using UnityEngine;

namespace RCG.Benchmark.SystemB
{
    /// <summary>
    /// System B — Manual C# Events.
    /// Fires a C# event on value change. Downstream components subscribe in
    /// their Initialize() and must manually unsubscribe to avoid memory leaks
    /// — the boilerplate cost this system represents.
    /// </summary>
    public sealed class B_DataSource : MonoBehaviour, IDataSource
    {
        public event Action<float> OnValueChanged;

        private float _rawValue = 50f;

        public float RawValue => _rawValue;

        public void SetValue(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, 100f);
            if (Mathf.Approximately(_rawValue, clamped)) return;
            _rawValue = clamped;
            OnValueChanged?.Invoke(_rawValue); // synchronous cascade
        }
    }
}
