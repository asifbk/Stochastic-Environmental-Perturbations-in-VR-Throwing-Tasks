using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using RCG.Core;

namespace RCG.Benchmark
{
    /// <summary>
    /// Orchestrates the full benchmark across all four systems, N values, and R values.
    ///
    /// Measurement model (fair across all systems):
    ///   Each frame, BenchmarkController:
    ///     1. Selects R% of entities as "dirty" (value changed this frame).
    ///     2. Calls SetValue() on dirty entities — for B/C this synchronously triggers
    ///        the full cascade; for A this only stores the value; for D this marks dirty.
    ///     3. For System A: calls ManualTick() on ALL entity processors (polling).
    ///        For System D: calls RCGResolver.PropagateAll().
    ///        For B/C: no additional step — cascade already completed in step 2.
    ///     4. Records frame time (Stopwatch), GC allocation delta, and redundancy counts.
    ///
    /// Run matrix: [4 systems] × [N values] × [R values] = fully factorial.
    /// Results written to Application.persistentDataPath/RCG_Benchmark/.
    /// </summary>
    public sealed class BenchmarkController : MonoBehaviour
    {
        // ── Test Matrix ───────────────────────────────────────────────────────
        [Header("Test Matrix")]
        [Tooltip("Number of entities to spawn per run.")]
        [SerializeField] private int[]   entityCounts  = { 50, 100, 500 };

        [Tooltip("Fraction of entities whose value changes each frame (0–1).")]
        [SerializeField] private float[] changeRates   = { 0.01f, 0.10f, 0.50f, 1.00f };

        [Tooltip("Which systems to benchmark. Uncheck to skip a system.")]
        [SerializeField] private bool runSystemA = true;
        [SerializeField] private bool runSystemB = true;
        [SerializeField] private bool runSystemC = true;
        [SerializeField] private bool runSystemD = true;

        // ── Timing ────────────────────────────────────────────────────────────
        [Header("Timing")]
        [Tooltip("Frames to run before measuring (JIT warmup + scene settle).")]
        [SerializeField] private int warmupFrames   = 60;
        [Tooltip("Frames to measure per run.")]
        [SerializeField] private int measureFrames  = 300;

        // ── Internal State ────────────────────────────────────────────────────
        private enum SystemKind { A_Polling, B_Events, C_Reactive, D_RCG }

        private ResultsLogger              _logger;
        private RCGResolver                _resolver;
        private readonly Stopwatch         _sw         = new Stopwatch();
        private readonly List<GameObject>  _entities   = new List<GameObject>();

        // Per-entity component references (flattened lists for cache-friendly iteration)
        private readonly List<IDataSource>     _dataSources    = new List<IDataSource>();
        private readonly List<IManualTickable> _tickables      = new List<IManualTickable>();    // System A only
        private readonly List<IMetricsProvider> _metricsAll    = new List<IMetricsProvider>();  // all systems

        // Per-frame frame-data accumulator for current run
        private readonly List<ResultsLogger.FrameRow> _currentFrames = new List<ResultsLogger.FrameRow>();

        // ── Progress HUD ─────────────────────────────────────────────────────
        private string _hudStatus = "Idle";
        private double _hudLastAvgMs;
        private float  _hudLastRedundancy;
        private int    _hudCompletedRuns;
        private int    _hudTotalRuns;

        // ─────────────────────────────────────────────────────────────────────

        private void Start()
        {
            _logger   = new ResultsLogger();
            _resolver = FindObjectOfType<RCGResolver>();
            if (_resolver == null)
            {
                var go = new GameObject("RCGResolver");
                _resolver = go.AddComponent<RCGResolver>();
            }

            // Count total runs for HUD
            int systemCount = (runSystemA ? 1 : 0) + (runSystemB ? 1 : 0)
                            + (runSystemC ? 1 : 0) + (runSystemD ? 1 : 0);
            _hudTotalRuns = systemCount * entityCounts.Length * changeRates.Length;

            StartCoroutine(RunAllBenchmarks());
        }

        private void OnDestroy()
        {
            _logger?.Close();
        }

        // ── Benchmark Orchestration ───────────────────────────────────────────

