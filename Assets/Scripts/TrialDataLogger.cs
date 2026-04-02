using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Logs per-trial data to a CSV file for offline statistical analysis.
    /// Each row represents one throw attempt with kinematic, environmental, outcome,
    /// and SenseGlove finger-flexion data.
    ///
    /// CSV columns:
    ///   ParticipantId, ConditionLabel, TrialNumber, SessionTimestamp,
    ///   Hand,
    ///   ReleasePosX, ReleasePosY, ReleasePosZ,
    ///   ReleaseHeight,
    ///   ReleaseVelocityX, ReleaseVelocityY, ReleaseVelocityZ,
    ///   ReleaseSpeedMs, ReleaseAngleDeg,
    ///   GrabDurationSec,
    ///   FingerFlexThumb, FingerFlexIndex, FingerFlexMiddle, FingerFlexRing, FingerFlexPinky,
    ///   WindSpeedMs, WindSpeedKmh, WindAngleDeg, WindDirectionX, WindDirectionZ,
    ///   EntrySpeedMs, EntryVelocityX, EntryVelocityY, EntryVelocityZ,
    ///   EntryAngleDeg,
    ///   RimImpactSpeedMs,
    ///   Outcome
    /// </summary>
    public class TrialDataLogger : MonoBehaviour
    {
        // ─── CSV Header ───────────────────────────────────────────────────────────
        private const string CsvHeader =
            "ParticipantId,ConditionLabel,TrialNumber,SessionTimestamp," +
            "Hand," +
            "ReleasePosX,ReleasePosY,ReleasePosZ," +
            "ReleaseHeight," +
            "ReleaseVelocityX,ReleaseVelocityY,ReleaseVelocityZ," +
            "ReleaseSpeedMs,ReleaseAngleDeg," +
            "GrabDurationSec," +
            "FingerFlexThumb,FingerFlexIndex,FingerFlexMiddle,FingerFlexRing,FingerFlexPinky," +
            "WindSpeedMs,WindSpeedKmh,WindAngleDeg,WindDirectionX,WindDirectionZ," +
            "EntrySpeedMs,EntryVelocityX,EntryVelocityY,EntryVelocityZ," +
            "EntryAngleDeg," +
            "RimImpactSpeedMs," +
            "Outcome";

        // ─── Inspector ────────────────────────────────────────────────────────────
        [Header("Data Sources")]
        [SerializeField] private HandThrow handThrow;
        [SerializeField] private BallThrower ballThrower;
        [SerializeField] private ScoringTrigger scoringTrigger;
        [SerializeField] private WindSystem windSystem;

        [Header("Reference Point")]
        [Tooltip("World-space Y used as the floor reference for ReleaseHeight calculation. Typically the court floor level.")]
        [SerializeField] private float floorReferenceY = 0f;

        [Header("Session Metadata")]
        [Tooltip("Participant ID written into every CSV row and the filename.")]
        [SerializeField] private string participantId = "P00";

        [Tooltip("Condition label written into every CSV row (e.g. 'Wind_NoPreview'). Change at runtime via SetCondition().")]
        [SerializeField] private string conditionLabel = "Default";

        [Header("Settings")]
        [Tooltip("Seconds to wait for a score or rim-hit event after a throw before marking as Miss.")]
        [SerializeField] [Min(1f)] private float scoreWindowSeconds = 5f;

        // ─── Private State ────────────────────────────────────────────────────────
        private int          _trialNumber;
        private bool         _awaitingOutcome;
        private TrialRecord  _pendingRecord;
        private StreamWriter _writer;
        private float        _sessionStartTime;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Start()
        {
            _sessionStartTime = Time.time;
            OpenFile();

            if (handThrow != null)
                handThrow.OnBallReleased += OnHandBallReleased;

            if (ballThrower != null)
                ballThrower.OnShotFired += OnAutoShotFired;

            if (scoringTrigger != null)
            {
                scoringTrigger.OnScored += OnScored;
                scoringTrigger.OnRimHit += OnRimHit;
            }
        }

        private void OnDestroy()
        {
            if (handThrow != null)
                handThrow.OnBallReleased -= OnHandBallReleased;

            if (ballThrower != null)
                ballThrower.OnShotFired -= OnAutoShotFired;

            if (scoringTrigger != null)
            {
                scoringTrigger.OnScored -= OnScored;
                scoringTrigger.OnRimHit -= OnRimHit;
            }

            CloseFile();
        }

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>Updates the condition label written into subsequent CSV rows at runtime.</summary>
        public void SetCondition(string label) => conditionLabel = label;

        /// <summary>Updates the participant ID written into subsequent CSV rows and used in filenames.</summary>
        public void SetParticipantId(string id) => participantId = id;

        // ─── Event Handlers ───────────────────────────────────────────────────────

        private void OnHandBallReleased(Vector3 releaseVelocity, HandThrow.HandSide side,
                                        Vector3 releasePosition, float grabDuration,
                                        float[] fingerFlexion)
        {
            StartTrial(releaseVelocity, side.ToString(), releasePosition, grabDuration, fingerFlexion);
        }

        private void OnAutoShotFired()
        {
            // AutoShot computes velocity internally; we log Vector3.zero as release velocity.
            StartTrial(Vector3.zero, "AutoShot", Vector3.zero, -1f, null);
        }

        private void OnScored(GameObject ball, float entrySpeed, Vector3 entryVelocity)
        {
            if (!_awaitingOutcome) return;

            // Entry angle: angle between entry velocity and straight-down (0° = perfectly vertical).
            float entryAngleDeg = Vector3.Angle(entryVelocity, Vector3.down);

            _pendingRecord.EntrySpeedMs  = entrySpeed;
            _pendingRecord.EntryVelocity = entryVelocity;
            _pendingRecord.EntryAngleDeg = entryAngleDeg;
            _pendingRecord.Outcome       = "Score";
            _awaitingOutcome = false;

            FlushRecord(_pendingRecord);
        }

        private void OnRimHit(GameObject ball, float impactSpeed)
        {
            if (!_awaitingOutcome) return;

            // Record rim impact but keep the window open — ball may still score after a bounce.
            _pendingRecord.RimImpactSpeedMs = impactSpeed;
            _pendingRecord.Outcome          = "Rim";
        }

        // ─── Trial Lifecycle ──────────────────────────────────────────────────────

        /// <summary>Captures all pre-throw data and starts the outcome wait window.</summary>
        private void StartTrial(Vector3 releaseVelocity, string hand,
                                 Vector3 releasePosition, float grabDuration,
                                 float[] fingerFlexion)
        {
            if (_awaitingOutcome)
                FlushPending();

            _trialNumber++;

            float horizontalSpeed = new Vector2(releaseVelocity.x, releaseVelocity.z).magnitude;
            float releaseAngleDeg = horizontalSpeed > 0.001f
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
                ParticipantId    = participantId,
                ConditionLabel   = conditionLabel,
                TrialNumber      = _trialNumber,
                SessionTimestamp = Time.time - _sessionStartTime,
                Hand             = hand,
                ReleasePosition  = releasePosition,
                ReleaseHeight    = releasePosition.y - floorReferenceY,
                ReleaseVelocity  = releaseVelocity,
                ReleaseSpeedMs   = releaseVelocity.magnitude,
                ReleaseAngleDeg  = releaseAngleDeg,
                GrabDurationSec  = grabDuration,
                FingerFlexion    = fingerFlexion,
                WindSpeedMs      = windSystem != null ? windSystem.WindSpeedMs  : 0f,
                WindSpeedKmh     = windSystem != null ? windSystem.WindSpeedKmh : 0f,
                WindAngleDeg     = windSystem != null ? windSystem.WindAngleDeg : 0f,
                WindDirectionX   = windDir.x,
                WindDirectionZ   = windDir.z,
                EntrySpeedMs     = -1f,
                EntryVelocity    = Vector3.zero,
                EntryAngleDeg    = -1f,
                RimImpactSpeedMs = -1f,
                Outcome          = "Miss"
            };

            _awaitingOutcome = true;
            StartCoroutine(OutcomeWindowCoroutine());
        }

        private IEnumerator OutcomeWindowCoroutine()
        {
            yield return new WaitForSeconds(scoreWindowSeconds);
            if (_awaitingOutcome)
                FlushPending();
        }

        private void FlushPending()
        {
            _awaitingOutcome = false;
            FlushRecord(_pendingRecord);
        }

        // ─── CSV I/O ──────────────────────────────────────────────────────────────

        private void OpenFile()
        {
            string directory = Application.persistentDataPath;
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename  = $"basketball_trials_{participantId}_{timestamp}.csv";
            string path      = Path.Combine(directory, filename);

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

            // Finger flexion: write -1 per finger if data is unavailable.
            string FlexStr(int i) => r.FingerFlexion != null && i < r.FingerFlexion.Length
                ? r.FingerFlexion[i].ToString("F4")
                : "-1";

            string line = string.Join(",",
                r.ParticipantId,
                r.ConditionLabel,
                r.TrialNumber,
                r.SessionTimestamp.ToString("F3"),
                r.Hand,
                r.ReleasePosition.x.ToString("F4"),
                r.ReleasePosition.y.ToString("F4"),
                r.ReleasePosition.z.ToString("F4"),
                r.ReleaseHeight.ToString("F4"),
                r.ReleaseVelocity.x.ToString("F4"),
                r.ReleaseVelocity.y.ToString("F4"),
                r.ReleaseVelocity.z.ToString("F4"),
                r.ReleaseSpeedMs.ToString("F4"),
                r.ReleaseAngleDeg.ToString("F2"),
                r.GrabDurationSec.ToString("F3"),
                FlexStr(0), FlexStr(1), FlexStr(2), FlexStr(3), FlexStr(4),
                r.WindSpeedMs.ToString("F4"),
                r.WindSpeedKmh.ToString("F4"),
                r.WindAngleDeg.ToString("F2"),
                r.WindDirectionX.ToString("F4"),
                r.WindDirectionZ.ToString("F4"),
                r.EntrySpeedMs.ToString("F4"),
                r.EntryVelocity.x.ToString("F4"),
                r.EntryVelocity.y.ToString("F4"),
                r.EntryVelocity.z.ToString("F4"),
                r.EntryAngleDeg.ToString("F2"),
                r.RimImpactSpeedMs.ToString("F4"),
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

        // ─── Data Transfer Object ─────────────────────────────────────────────────

        private struct TrialRecord
        {
            public string  ParticipantId;
            public string  ConditionLabel;
            public int     TrialNumber;
            public float   SessionTimestamp;
            public string  Hand;
            public Vector3 ReleasePosition;
            public float   ReleaseHeight;
            public Vector3 ReleaseVelocity;
            public float   ReleaseSpeedMs;
            public float   ReleaseAngleDeg;
            public float   GrabDurationSec;
            public float[] FingerFlexion;      // [Thumb, Index, Middle, Ring, Pinky] 0–1
            public float   WindSpeedMs;
            public float   WindSpeedKmh;
            public float   WindAngleDeg;
            public float   WindDirectionX;
            public float   WindDirectionZ;
            public float   EntrySpeedMs;
            public Vector3 EntryVelocity;
            public float   EntryAngleDeg;
            public float   RimImpactSpeedMs;
            public string  Outcome;            // "Score" | "Rim" | "Miss"
        }
    }
}
