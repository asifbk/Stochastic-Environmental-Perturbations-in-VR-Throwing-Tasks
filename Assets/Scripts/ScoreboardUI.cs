using TMPro;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Reads game state from ScoringTrigger, BallThrower, and WindSystem, then
    /// drives all TextMeshPro elements on the in-world scoreboard every frame.
    /// </summary>
    public class ScoreboardUI : MonoBehaviour
    {
        private const int PointsPerBasket = 2;

        [Header("Game Systems")]
        [SerializeField] private ScoringTrigger scoringTrigger;
        [SerializeField] private BallThrower ballThrower;
        [SerializeField] private WindSystem windSystem;

        [Header("Score Panel")]
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private TextMeshProUGUI shotsText;
        [SerializeField] private TextMeshProUGUI accuracyText;

        [Header("Stats Panel")]
        [SerializeField] private TextMeshProUGUI windSpeedText;
        [SerializeField] private TextMeshProUGUI entrySpeedText;
        [SerializeField] private TextMeshProUGUI lastBallText;

        private int _totalScore;
        private float _lastEntrySpeed;
        private string _lastBallName = "-";

        private void OnEnable()
        {
            if (scoringTrigger != null)
            {
                scoringTrigger.OnScored += HandleScored;
            }
        }

        private void OnDisable()
        {
            if (scoringTrigger != null)
            {
                scoringTrigger.OnScored -= HandleScored;
            }
        }

        private void Update()
        {
            RefreshDisplay();
        }

        private void HandleScored(GameObject ball, float entrySpeed, Vector3 entryVelocity)
        {
            _totalScore += PointsPerBasket;
            _lastEntrySpeed = entrySpeed;
            _lastBallName = ball.name;
        }

        /// <summary>
        /// Writes the latest values into every text element on the scoreboard.
        /// </summary>
        private void RefreshDisplay()
        {
            int totalShots = ballThrower != null ? ballThrower.TotalShots : 0;
            int baskets = _totalScore / PointsPerBasket;
            float accuracy = totalShots > 0 ? baskets / (float)totalShots * 100f : 0f;

            if (scoreText != null)
            {
                scoreText.text = $"SCORE\n{_totalScore}";
            }

            if (shotsText != null)
            {
                shotsText.text = $"SHOTS\n{totalShots}";
            }

            if (accuracyText != null)
            {
                accuracyText.text = totalShots > 0
                    ? $"ACCURACY\n{accuracy:F1}%"
                    : "ACCURACY\n\u2014";
            }

            if (windSpeedText != null && windSystem != null)
            {
                windSpeedText.text = $"WIND\n{windSystem.WindSpeedKmh:F1} km/h\n{windSystem.WindCardinal()}";
            }

            if (entrySpeedText != null)
            {
                entrySpeedText.text = $"ENTRY SPEED\n{_lastEntrySpeed:F2} m/s";
            }

            if (lastBallText != null)
            {
                lastBallText.text = $"LAST SCORE\n{_lastBallName}";
            }
        }
    }
}