        private IEnumerator RunAllBenchmarks()
        {
            yield return null; // let scene settle one frame

            foreach (int n in entityCounts)
            {
                foreach (float r in changeRates)
                {
                    if (runSystemA) yield return RunSingleBenchmark(SystemKind.A_Polling, n, r);
                    if (runSystemB) yield return RunSingleBenchmark(SystemKind.B_Events,  n, r);
                    if (runSystemC) yield return RunSingleBenchmark(SystemKind.C_Reactive, n, r);
                    if (runSystemD) yield return RunSingleBenchmark(SystemKind.D_RCG,     n, r);
                }
            }

            _hudStatus = $"COMPLETE — results in:\n{_logger.OutputDirectory}";
            _logger.Close();
            UnityEngine.Debug.Log("[BenchmarkController] All runs complete.");
        }

        private IEnumerator RunSingleBenchmark(SystemKind kind, int n, float r)
        {
            string systemName = kind.ToString();
            _hudStatus = $"Spawning {n} entities [{systemName}] R={r:P0}";
            yield return null;

            // 1. Spawn entities
            SpawnEntities(kind, n);
            yield return null; // allow Start() on spawned components to fire

            // 2. Warmup
            _hudStatus = $"Warmup [{systemName}] N={n} R={r:P0}";
            for (int f = 0; f < warmupFrames; f++)
            {
                StepFrame(kind, n, r, measureOnly: false);
                yield return null;
            }
            ResetAllMetrics();

            // 3. Measure
            _currentFrames.Clear();
            for (int f = 0; f < measureFrames; f++)
            {
                _hudStatus = $"Measuring [{systemName}] N={n} R={r:P0} — frame {f + 1}/{measureFrames}";
                var row = StepFrame(kind, n, r, measureOnly: true);
                row.System = systemName;
                row.N      = n;
                row.R      = r;
                row.Frame  = f;
                _currentFrames.Add(row);
                _logger.LogFrame(row);
                yield return null;
            }

            // 4. Summarise
            _logger.LogSummary(systemName, n, r, _currentFrames);

            if (_currentFrames.Count > 0)
            {
                double sumTime = 0, sumRedund = 0;
                foreach (var fr in _currentFrames)
                {
                    sumTime    += fr.FrameTimeMs;
                    sumRedund  += fr.TotalEvals > 0 ? fr.RedundantEvals / (double)fr.TotalEvals * 100.0 : 0.0;
                }
                _hudLastAvgMs      = sumTime    / _currentFrames.Count;
                _hudLastRedundancy = (float)(sumRedund / _currentFrames.Count);
            }
            _hudCompletedRuns++;

            // 5. Teardown
            DestroyEntities();
            yield return null;
        }

        // ── Per-Frame Step ────────────────────────────────────────────────────

        private ResultsLogger.FrameRow StepFrame(SystemKind kind, int n, float r, bool measureOnly)
        {
            int dirtyCount = Mathf.Max(1, Mathf.RoundToInt(n * r));

            // Pre-generate dirty values to avoid RNG inside the measured block
            float[] newValues = new float[dirtyCount];
            for (int i = 0; i < dirtyCount; i++)
                newValues[i] = UnityEngine.Random.Range(0f, 100f);

            long gcBefore = System.GC.GetTotalMemory(false);
            _sw.Restart();

            // ── Apply source changes (same for all systems) ───────────────────
            for (int i = 0; i < dirtyCount; i++)
                _dataSources[i % _dataSources.Count].SetValue(newValues[i]);
            // ^ B and C: cascade fires synchronously inside SetValue.
            // ^ A: only stores value; cascade in polling step below.
            // ^ D: marks Observable dirty; resolver step below.

            // ── System-specific propagation ───────────────────────────────────
            if (kind == SystemKind.A_Polling)
            {
                for (int i = 0; i < _tickables.Count; i++)
                    _tickables[i].ManualTick();
            }
            else if (kind == SystemKind.D_RCG)
            {
                _resolver.PropagateAll();
            }
            // B and C: already propagated synchronously

            _sw.Stop();
            long gcAfter = System.GC.GetTotalMemory(false);

            // ── Collect metrics ───────────────────────────────────────────────
            int totalEvals = 0, redundantEvals = 0;
            for (int i = 0; i < _metricsAll.Count; i++)
            {
                totalEvals     += _metricsAll[i].TotalEvaluations;
                redundantEvals += _metricsAll[i].RedundantEvaluations;
            }

            if (measureOnly) ResetAllMetrics();

            return new ResultsLogger.FrameRow
            {
                FrameTimeMs    = _sw.Elapsed.TotalMilliseconds,
                GCBytes        = gcAfter - gcBefore,
                TotalEvals     = totalEvals,
                RedundantEvals = redundantEvals,
            };
        }

