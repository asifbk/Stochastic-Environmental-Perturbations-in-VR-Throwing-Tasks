namespace RCG.Benchmark
{
    /// <summary>
    /// Implemented by every system's DataSource component.
    /// BenchmarkController calls SetValue() on the R% of entities that change per frame.
    /// </summary>
    public interface IDataSource
    {
        /// <summary>Sets the raw source value (0–100). Triggers the cascade for this system.</summary>
        void SetValue(float value);

        /// <summary>Returns the current raw value without triggering any cascade.</summary>
        float RawValue { get; }
    }

    /// <summary>
    /// Implemented by Processor, UIDisplay, and SideEffect components in all four systems.
    /// BenchmarkController reads these after each measurement frame to compute redundancy rates.
    /// </summary>
    public interface IMetricsProvider
    {
        /// <summary>Total number of times this component's core logic executed.</summary>
        int TotalEvaluations { get; }

        /// <summary>
        /// Number of executions where the output was identical to the previous execution.
        /// Redundancy rate = RedundantEvaluations / TotalEvaluations.
        /// </summary>
        int RedundantEvaluations { get; }

        /// <summary>Resets both counters to zero. Called between measurement windows.</summary>
        void ResetMetrics();
    }

    /// <summary>
    /// Implemented by every system's Processor component.
    /// For System A (polling), BenchmarkController calls ManualTick() each frame
    /// to drive the full component chain explicitly.
    /// Systems B, C, D propagate automatically when SetValue() is called.
    /// </summary>
    public interface IManualTickable
    {
        void ManualTick();
    }
}
