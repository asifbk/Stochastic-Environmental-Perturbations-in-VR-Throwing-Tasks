using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — Grab State Detector (System A: Update polling).
    ///
    /// Maps raw hand proximity (0–100) to:
    ///   - GripStrength (0–1): how firmly the hand is closing around the object
    ///   - IsGrabbed (bool): proximity below grab threshold
    ///
    /// System A: ManualTick() is called every frame by VRBenchmarkController on ALL objects,
    /// regardless of whether the hand proximity changed. This is the polling cost being measured.
    ///
    /// Drives XRProximityHUD and XRHapticCommandGenerator via direct ManualTick() calls —
    /// reproducing the Update() chain developers write in polling-based XR apps.
    /// </summary>
    public sealed class XRGrabStateDetector_A : MonoBehaviour, IMetricsProvider, IManualTickable
    {
        private const float GrabThreshold = 0.25f; // grip strength > 0.25 = grabbed

        private XRHandProximitySensor      _sensor;
        internal XRProximityHUD_A          _hud;
        internal XRHapticCommandGenerator_A _haptic;

        private float _lastGripStrength = -1f;
        private int   _totalEvals;
        private int   _redundantEvals;

        /// <summary>Normalised grip strength (0 = no contact, 1 = full grip).</summary>
        public float GripStrength { get; private set; }

        /// <summary>True when grip strength exceeds the grab threshold.</summary>
        public bool IsGrabbed { get; private set; }

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        /// <summary>Initialise with the proximity sensor on the same interactable object.</summary>
        public void Initialize(XRHandProximitySensor sensor)
        {
            _sensor = sensor;
        }

        /// <summary>
        /// Called every frame for ALL N objects by VRBenchmarkController.
        /// This is the defining cost of polling: re-evaluating grip state even when
        /// the hand has not moved and no interaction is occurring.
        /// </summary>
        public void ManualTick()
        {
            _totalEvals++;

            // Proximity 0 = contact (grip=1), proximity 100 = far (grip=0)
            float gripStrength = 1f - (_sensor.RawValue / 100f);

            if (Mathf.Approximately(gripStrength, _lastGripStrength))
            {
                _redundantEvals++;
                // Polling must still drive downstream because it cannot know if they read the value
                _hud?.ManualTick(gripStrength);
                _haptic?.ManualTick(gripStrength);
                return;
            }

            _lastGripStrength = gripStrength;
            GripStrength      = gripStrength;
            IsGrabbed         = gripStrength > GrabThreshold;

            _hud?.ManualTick(gripStrength);
            _haptic?.ManualTick(gripStrength);
        }
    }
}
