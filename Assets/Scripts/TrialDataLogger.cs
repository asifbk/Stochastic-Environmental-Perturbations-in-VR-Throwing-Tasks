using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Logs per-trial data to a CSV file for offline statistical analysis.
    /// Each row represents one throw attempt with kinematic, environmental, and outcome data.
    ///
    /// CSV columns:
    ///   TrialNumber, Timestamp, Hand, ReleaseVelocityX, ReleaseVelocityY, ReleaseVelocityZ,
    ///   ReleaseSpeedMs, ReleaseAngleDeg, WindSpeedMs, WindSpeedKmh, WindAngleDeg,
    ///   WindDirectionX, WindDirectionZ, EntrySpeedMs, Outcome
    /// </summary>
    public class TrialDataLogger : MonoBehaviour
    {
        [Header("Data Sources")]
        [SerializeField] private HandThrow handThrow;
        [SerializeField] private BallThrower ballThrower;
        [SerializeField] private ScoringTrigger scoringTrigger;
        [SerializeField] private WindSystem windSystem;

        [Header("Settings")]
        [Tooltip("Seconds to wait for a score event after a throw before marking as Miss.")]
        [SerializeField] [Min(1f)] private float scoreWindowSeconds = 5f;
        [Tooltip("Participant ID written into the filename for experiment bookkeeping.")]
        [SerializeField] private string participantId = "P00";

        private const string CsvHeader =
            "TrialNumber,Timestamp,Hand,ReleaseVelocityX,ReleaseVelocityY,ReleaseVelocityZ," +
            "ReleaseSpeedMs,ReleaseAngleDeg," +
            "WindSpeedMs,WindSpeedKmh,WindAngleDeg,WindDirectionX,WindDirectionZ," +
            "EntrySpeedMs,Outcome";

        // Pending trial state
        private int _trialNumber;
        private bool _awaitingScore;
        private TrialRecord _pendingRecord;
        private StreamWriter _writer;
        private float _sessionStartTime;

        private void Start()
        {
            _sessionStartTime = Time.time;
            OpenFile();

            if (handThrow != null)
                handThrow.OnBallReleased += OnHandBallReleased;

            // Also support keyboard throws from BallThrower
            if (ballThrower != null)
                ballThrower.OnShotFired += OnKeyboardShotFired;

            if (scoringTrigger != null)
                scoringTrigger.OnScored += OnScored;
        }

        private void OnDestroy()
        {
            if (handThrow != null)
                handThrow.OnBallReleased -= OnHandBallReleased;

            if (ballThrower != null)
                ballThrower.OnShotFired -= OnKeyboardShotFired;

            if (scoringTrigger != null)
                scoringTrigger.OnScored -= OnScored;

            CloseFile();
        }

        // --- Event Handlers ---

        private void OnHandBallReleased(Vector3 releaseVelocity, HandThrow.HandSide side)
        {
            StartTrial(releaseVelocity, side.ToString());
        }

        private void OnKeyboardShotFired()
        {
            // BallThrower computes the velocity; we can only approximate it here.
            // For keyboard throws, release velocity is unknown so we log Vector3.zero.
            StartTrial(Vector3.zero, "Keyboard");
        }

        private void OnScored(GameObject ball, float entrySpeed)
        {
            if (!_awaitingScore) return;

            _pendingRecord.EntrySpeedMs = entrySpeed;
            _pendingRecord.Outcome = "Score";
            _awaitingScore = false;

            FlushRecord(_pendingRecord);
        }

        // --- Trial Lifecycle ---

        /// <summary>Captures all pre-throw data and starts the score wait window.</summary>
        private void StartTrial(Vector3 releaseVelocity, string hand)
        {
            // If a previous trial is still open, flush it as a miss first.
            if (_awaitingScore)
                FlushMiss();

            _trialNumber++;

            float horizontalSpeed = new Vector2(releaseVelocity.x, releaseVelocity.z).magnitude;
            float releaseAngle = horizontalSpeed > 0.001f
                ? Mathf.Atan2(releaseVelocity.y, horizontalSpeed) * Mathf.Rad2Deg
                : 0f;

            Vector3 windDir = windSystem != null
                ? new Vector3(
                    Mathf.Cos(windSystem.WindAngleDeg * Mathf.Deg2Rad),
                    0f,
                    Mathf.Sin(windSystem.WindAngleDeg * Mathf.Deg2Rad))
                : Vector3.zero;

            _pendingRecord = new TrialRecord
            {
                TrialNumber    = _trialNumber,
                Timestamp      = Time.time - _sessionStartTime,
                Hand           = hand,
                ReleaseVelocity = releaseVelocity,
                ReleaseSpeedMs = releaseVelocity.magnitude,
                ReleaseAngleDeg = releaseAngle,
                WindSpeedMs    = windSystem != null ? windSystem.WindSpeedMs : 0f,
                WindSpeedKmh   = windSystem != null ? windSystem.WindSpeedKmh : 0f,
                WindAngleDeg   = windSystem != null ? windSystem.WindAngleDeg : 0f,
                WindDirectionX = windDir.x,
                WindDirectionZ = windDir.z,
                EntrySpeedMs   = -1f,
                Outcome        = "Miss"
            };

            _awaitingScore = true;
            StartCoroutine(ScoreWindowCoroutine());
        }

        private IEnumerator ScoreWindowCoroutine()
        {
            yield return new WaitForSeconds(scoreWindowSeconds);

            if (_awaitingScore)
                FlushMiss();
        }

        private void FlushMiss()
        {
            _awaitingScore = false;
            _pendingRecord.Outcome = "Miss";
            FlushRecord(_pendingRecord);
        }

        // --- CSV I/O ---

        private void OpenFile()
        {
            string directory = Application.persistentDataPath;
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = $"basketball_trials_{participantId}_{timestamp}.csv";
            string path = Path.Combine(directory, filename);

            try
            {
                _writer = new StreamWriter(path, append: false, encoding: Encoding.UTF8);
                _writer.WriteLine(CsvHeader);
                _writer.Flush();
                Debug.Log($"[TrialDataLogger] Logging to: {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrialDataLogger] Failed to open log file: {ex.Message}");
            }
        }

        private void FlushRecord(TrialRecord r)
        {
            if (_writer == null) return;

            string line = string.Format(
                "{0},{1:F3},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F2}," +
                "{8:F4},{9:F4},{10:F2},{11:F4},{12:F4}," +
                "{13:F4},{14}",
                r.TrialNumber,
                r.Timestamp,
                r.Hand,
                r.ReleaseVelocity.x, r.ReleaseVelocity.y, r.ReleaseVelocity.z,
                r.ReleaseSpeedMs,
                r.ReleaseAngleDeg,
                r.WindSpeedMs, r.WindSpeedKmh, r.WindAngleDeg,
                r.WindDirectionX, r.WindDirectionZ,
                r.EntrySpeedMs,
                r.Outcome);

            try
            {
                _writer.WriteLine(line);
                _writer.Flush();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrialDataLogger] Write error: {ex.Message}");
            }
        }

        private void CloseFile()
        {
            try
            {
                _writer?.Close();
                _writer = null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TrialDataLogger] Error closing log file: {ex.Message}");
            }
        }

        // --- Data Transfer Object ---

        private struct TrialRecord
        {
            public int TrialNumber;
            public float Timestamp;
            public string Hand;
            public Vector3 ReleaseVelocity;
            public float ReleaseSpeedMs;
            public float ReleaseAngleDeg;
            public float WindSpeedMs;
            public float WindSpeedKmh;
            public float WindAngleDeg;
            public float WindDirectionX;
            public float WindDirectionZ;
            public float EntrySpeedMs;
            public string Outcome;
        }
    }
}
