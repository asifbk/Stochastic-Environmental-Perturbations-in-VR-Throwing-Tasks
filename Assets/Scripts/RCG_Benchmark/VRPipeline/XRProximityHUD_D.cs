using RCG.Core;
using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — Proximity HUD Display (System D: Reactive Component Graph).
    ///
    /// [ReactsTo("_detector", "GripStrength")] — called only when grip strength changed.
    /// No Update(), no subscription code, no OnDestroy() required.
    /// Zero per-frame cost when the hand has not moved.
    /// </summary>
    public sealed class XRProximityHUD_D : RCGBehaviour, IMetricsProvider
    {
        private const float GrabThreshold = 0.25f;
        private const float NearThreshold = 0.05f;

        private XRGrabStateDetector_D _detector;
        private Renderer              _renderer;
        private string                _lastText = "";
        private int                   _totalEvals;
        private int                   _redundantEvals;

        private static readonly Color ColorFar     = new Color(0.2f, 0.2f, 0.2f);
        private static readonly Color ColorNear    = new Color(0.9f, 0.7f, 0.1f);
        private static readonly Color ColorGrabbed = new Color(0.1f, 0.9f, 0.2f);

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(XRGrabStateDetector_D detector, Renderer renderer)
        {
            _detector = detector;
            _renderer = renderer;
            // RCGBehaviour.Start() wires [ReactsTo] using _detector
        }

        [ReactsTo("_detector", "GripStrength")]
        private void OnGripStrengthChanged(float gripStrength)
        {
            _totalEvals++;

            string displayText;
            if (gripStrength > GrabThreshold)
                displayText = $"GRAB {Mathf.RoundToInt(gripStrength * 100):00}%";
            else if (gripStrength > NearThreshold)
                displayText = $"NEAR {Mathf.RoundToInt(gripStrength * 100):00}%";
            else
                displayText = "----";

            if (displayText == _lastText)
            {
                _redundantEvals++;
                return;
            }

            _lastText = displayText;

            if (_renderer != null)
            {
                Color target = gripStrength > GrabThreshold ? ColorGrabbed
                             : gripStrength > NearThreshold ? ColorNear
                             : ColorFar;
                _renderer.material.color = target;
            }
        }
    }
}
