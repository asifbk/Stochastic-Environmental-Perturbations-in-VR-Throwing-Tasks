using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using RCG.Core;
using RCG.Benchmark;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Benchmark Controller.
    ///
    /// Orchestrates the full benchmark across all four XR interaction pipeline implementations,
    /// N values, and R values. Each "entity" is a visible interactable prop (cube) arranged in
    /// a grid in the scene — the colour of each prop reflects its current grab state, making
    /// the problem visible: at R=1% only ~1% of cubes glow green per frame; at R=100% all flash.
    ///
    /// Pipeline per entity (all four systems implement this chain):
    ///   XRHandProximitySensor  →  XRGrabStateDetector  →  XRHapticCommandGenerator
    ///                                                    →  XRProximityHUD (colour + label)
    ///
    /// The benchmark measures:
    ///   - Per-frame CPU time (Stopwatch, microsecond resolution)
    ///   - GC allocation delta per frame
    ///   - Total and redundant component evaluations
    ///
    /// Results written to Application.persistentDataPath/RCG_VR_Benchmark/.
    /// </summary>
    public sealed class VRBenchmarkController : MonoBehaviour
    {
        // ── Test Matrix ───────────────────────────────────────────────────────
        [Header("Test Matrix")]
        [Tooltip("Number of interactable props to spawn per run.")]
        [SerializeField] private int[] entityCounts = { 50, 100, 500 };

        [Tooltip("Fraction of props whose proximity changes each frame (0–1). " +
                 "R=0.01 simulates sparse XR interactions (1% of objects grabbed per frame). " +
                 "R=1.0 simulates all objects active simultaneously.")]
        [SerializeField] private float[] changeRates = { 0.01f, 0.10f, 0.50f, 1.00f };

        [Header("Systems")]
        [SerializeField] private bool runSystemA = true; // Update() polling
        [SerializeField] private bool runSystemB = true; // Manual C# events
        [SerializeField] private bool runSystemC = true; // Reactive Subject
        [SerializeField] private bool runSystemD = true; // RCG

        // ── Timing ────────────────────────────────────────────────────────────
        [Header("Timing")]
        [SerializeField] private int warmupFrames  = 60;
        [SerializeField] private int measureFrames = 300;

        // ── Scene Layout ──────────────────────────────────────────────────────
        [Header("Scene Layout")]
        [Tooltip("World-space origin of the prop grid.")]
        [SerializeField] private Vector3 gridOrigin = new Vector3(-10f, 0.5f, 0f);

        [Tooltip("Spacing between props in the grid (metres).")]
        [SerializeField] private float gridSpacing = 0.6f;

        [Tooltip("Number of columns in the prop grid. Rows auto-calculated from N.")]
        [SerializeField] private int gridColumns = 25;

        // ── Internal ──────────────────────────────────────────────────────────
        private enum SystemKind { A_Polling, B_Events, C_Reactive, D_RCG }

        private ResultsLogger              _logger;
        private RCGResolver                _resolver;
        private readonly Stopwatch         _sw       = new Stopwatch();
        private readonly List<GameObject>  _props    = new List<GameObject>();

        private readonly List<IDataSource>      _sensors   = new List<IDataSource>();
        private readonly List<IManualTickable>  _tickables = new List<IManualTickable>(); // System A
        private readonly List<IMetricsProvider> _metrics   = new List<IMetricsProvider>();

        private readonly List<ResultsLogger.FrameRow> _currentFrames = new List<ResultsLogger.FrameRow>();

        // HUD state
        private string _hudStatus       = "Initialising…";
        private double _hudLastAvgMs;
        private float  _hudLastRedund;
        private int    _hudDone;
        private int    _hudTotal;
        private string _hudSystemLabel  = "";

        // VR frame budget reference (90 Hz)
        private const double VRFrameBudgetMs = 11.1;

        // ─────────────────────────────────────────────────────────────────────

        private void Start()
        {
            _logger   = new ResultsLogger("RCG_VR_Benchmark");
            _resolver = FindObjectOfType<RCGResolver>();
            if (_resolver == null)
            {
                var go = new GameObject("RCGResolver");
                _resolver = go.AddComponent<RCGResolver>();
            }

            int systemCount = (runSystemA ? 1 : 0) + (runSystemB ? 1 : 0)
                            + (runSystemC ? 1 : 0) + (runSystemD ? 1 : 0);
            _hudTotal = systemCount * entityCounts.Length * changeRates.Length;

            StartCoroutine(RunAllBenchmarks());
        }

        private void OnDestroy() => _logger?.Close();

        // ── Orchestration ─────────────────────────────────────────────────────

        private IEnumerator RunAllBenchmarks()
        {
            yield return null;

            foreach (int n in entityCounts)
            {
                foreach (float r in changeRates)
                {
                    if (runSystemA) yield return RunSingle(SystemKind.A_Polling,  n, r);
                    if (runSystemB) yield return RunSingle(SystemKind.B_Events,   n, r);
                    if (runSystemC) yield return RunSingle(SystemKind.C_Reactive, n, r);
                    if (runSystemD) yield return RunSingle(SystemKind.D_RCG,      n, r);
                }
            }

            _hudStatus = $"COMPLETE\n{_logger.OutputDirectory}";
            _logger.Close();
            UnityEngine.Debug.Log("[VRBenchmarkController] All runs complete.");
        }

        private IEnumerator RunSingle(SystemKind kind, int n, float r)
        {
            string label = kind.ToString();
            _hudSystemLabel = label;
            _hudStatus = $"Spawning {n} props [{label}] R={r:P0}";
            yield return null;

            SpawnProps(kind, n);
            yield return null; // allow Start() on spawned components

            // Warmup
            _hudStatus = $"Warmup [{label}] N={n} R={r:P0}";
            for (int f = 0; f < warmupFrames; f++)
            {
                StepFrame(kind, n, r);
                yield return null;
            }
            ResetMetrics();

            // Measure
            _currentFrames.Clear();
            for (int f = 0; f < measureFrames; f++)
            {
                _hudStatus = $"[{label}] N={n} R={r:P0} — {f + 1}/{measureFrames}";
                var row    = StepFrame(kind, n, r);
                row.System = label;
                row.N      = n;
                row.R      = r;
                row.Frame  = f;
                _currentFrames.Add(row);
                _logger.LogFrame(row);
                yield return null;
            }

            _logger.LogSummary(label, n, r, _currentFrames);

            if (_currentFrames.Count > 0)
            {
                double sumTime = 0, sumRedund = 0;
                foreach (var fr in _currentFrames)
                {
                    sumTime   += fr.FrameTimeMs;
                    sumRedund += fr.TotalEvals > 0 ? fr.RedundantEvals / (double)fr.TotalEvals * 100.0 : 0.0;
                }
                _hudLastAvgMs = sumTime    / _currentFrames.Count;
                _hudLastRedund = (float)(sumRedund / _currentFrames.Count);
            }

            _hudDone++;
            DestroyProps();
            yield return null;
        }

        // ── Per-Frame Step ────────────────────────────────────────────────────

        private ResultsLogger.FrameRow StepFrame(SystemKind kind, int n, float r)
        {
            int dirtyCount = Mathf.Max(1, Mathf.RoundToInt(n * r));

            float[] newValues = new float[dirtyCount];
            for (int i = 0; i < dirtyCount; i++)
                newValues[i] = UnityEngine.Random.Range(0f, 100f);

            long gcBefore = System.GC.GetTotalMemory(false);
            _sw.Restart();

            for (int i = 0; i < dirtyCount; i++)
                _sensors[i % _sensors.Count].SetValue(newValues[i]);

            if (kind == SystemKind.A_Polling)
            {
                for (int i = 0; i < _tickables.Count; i++)
                    _tickables[i].ManualTick();
            }
            else if (kind == SystemKind.D_RCG)
            {
                _resolver.PropagateAll();
            }

            _sw.Stop();
            long gcAfter = System.GC.GetTotalMemory(false);

            int totalEvals = 0, redundantEvals = 0;
            for (int i = 0; i < _metrics.Count; i++)
            {
                totalEvals     += _metrics[i].TotalEvaluations;
                redundantEvals += _metrics[i].RedundantEvaluations;
            }

            ResetMetrics();

            return new ResultsLogger.FrameRow
            {
                FrameTimeMs    = _sw.Elapsed.TotalMilliseconds,
                GCBytes        = gcAfter - gcBefore,
                TotalEvals     = totalEvals,
                RedundantEvals = redundantEvals,
            };
        }

        // ── Prop Lifecycle ────────────────────────────────────────────────────

        private void SpawnProps(SystemKind kind, int n)
        {
            _sensors.Clear();
            _tickables.Clear();
            _metrics.Clear();

            for (int i = 0; i < n; i++)
            {
                // Create a visible cube prop in a grid layout
                var go       = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name      = $"Prop_{kind}_{i:000}";
                go.transform.localScale = Vector3.one * 0.45f;

                int   col = i % gridColumns;
                int   row = i / gridColumns;
                go.transform.position = gridOrigin + new Vector3(col * gridSpacing, 0f, row * gridSpacing);

                // Remove collider — not needed for benchmark
                Destroy(go.GetComponent<Collider>());

                var rend = go.GetComponent<Renderer>();

                _props.Add(go);

                switch (kind)
                {
                    case SystemKind.A_Polling:  SpawnA(go, rend); break;
                    case SystemKind.B_Events:   SpawnB(go, rend); break;
                    case SystemKind.C_Reactive: SpawnC(go, rend); break;
                    case SystemKind.D_RCG:      SpawnD(go, rend); break;
                }
            }
        }

        private void SpawnA(GameObject go, Renderer rend)
        {
            var sensor  = go.AddComponent<XRHandProximitySensor>();
            var detect  = go.AddComponent<XRGrabStateDetector_A>();
            var hud     = go.AddComponent<XRProximityHUD_A>();
            var haptic  = go.AddComponent<XRHapticCommandGenerator_A>();

            detect.Initialize(sensor);
            hud.Initialize(detect, rend);
            haptic.Initialize(detect);

            _sensors.Add(sensor);
            _tickables.Add(detect);
            _metrics.Add(detect);
            _metrics.Add(hud);
            _metrics.Add(haptic);
        }

        private void SpawnB(GameObject go, Renderer rend)
        {
            var sensor  = go.AddComponent<XRHandProximitySensor_B>();
            var detect  = go.AddComponent<XRGrabStateDetector_B>();
            var hud     = go.AddComponent<XRProximityHUD_B>();
            var haptic  = go.AddComponent<XRHapticCommandGenerator_B>();

            detect.Initialize(sensor);
            hud.Initialize(detect, rend);
            haptic.Initialize(detect);

            _sensors.Add(sensor);
            _metrics.Add(detect);
            _metrics.Add(hud);
            _metrics.Add(haptic);
        }

        private void SpawnC(GameObject go, Renderer rend)
        {
            var sensor  = go.AddComponent<XRHandProximitySensor_C>();
            var detect  = go.AddComponent<XRGrabStateDetector_C>();
            var hud     = go.AddComponent<XRProximityHUD_C>();
            var haptic  = go.AddComponent<XRHapticCommandGenerator_C>();

            detect.Initialize(sensor);
            hud.Initialize(detect, rend);
            haptic.Initialize(detect);

            _sensors.Add(sensor);
            _metrics.Add(detect);
            _metrics.Add(hud);
            _metrics.Add(haptic);
        }

        private void SpawnD(GameObject go, Renderer rend)
        {
            var sensor  = go.AddComponent<XRHandProximitySensor_D>();
            var detect  = go.AddComponent<XRGrabStateDetector_D>();
            var hud     = go.AddComponent<XRProximityHUD_D>();
            var haptic  = go.AddComponent<XRHapticCommandGenerator_D>();

            detect.Initialize(sensor);
            hud.Initialize(detect, rend);
            haptic.Initialize(detect);

            _sensors.Add(sensor);
            _metrics.Add(detect);
            _metrics.Add(hud);
            _metrics.Add(haptic);
        }

        private void DestroyProps()
        {
            foreach (var go in _props) Destroy(go);
            _props.Clear();
            _sensors.Clear();
            _tickables.Clear();
            _metrics.Clear();
        }

        private void ResetMetrics()
        {
            for (int i = 0; i < _metrics.Count; i++)
                _metrics[i].ResetMetrics();
        }

        // ── HUD ───────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 14,
                alignment = TextAnchor.UpperLeft,
            };
            style.normal.textColor = Color.white;

            double budgetPct = _hudLastAvgMs / VRFrameBudgetMs * 100.0;

            string text =
                "VR XR INTERACTION PIPELINE BENCHMARK\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                $"Status:       {_hudStatus}\n" +
                $"Progress:     {_hudDone} / {_hudTotal} runs\n" +
                $"Last avg:     {_hudLastAvgMs:F3} ms/frame   " +
                    $"({budgetPct:F1}% of 11.1 ms VR budget)\n" +
                $"Redundancy:   {_hudLastRedund:F1}%\n" +
                "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                "Props colour:  grey=far  amber=near  green=grabbed\n" +
                $"Output: {System.IO.Path.GetFileName(_logger?.OutputDirectory ?? "")}";

            GUILayout.BeginArea(new Rect(10, 10, 500, 220));
            GUILayout.Box(text, style, GUILayout.ExpandWidth(true));
            GUILayout.EndArea();
        }
    }
}
