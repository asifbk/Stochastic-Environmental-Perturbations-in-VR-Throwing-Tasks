using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RCG.Benchmark
{
    /// <summary>
    /// Writes per-frame measurement rows and per-run summary rows to CSV files
    /// in Application.persistentDataPath/RCG_Benchmark/.
    ///
    /// Output files:
    ///   frames_TIMESTAMP.csv   — one row per measured frame
    ///   summary_TIMESTAMP.csv  — one row per completed run (averaged over frames)
    /// </summary>
    public sealed class ResultsLogger
    {
        // ── Frame log ────────────────────────────────────────────────────────
        public struct FrameRow
        {
            public string System;
            public int    N;
            public float  R;
            public int    Frame;
            public double FrameTimeMs;
            public long   GCBytes;
            public int    TotalEvals;
            public int    RedundantEvals;
        }

        // ── Summary log ──────────────────────────────────────────────────────
        public struct SummaryRow
        {
            public string System;
            public int    N;
            public float  R;
            public int    FramesMeasured;
            public double AvgFrameTimeMs;
            public double StdDevFrameTimeMs;
            public double AvgGCBytes;
            public double AvgRedundancyPct;
        }

        private readonly string  _outputDir;
        private readonly string  _framesPath;
        private readonly string  _summaryPath;
        private StreamWriter     _framesWriter;
        private StreamWriter     _summaryWriter;

        private static readonly string FrameHeader =
            "System,N,R,Frame,FrameTimeMs,GCBytes,TotalEvals,RedundantEvals,RedundancyPct";
        private static readonly string SummaryHeader =
            "System,N,R,FramesMeasured,AvgFrameTimeMs,StdDevFrameTimeMs,AvgGCBytes,AvgRedundancyPct";

        /// <param name="subDirectory">
        /// Subdirectory under Application.persistentDataPath to write results into.
        /// Defaults to "RCG_Benchmark". VR benchmark uses "RCG_VR_Benchmark".
        /// </param>
        public ResultsLogger(string subDirectory = "RCG_Benchmark")
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _outputDir   = Path.Combine(Application.persistentDataPath, subDirectory);
            _framesPath  = Path.Combine(_outputDir, $"frames_{timestamp}.csv");
            _summaryPath = Path.Combine(_outputDir, $"summary_{timestamp}.csv");

            Directory.CreateDirectory(_outputDir);

            _framesWriter  = new StreamWriter(_framesPath,  append: false, encoding: Encoding.UTF8);
            _summaryWriter = new StreamWriter(_summaryPath, append: false, encoding: Encoding.UTF8);

            _framesWriter.WriteLine(FrameHeader);
            _summaryWriter.WriteLine(SummaryHeader);

            Debug.Log($"[ResultsLogger] Writing to:\n  {_framesPath}\n  {_summaryPath}");
        }

        /// <summary>Appends one frame row. Call every measurement frame.</summary>
        public void LogFrame(FrameRow row)
        {
            double redundancyPct = row.TotalEvals > 0
                ? row.RedundantEvals / (double)row.TotalEvals * 100.0
                : 0.0;

            _framesWriter.WriteLine(
                $"{row.System},{row.N},{row.R:F2},{row.Frame}," +
                $"{row.FrameTimeMs:F4},{row.GCBytes},{row.TotalEvals}," +
                $"{row.RedundantEvals},{redundancyPct:F2}");
        }

        /// <summary>
        /// Computes summary statistics over the provided frame rows and writes one summary row.
        /// </summary>
        public void LogSummary(string system, int n, float r, List<FrameRow> frames)
        {
            if (frames.Count == 0) return;

            double sumTime    = 0, sumGC = 0, sumRedundancy = 0;
            foreach (var f in frames)
            {
                sumTime       += f.FrameTimeMs;
                sumGC         += f.GCBytes;
                double pct     = f.TotalEvals > 0 ? f.RedundantEvals / (double)f.TotalEvals * 100.0 : 0.0;
                sumRedundancy += pct;
            }

            double avgTime    = sumTime    / frames.Count;
            double avgGC      = sumGC      / frames.Count;
            double avgRedund  = sumRedundancy / frames.Count;

            // Standard deviation of frame time
            double variance = 0;
            foreach (var f in frames) variance += (f.FrameTimeMs - avgTime) * (f.FrameTimeMs - avgTime);
            double stdDev = Math.Sqrt(variance / frames.Count);

            var row = new SummaryRow
            {
                System            = system,
                N                 = n,
                R                 = r,
                FramesMeasured    = frames.Count,
                AvgFrameTimeMs    = avgTime,
                StdDevFrameTimeMs = stdDev,
                AvgGCBytes        = avgGC,
                AvgRedundancyPct  = avgRedund,
            };

            _summaryWriter.WriteLine(
                $"{row.System},{row.N},{row.R:F2},{row.FramesMeasured}," +
                $"{row.AvgFrameTimeMs:F4},{row.StdDevFrameTimeMs:F4}," +
                $"{row.AvgGCBytes:F1},{row.AvgRedundancyPct:F2}");

            _framesWriter.Flush();
            _summaryWriter.Flush();

            Debug.Log($"[ResultsLogger] Run complete — {system} N={n} R={r:P0} | " +
                      $"Avg: {avgTime:F3}ms  StdDev: {stdDev:F3}ms  Redundancy: {avgRedund:F1}%");
        }

        public string OutputDirectory => _outputDir;

        /// <summary>Flushes and closes both files. Call when all runs are complete.</summary>
        public void Close()
        {
            _framesWriter?.Flush();
            _summaryWriter?.Flush();
            _framesWriter?.Close();
            _summaryWriter?.Close();
            _framesWriter  = null;
            _summaryWriter = null;
            Debug.Log($"[ResultsLogger] All results written to {_outputDir}");
        }
    }
}
