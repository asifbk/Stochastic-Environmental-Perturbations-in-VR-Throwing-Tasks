using UnityEngine;

namespace RCG.Benchmark.VRPipeline
{
    /// <summary>
    /// VR Pipeline — Proximity HUD Display (System A: Update polling).
    ///
    /// Formats grip strength as a world-space label string ("GRAB 87%", "NEAR 23%", "----").
    /// The string allocation on every ManualTick() call is the GC pressure being measured —
    /// identical to A_UIDisplay but with XR-domain label semantics.
    ///
    /// Also drives the interactable object's highlight colour via its Renderer, making
    /// the polling cost visually observable in the scene: objects flash/update colour
    /// every frame even when no interaction is happening.
    /// </summary>
    public sealed class XRProximityHUD_A : MonoBehaviour, IMetricsProvider
    {
        private const float GrabThreshold = 0.25f;
        private const float NearThreshold = 0.05f;

        private XRGrabStateDetector_A _detector;
        private Renderer              _renderer;
        private string                _displayText = "";
        private string                _lastText    = "";
        private int                   _totalEvals;
        private int                   _redundantEvals;

        private static readonly Color ColorFar     = new Color(0.2f, 0.2f, 0.2f); // dark grey
        private static readonly Color ColorNear    = new Color(0.9f, 0.7f, 0.1f); // amber
        private static readonly Color ColorGrabbed = new Color(0.1f, 0.9f, 0.2f); // green

        public int TotalEvaluations     => _totalEvals;
        public int RedundantEvaluations => _redundantEvals;
        public void ResetMetrics()      { _totalEvals = 0; _redundantEvals = 0; }

        public void Initialize(XRGrabStateDetector_A detector, Renderer renderer)
        {
            _detector         = detector;
            _renderer         = renderer;
            _detector._hud    = this;
        }

        /// <summary>
        /// Called by XRGrabStateDetector_A.ManualTick() every frame for ALL objects.
        /// String formatting and colour assignment happen unconditionally.
        /// </summary>
        public void ManualTick(float gripStrength)
        {
            _totalEvals++;

            // Format label — string allocation every call (the GC work being measured)
            if (gripStrength > GrabThreshold)
                _displayText = $"GRAB {Mathf.RoundToInt(gripStrength * 100):00}%";
            else if (gripStrength > NearThreshold)
                _displayText = $"NEAR {Mathf.RoundToInt(gripStrength * 100):00}%";
            else
                _displayText = "----";

            if (_displayText == _lastText)
            {
                _redundantEvals++;
                return;
            }

            _lastText = _displayText;

            // Update highlight colour — visually shows which objects are active this frame
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
