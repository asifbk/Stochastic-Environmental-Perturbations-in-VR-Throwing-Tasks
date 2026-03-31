using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — Proximity HUD Display (System B: Manual C# Events).
    ///
    /// Subscribes to XRGrabStateDetector_B.OnGripStrengthChanged.
    /// Only evaluates when grip strength actually changed — avoiding the per-frame
    /// polling cost of System A — but requires manual unsubscription lifecycle.
    /// </summary>
    public sealed class XRProximityHUD_B : MonoBehaviour, IMetricsProvider
    {
        private const float GrabThreshold = 0.25f;
        private const float NearThreshold = 0.05f;

        private XRGrabStateDetector_B _detector;
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

        public void Initialize(XRGrabStateDetector_B detector, Renderer renderer)
        {
            _detector = detector;
            _renderer = renderer;
            _detector.OnGripStrengthChanged += OnGripStrengthChanged;
        }

        private void OnDestroy()
        {
            if (_detector != null) _detector.OnGripStrengthChanged -= OnGripStrengthChanged;
        }

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
