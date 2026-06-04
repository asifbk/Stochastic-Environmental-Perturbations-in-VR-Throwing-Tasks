using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Basketball
{
    /// <summary>
    /// Drives a structured UGUI panel with three rows — angle bar, speed bar, and
    /// outcome badge — populated from pre-LLM kinematic data so it appears
    /// instantly on shot commit, before the model response arrives.
    /// </summary>
    public class FeedbackMetricsPanel : MonoBehaviour
    {
        // Reuse the same threshold constants as AICoach.BuildPrompt().
        private const float AngleFaultThresholdDeg = 3f;
        private const float SpeedFaultThresholdMs  = 0.5f;

        // Bar scales — values beyond these are "catastrophic" and fill the bar completely.
        private const float AngleBarMaxDeg = 15f;
        private const float SpeedBarMaxMs  = 3f;

        private static readonly Color ColorOk     = Color.green;
        private static readonly Color ColorWarn   = new Color(1f, 0.75f, 0f);
        private static readonly Color ColorBad    = Color.red;

        [SerializeField] private GameObject      panel;
        [SerializeField] private Image           angleBarFill;
        [SerializeField] private TextMeshProUGUI angleLabel;
        [SerializeField] private Image           speedBarFill;
        [SerializeField] private TextMeshProUGUI speedLabel;
        [SerializeField] private TextMeshProUGUI outcomeBadge;

        /// <summary>
        /// Populates and shows all panel rows.
        /// </summary>
        /// <param name="angleDelta">idealAngle − actualAngle (positive → shot too flat, negative → too steep).</param>
        /// <param name="speedDelta">idealSpeed − actualSpeed (positive → too slow, negative → too fast).</param>
        /// <param name="outcome">Shot outcome string: "Score", "Rim", or "Miss".</param>
        public void Show(float angleDelta, float speedDelta, string outcome)
        {
            float angleMag = Mathf.Abs(angleDelta);
            float speedMag = Mathf.Abs(speedDelta);

            // ── Angle bar ────────────────────────────────────────────────────────
            if (angleBarFill != null)
            {
                angleBarFill.fillAmount = Mathf.Clamp01(angleMag / AngleBarMaxDeg);
                angleBarFill.color      = MetricColor(angleMag, AngleFaultThresholdDeg);
            }

            if (angleLabel != null)
            {
                if (angleMag < AngleFaultThresholdDeg)
                    angleLabel.text = "OK";
                else if (angleDelta > 0f)
                    angleLabel.text = $"\u2191 too flat {angleMag:F1}\u00B0";
                else
                    angleLabel.text = $"\u2193 too steep {angleMag:F1}\u00B0";
            }

            // ── Speed bar ────────────────────────────────────────────────────────
            if (speedBarFill != null)
            {
                speedBarFill.fillAmount = Mathf.Clamp01(speedMag / SpeedBarMaxMs);
                speedBarFill.color      = MetricColor(speedMag, SpeedFaultThresholdMs);
            }

            if (speedLabel != null)
            {
                if (speedMag < SpeedFaultThresholdMs)
                    speedLabel.text = "OK";
                else if (speedDelta > 0f)
                    speedLabel.text = $"too slow {speedMag:F1} m/s";
                else
                    speedLabel.text = $"too fast {speedMag:F1} m/s";
            }

            // ── Outcome badge ────────────────────────────────────────────────────
            if (outcomeBadge != null)
            {
                switch (outcome)
                {
                    case "Score":
                        outcomeBadge.text  = "SCORE";
                        outcomeBadge.color = ColorOk;
                        break;
                    case "Rim":
                        outcomeBadge.text  = "RIM";
                        outcomeBadge.color = ColorWarn;
                        break;
                    default:
                        outcomeBadge.text  = "MISS";
                        outcomeBadge.color = ColorBad;
                        break;
                }
            }

            if (panel != null) panel.SetActive(true);
        }

        /// <summary>Hides the metrics panel.</summary>
        public void Hide()
        {
            if (panel != null) panel.SetActive(false);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns green when within threshold, yellow when 1–2× threshold, red when > 2×.
        /// </summary>
        private static Color MetricColor(float magnitude, float threshold)
        {
            if (magnitude < threshold)               return ColorOk;
            if (magnitude < threshold * 2f)          return ColorWarn;
            return ColorBad;
        }
    }
}
