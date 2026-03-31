using System;
using System.Collections.Generic;

namespace RCG.Benchmark.SystemC
{
    /// <summary>
    /// Minimal Subject[T] that reproduces the UniRx / R3 Subscribe/Dispose pattern
    /// without requiring the UniRx package to be installed.
    ///
    /// This captures the essential overhead of the reactive subscription model:
    ///  - Subscribe() creates a heap-allocated Subscription object (IDisposable).
    ///  - OnNext() iterates the subscription list and invokes each Action[T].
    ///  - Dispose() removes the entry from the subscription list.
    ///
    /// The GC and iteration cost of this pattern is what System C measures vs System D.
    /// </summary>
    public sealed class SimpleSubject<T>
    {
        private readonly List<Action<T>> _subscribers = new List<Action<T>>();

        /// <summary>
        /// Registers a callback. Returns an IDisposable that removes the subscription.
        /// The IDisposable allocation is the per-Subscribe cost being measured.
        /// </summary>
        public IDisposable Subscribe(Action<T> onNext)
        {
            _subscribers.Add(onNext);
            return new Subscription(onNext, _subscribers);
        }

        /// <summary>
        /// Pushes a value to all current subscribers synchronously.
        /// Equivalent to UniRx Subject[T].OnNext().
        /// </summary>
        public void OnNext(T value)
        {
            // Iterate backwards so that Dispose() during OnNext doesn't skip entries
            for (int i = _subscribers.Count - 1; i >= 0; i--)
                _subscribers[i](value);
        }

        public int SubscriberCount => _subscribers.Count;

        private sealed class Subscription : IDisposable
        {
            private readonly Action<T>    _callback;
            private readonly List<Action<T>> _list;
            private bool _disposed;

            public Subscription(Action<T> callback, List<Action<T>> list)
            {
                _callback = callback;
                _list     = list;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _list.Remove(_callback);
            }
        }
    }
}
