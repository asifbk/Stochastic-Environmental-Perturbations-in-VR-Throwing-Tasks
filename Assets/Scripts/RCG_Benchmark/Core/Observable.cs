using System;
using System.Collections.Generic;

namespace RCG.Core
{
    /// <summary>
    /// A thin, allocation-free wrapper around a value T that tracks whether its value
    /// changed this propagation cycle and notifies the RCGResolver to schedule propagation.
    ///
    /// Key properties:
    ///  - Setting Value to an equal value is a no-op (equality guard).
    ///  - Dirty flagging happens once per change; RCGResolver clears it after propagation.
    ///  - Dependents are pre-compiled delegates registered once at Start() — zero
    ///    per-frame reflection overhead.
    ///  - Chained dependencies are handled naturally: if a dependent method sets another
    ///    Observable, that observable joins the dirty list and is propagated in the same
    ///    PropagateAll() call, in correct topological order.
    /// </summary>
    public sealed class Observable<T>  : IObservableNode
    {
        private T _value;
        private bool _isDirty;
        private readonly List<Action<T>> _dependents = new List<Action<T>>();

        public Observable(T initialValue = default)
        {
            _value = initialValue;
        }

        /// <summary>
        /// Gets or sets the stored value.
        /// Setting to an equal value is a no-op.
        /// Setting to a different value marks this Observable dirty and registers
        /// it with the RCGResolver for propagation.
        /// </summary>
        public T Value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                _value = value;
                if (!_isDirty)
                {
                    _isDirty = true;
                    RCGResolver.Instance?.RegisterDirty(this);
                }
            }
        }

        /// <summary>
        /// Registers a pre-compiled delegate to be called when this Observable propagates.
        /// Called once at Start() by RCGBehaviour via reflection — zero per-frame cost.
        /// </summary>
        public void RegisterDependent(Action<T> callback)
        {
            _dependents.Add(callback);
        }

        /// <summary>
        /// Called by RCGResolver during PropagateAll().
        /// Clears the dirty flag then invokes all registered dependent delegates.
        /// Clearing BEFORE invocation allows dependents to set this value again
        /// (e.g. feedback loops) without losing the new dirty signal.
        /// </summary>
        void IObservableNode.Propagate()
        {
            _isDirty = false;
            T snapshot = _value;
            for (int i = 0; i < _dependents.Count; i++)
                _dependents[i](snapshot);
        }
    }
}
