using UnityEngine;

namespace RCG.Benchmark.SystemC
{
    /// <summary>
    /// System C — Reactive Subject (UniRx pattern).
    /// Exposes a SimpleSubject[float] that downstream components subscribe to.
    /// OnNext() is called on value change, pushing the new value to all subscribers.
    /// </summary>
    public sealed class C_DataSource : MonoBehaviour, IDataSource
    {
        public readonly SimpleSubject<float> ValueSubject = new SimpleSubject<float>();

        private float _rawValue = 50f;

        public float RawValue => _rawValue;

        public void SetValue(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, 100f);
            if (Mathf.Approximately(_rawValue, clamped)) return;
            _rawValue = clamped;
            ValueSubject.OnNext(_rawValue); // synchronous cascade via Subject
        }
    }
}
