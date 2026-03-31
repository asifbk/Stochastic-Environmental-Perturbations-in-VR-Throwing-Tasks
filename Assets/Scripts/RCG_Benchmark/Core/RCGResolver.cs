using System.Collections.Generic;
using UnityEngine;

namespace RCG.Core
{
    /// <summary>
    /// Singleton that maintains the dirty Observable list and drives propagation.
    ///
    /// Design:
    ///  - Observable[T].Value setter calls RegisterDirty() when a value changes.
    ///  - BenchmarkController calls PropagateAll() explicitly each frame so that
    ///    propagation timing can be measured in isolation.
    ///  - The propagation loop is index-based (not foreach) so that chained
    ///    dependencies — where a dependent method sets another Observable — are
    ///    automatically appended to the list and processed in the same call,
    ///    in correct topological order, without an explicit sort step.
    ///  - Circular dependencies cause infinite growth of _dirtyNodes; they are
    ///    caught by a max-iteration guard that logs an error.
    /// </summary>
    public sealed class RCGResolver : MonoBehaviour
    {
        private const int MaxPropagationIterations = 100_000;

        public static RCGResolver Instance { get; private set; }

        private readonly List<IObservableNode>    _dirtyNodes = new List<IObservableNode>();
        private readonly HashSet<IObservableNode> _dirtySet   = new HashSet<IObservableNode>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Called by Observable[T] when its value changes.
        /// Enqueues the node for the next PropagateAll() call.
        /// Thread-safety: must be called from the main thread.
        /// </summary>
        public void RegisterDirty(IObservableNode node)
        {
            if (_dirtySet.Add(node))
                _dirtyNodes.Add(node);
        }

        /// <summary>
        /// Propagates all dirty Observables in topological order.
        /// Called explicitly by BenchmarkController each frame so timing is isolated.
        ///
        /// The index-based loop handles chained dependencies: when a dependent
        /// sets another Observable, that node is appended to _dirtyNodes and will
        /// be reached as i increments — no explicit sort needed.
        /// </summary>
        public void PropagateAll()
        {
            int guard = 0;
            for (int i = 0; i < _dirtyNodes.Count; i++)
            {
                if (++guard > MaxPropagationIterations)
                {
                    Debug.LogError("[RCGResolver] Propagation exceeded max iterations — possible circular dependency. Aborting.");
                    break;
                }
                _dirtyNodes[i].Propagate();
            }
            _dirtyNodes.Clear();
            _dirtySet.Clear();
        }

        /// <summary>Returns the number of dirty nodes queued for the next propagation.</summary>
        public int PendingCount => _dirtyNodes.Count;
    }
}
