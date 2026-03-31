using RCG.Core;
using UnityEngine;

namespace RCG.Benchmark.SystemD
{
    /// <summary>
    /// System D — Reactive Component Graph (RCG).
    /// Exposes an Observable[float] field. Setting its Value marks it dirty
    /// and registers it with RCGResolver — zero allocation, zero per-frame cost
    /// until RCGResolver.PropagateAll() is called by BenchmarkController.
    /// </summary>
    public sealed class D_DataSource : MonoBehaviour, IDataSource
    {
        /// <summary>
        /// Public Observable field. Downstream [ReactsTo] declarations reference
        /// this field by name: [ReactsTo("_source", "Value")].
        /// </summary>
        public readonly Observable<float> Value = new Observable<float>(50f);

        public float RawValue => Value.Value;

        public void SetValue(float value)
        {
            Value.Value = Mathf.Clamp(value, 0f, 100f);
            // Marks dirty → registered in RCGResolver._dirtyNodes.
            // No cascade yet. BenchmarkController calls PropagateAll() after all SetValue calls.
        }
    }
}