        // ── Entity Lifecycle ──────────────────────────────────────────────────

        private void SpawnEntities(SystemKind kind, int n)
        {
            _dataSources.Clear();
            _tickables.Clear();
            _metricsAll.Clear();

            for (int i = 0; i < n; i++)
            {
                GameObject go = new GameObject($"Entity_{kind}_{i}");
                _entities.Add(go);

                switch (kind)
                {
                    case SystemKind.A_Polling:  SpawnA(go); break;
                    case SystemKind.B_Events:   SpawnB(go); break;
                    case SystemKind.C_Reactive: SpawnC(go); break;
                    case SystemKind.D_RCG:      SpawnD(go); break;
                }
            }
        }

        private void SpawnA(GameObject go)
        {
            var ds   = go.AddComponent<SystemA.A_DataSource>();
            var proc = go.AddComponent<SystemA.A_Processor>();
            var ui   = go.AddComponent<SystemA.A_UIDisplay>();
            var fx   = go.AddComponent<SystemA.A_SideEffect>();
            proc.Initialize(ds);
            ui.Initialize(proc);
            fx.Initialize(proc);
            _dataSources.Add(ds);
            _tickables.Add(proc);           // ManualTick drives proc → ui → fx
            _metricsAll.Add(proc);
            _metricsAll.Add(ui);
            _metricsAll.Add(fx);
        }

        private void SpawnB(GameObject go)
        {
            var ds   = go.AddComponent<SystemB.B_DataSource>();
            var proc = go.AddComponent<SystemB.B_Processor>();
            var ui   = go.AddComponent<SystemB.B_UIDisplay>();
            var fx   = go.AddComponent<SystemB.B_SideEffect>();
            proc.Initialize(ds);
            ui.Initialize(proc);
            fx.Initialize(proc);
            _dataSources.Add(ds);
            _metricsAll.Add(proc);
            _metricsAll.Add(ui);
            _metricsAll.Add(fx);
        }

        private void SpawnC(GameObject go)
        {
            var ds   = go.AddComponent<SystemC.C_DataSource>();
            var proc = go.AddComponent<SystemC.C_Processor>();
            var ui   = go.AddComponent<SystemC.C_UIDisplay>();
            var fx   = go.AddComponent<SystemC.C_SideEffect>();
            proc.Initialize(ds);
            ui.Initialize(proc);
            fx.Initialize(proc);
            _dataSources.Add(ds);
            _metricsAll.Add(proc);
            _metricsAll.Add(ui);
            _metricsAll.Add(fx);
        }

        private void SpawnD(GameObject go)
        {
            var ds   = go.AddComponent<SystemD.D_DataSource>();
            var proc = go.AddComponent<SystemD.D_Processor>();
            var ui   = go.AddComponent<SystemD.D_UIDisplay>();
            var fx   = go.AddComponent<SystemD.D_SideEffect>();
            proc.Initialize(ds);
            ui.Initialize(proc);
            fx.Initialize(proc);
            _dataSources.Add(ds);
            _metricsAll.Add(proc);
            _metricsAll.Add(ui);
            _metricsAll.Add(fx);
        }

        private void DestroyEntities()
        {
            foreach (var go in _entities) Destroy(go);
            _entities.Clear();
            _dataSources.Clear();
            _tickables.Clear();
            _metricsAll.Clear();
        }

        private void ResetAllMetrics()
        {
            for (int i = 0; i < _metricsAll.Count; i++)
                _metricsAll[i].ResetMetrics();
        }

        // ── HUD ───────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperLeft };
            style.normal.textColor = Color.white;

            string text =
                "RCG BENCHMARK\n" +
                "─────────────────────────────\n" +
                $"Status:     {_hudStatus}\n" +
                $"Progress:   {_hudCompletedRuns} / {_hudTotalRuns} runs\n" +
                $"Last avg:   {_hudLastAvgMs:F3} ms/frame\n" +
                $"Redundancy: {_hudLastRedundancy:F1}%\n" +
                "─────────────────────────────\n" +
                $"Output: .../{System.IO.Path.GetFileName(_logger?.OutputDirectory ?? "")}";

            GUILayout.BeginArea(new Rect(10, 10, 420, 200));
            GUILayout.Box(text, style, GUILayout.ExpandWidth(true));
            GUILayout.EndArea();
        }
    }
}
