using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Basketball
{
    /// <summary>
    /// AI basketball coach powered by a local Ollama vision model (llava).
    ///
    /// Trigger chain:
    ///   1. HandThrow.OnBallReleased  — records release kinematics and opens an outcome window.
    ///   2. ScoringTrigger.OnScored   — marks the pending shot as a Score within the window.
    ///   3. ScoringTrigger.OnRimHit   — marks the pending shot as a Rim hit within the window.
    ///   4. Space key                 — manual trigger for editor testing.
    /// After the outcome window closes, the shot is committed to history and the model is queried.
    /// </summary>
    [RequireComponent(typeof(OllamaVLMClient))]
    public class AICoach : MonoBehaviour
    {
        private const int MaxShotHistory = 5;

        // ─── Inspector ────────────────────────────────────────────────────────────

        [Header("Game Systems")]
        [SerializeField] private ScoringTrigger scoringTrigger;
        [SerializeField] private HandThrow      handThrow;
        [SerializeField] private WindSystem     windSystem;

        [Header("Court Reference")]
        [Tooltip("Transform at the centre of the hoop opening (HoopScoreTrigger GameObject).")]
        [SerializeField] private Transform hoopTransform;

        [Tooltip("Player head/body transform used to place the floor marker at the player's feet. " +
                 "Assign the VR Camera (e.g. [CameraRig]/Camera).")]
        [SerializeField] private Transform playerTransform;

        [Header("Balls")]
        [Tooltip("All basketball Rigidbodies — used to sample velocity when SenseGlove release event is unavailable.")]
        [SerializeField] private Rigidbody[] ballRigidbodies;

        [Header("Vision")]
        [Tooltip("Leave this empty. Vision mode (screenshot) adds significant SSH tunnel latency " +
                 "and is disabled — OllamaVLMClient now runs text-only for fastest response.")]
        [SerializeField] private Camera visionCamera;

        [Header("Coach UI")]
        [SerializeField] private TextMeshProUGUI coachText;

        [Header("Coach Visuals")]
        [SerializeField] private CoachVisuals coachVisuals;

        [Header("Text-to-Speech")]
        [Tooltip("Optional CoachTTSClient component on this GameObject. When assigned the coach will speak its response aloud.")]
        [SerializeField] private CoachTTSClient coachTTS;

        [Header("Timing")]
        [SerializeField] private float feedbackDisplaySeconds = 10f;
        [SerializeField] private float outcomeWaitSeconds     = 2f;

        // ─── Private State ────────────────────────────────────────────────────────

        private ShotRecord _pending;
        private bool       _awaitingOutcome;
        private int        _shotCount;

        private Coroutine _outcomeCoroutine;
        private Coroutine _hideCoroutine;

        /// <summary>True only after CommitShot() has fully resolved the outcome window for the current shot.</summary>
        private bool _shotCommitted;

        /// <summary>
        /// Set to true when a real throw is initiated (SenseGlove release or AutoShot T-key).
        /// Prevents wind-drifted balls or stray physics events from triggering the coach pipeline
        /// without an actual intentional throw.
        /// </summary>
        private bool _throwInitiated;

        /// <summary>
        /// Holds the prompt for the most recent shot that arrived while the client was busy.
        /// Flushed and sent as soon as the client becomes free in OnModelResponse.
        /// Only the latest shot is kept — intermediate shots are overwritten.
        /// </summary>
        private string _pendingPrompt;

        private readonly Queue<ShotRecord> _history = new Queue<ShotRecord>();

        private OllamaVLMClient _client;

        // ─── Shot Record ──────────────────────────────────────────────────────────

        private struct ShotRecord
        {
            public int     ShotNumber;
            public Vector3 ReleasePosition;
            public float   ReleaseSpeedMs;
            public float   ReleaseAngleDeg;
            public Vector3 ReleaseVelocity;
            public float   GrabDurationSec;
            public float[] FingerFlexion;
            public float   WindSpeedMs;
            public string  WindCardinal;
            public float   WindAngleDeg;
            public float   EntrySpeedMs;
            public float   EntryAngleDeg;
            public float   RimImpactSpeedMs;
            public string  Outcome;
        }

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _client = GetComponent<OllamaVLMClient>();
            Debug.Log("[AICoach] Awake.");
        }

        private void Start()
        {
            Debug.Log($"[AICoach] Start — handThrow={handThrow}, scoringTrigger={scoringTrigger}, hoopTransform={hoopTransform}, coachText={coachText}");
            if (handThrow      == null) Debug.LogError("[AICoach] handThrow is not assigned.");
            if (scoringTrigger == null) Debug.LogError("[AICoach] scoringTrigger is not assigned.");
            if (hoopTransform  == null) Debug.LogWarning("[AICoach] hoopTransform is not assigned — court geometry will be omitted from the prompt.");
            if (coachText      == null) Debug.LogError("[AICoach] coachText is not assigned.");

            // Hide any stale visuals left over from a previous session.
            ClearFeedback();

            // Subscribe here (after all Awakes have run) instead of OnEnable,
            // so we are guaranteed the events exist on the source components.
            SubscribeToEvents();
        }

        private void OnEnable()
        {
            // Only re-subscribe after the first Start() has already run once.
            if (_client != null)
                SubscribeToEvents();
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
        }

        private bool _subscribed;

        private void SubscribeToEvents()
        {
            if (_subscribed) return;
            _subscribed = true;

            if (handThrow != null)
                handThrow.OnBallReleased += OnBallReleased;

            if (scoringTrigger != null)
            {
                scoringTrigger.OnScored += OnScored;
                scoringTrigger.OnRimHit += OnRimHit;
            }

            Debug.Log("[AICoach] Subscribed to events.");
        }

        private void UnsubscribeFromEvents()
        {
            if (!_subscribed) return;
            _subscribed = false;

            if (handThrow != null)
                handThrow.OnBallReleased -= OnBallReleased;

            if (scoringTrigger != null)
            {
                scoringTrigger.OnScored -= OnScored;
                scoringTrigger.OnRimHit -= OnRimHit;
            }
        }

        private void Update()
        {
            // Space key: manual trigger for editor testing — only fires if at least one
            // real shot has been committed to history so the prompt has meaningful data.
#if UNITY_EDITOR
#if ENABLE_INPUT_SYSTEM
            bool spaceDown = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
            bool spaceDown = UnityEngine.Input.GetKeyDown(KeyCode.Space);
#endif
            if (spaceDown)
            {
                if (_history.Count == 0)
                {
                    Debug.LogWarning("[AICoach] Space pressed but no shots in history yet — throw the ball first.");
                    if (coachText != null)
                        coachText.text = "Throw the ball first!";
                    return;
                }

                Debug.Log("[AICoach] Manual query triggered via Space key.");
                QueryModel();
            }
#endif
        }

        // ─── Event Handlers ───────────────────────────────────────────────────────

        /// <summary>
        /// Call this from AutoShot (T-key debug launch) immediately before the ball is fired.
        /// Marks the upcoming scoring/rim event as intentional so the coach pipeline activates.
        /// </summary>
        public void NotifyIntentionalThrow() => _throwInitiated = true;

        /// <summary>
        /// Called by AutoShot immediately after the ball is launched.
        /// Opens the shot record and outcome window so a miss is correctly handled
        /// even without a SenseGlove HandThrow release event.
        /// </summary>
        public void NotifyAutoShotLaunched(Vector3 releasePosition, Vector3 releaseVelocity,
                                           Rigidbody ball = null)
        {
            _throwInitiated = true;

            // If a previous outcome window is still open, commit it first.
            if (_awaitingOutcome && _outcomeCoroutine != null)
            {
                StopCoroutine(_outcomeCoroutine);
                CommitShot();
            }

            _shotCount++;

            float horizontalSpeed = new Vector2(releaseVelocity.x, releaseVelocity.z).magnitude;
            float releaseAngleDeg = horizontalSpeed > 0.001f
                ? Mathf.Atan2(releaseVelocity.y, horizontalSpeed) * Mathf.Rad2Deg
                : 0f;

            _pending = new ShotRecord
            {
                ShotNumber       = _shotCount,
                ReleasePosition  = releasePosition,
                ReleaseVelocity  = releaseVelocity,
                ReleaseSpeedMs   = releaseVelocity.magnitude,
                ReleaseAngleDeg  = releaseAngleDeg,
                GrabDurationSec  = -1f,
                FingerFlexion    = null,
                WindSpeedMs      = windSystem != null ? windSystem.WindSpeedMs   : 0f,
                WindCardinal     = windSystem != null ? windSystem.WindCardinal() : "none",
                WindAngleDeg     = windSystem != null ? windSystem.WindAngleDeg  : 0f,
                EntrySpeedMs     = -1f,
                EntryAngleDeg    = -1f,
                RimImpactSpeedMs = -1f,
                Outcome          = "Miss"
            };

            _awaitingOutcome  = true;
            _outcomeCoroutine = StartCoroutine(OutcomeWindow());
            _shotCommitted    = false;

            ClearFeedback();
            coachVisuals?.StartTracking(ball, hoopTransform != null ? hoopTransform.position : Vector3.zero);
            Debug.Log($"[AICoach] AutoShot #{_shotCount} — speed {_pending.ReleaseSpeedMs:F2} m/s, angle {_pending.ReleaseAngleDeg:F1}°. Waiting {outcomeWaitSeconds}s for outcome.");
        }

        private void OnBallReleased(Vector3 releaseVelocity, HandThrow.HandSide side,
                                    Vector3 releasePosition, float grabDuration, float[] fingerFlexion,
                                    Rigidbody ball)
        {
            _throwInitiated = true;
            Debug.Log($"[AICoach] OnBallReleased — hand={side}, speed={releaseVelocity.magnitude:F2} m/s. Coach pipeline starting.");
            if (_awaitingOutcome && _outcomeCoroutine != null)
            {
                StopCoroutine(_outcomeCoroutine);
                CommitShot();
            }

            _shotCount++;

            float horizontalSpeed = new Vector2(releaseVelocity.x, releaseVelocity.z).magnitude;
            float releaseAngleDeg = horizontalSpeed > 0.001f
                ? Mathf.Atan2(releaseVelocity.y, horizontalSpeed) * Mathf.Rad2Deg
                : 0f;

            _pending = new ShotRecord
            {
                ShotNumber      = _shotCount,
                ReleasePosition = releasePosition,
                ReleaseVelocity = releaseVelocity,
                ReleaseSpeedMs  = releaseVelocity.magnitude,
                ReleaseAngleDeg = releaseAngleDeg,
                GrabDurationSec = grabDuration,
                FingerFlexion   = fingerFlexion,
                WindSpeedMs     = windSystem != null ? windSystem.WindSpeedMs  : 0f,
                WindCardinal    = windSystem != null ? windSystem.WindCardinal() : "none",
                WindAngleDeg    = windSystem != null ? windSystem.WindAngleDeg : 0f,
                EntrySpeedMs    = -1f,
                EntryAngleDeg   = -1f,
                RimImpactSpeedMs = -1f,
                Outcome         = "Miss"
            };

            _awaitingOutcome  = true;
            _outcomeCoroutine = StartCoroutine(OutcomeWindow());
            _shotCommitted    = false;

            ClearFeedback();
            coachVisuals?.StartTracking(ball, hoopTransform != null ? hoopTransform.position : Vector3.zero);
            Debug.Log($"[AICoach] Shot #{_shotCount} — speed {_pending.ReleaseSpeedMs:F2} m/s, angle {_pending.ReleaseAngleDeg:F1}°. Waiting {outcomeWaitSeconds}s for outcome.");
        }

        private void OnScored(GameObject ball, float entrySpeed, Vector3 entryVelocity)
        {
            // Ignore scoring events that were not preceded by a real throw.
            if (!_throwInitiated) return;

            // If no throw was detected, synthesise a shot record from the ball's current state.
            if (!_awaitingOutcome)
            {
                Debug.Log("[AICoach] OnScored received without a prior release — synthesising shot record.");
                BeginShotFromBall(ball);
            }

            _pending.EntrySpeedMs  = entrySpeed;
            _pending.EntryAngleDeg = Vector3.Angle(entryVelocity, Vector3.down);
            _pending.Outcome       = "Score";

            Debug.Log($"[AICoach] Shot #{_pending.ShotNumber} outcome = Score, entry {entrySpeed:F2} m/s.");
        }

        private void OnRimHit(GameObject ball, float impactSpeed)
        {
            // Ignore rim hits that were not preceded by a real throw.
            if (!_throwInitiated) return;

            if (!_awaitingOutcome)
            {
                Debug.Log("[AICoach] OnRimHit received without a prior release — synthesising shot record.");
                BeginShotFromBall(ball);
            }

            _pending.RimImpactSpeedMs = impactSpeed;
            if (_pending.Outcome != "Score")
                _pending.Outcome = "Rim";

            Debug.Log($"[AICoach] Shot #{_pending.ShotNumber} outcome = {_pending.Outcome}, rim {impactSpeed:F2} m/s.");
        }

        /// <summary>
        /// Opens a new shot record synthesised from the ball's Rigidbody velocity.
        /// Used when OnScored/OnRimHit fire without a preceding HandThrow release event.
        /// </summary>
        private void BeginShotFromBall(GameObject ball)
        {
            _shotCount++;

            Rigidbody rb = ball != null ? ball.GetComponent<Rigidbody>() : null;
            Vector3 vel  = rb != null ? rb.velocity : Vector3.zero;
            float hSpeed = new Vector2(vel.x, vel.z).magnitude;

            _pending = new ShotRecord
            {
                ShotNumber       = _shotCount,
                ReleasePosition  = rb != null ? rb.position : Vector3.zero,
                ReleaseVelocity  = vel,
                ReleaseSpeedMs   = vel.magnitude,
                ReleaseAngleDeg  = hSpeed > 0.001f ? Mathf.Atan2(vel.y, hSpeed) * Mathf.Rad2Deg : 0f,
                GrabDurationSec  = -1f,
                FingerFlexion    = null,
                WindSpeedMs      = windSystem != null ? windSystem.WindSpeedMs   : 0f,
                WindCardinal     = windSystem != null ? windSystem.WindCardinal() : "none",
                WindAngleDeg     = windSystem != null ? windSystem.WindAngleDeg  : 0f,
                EntrySpeedMs     = -1f,
                EntryAngleDeg    = -1f,
                RimImpactSpeedMs = -1f,
                Outcome          = "Miss"
            };

            _awaitingOutcome  = true;
            _outcomeCoroutine = StartCoroutine(OutcomeWindow());
            _shotCommitted    = false;

            ClearFeedback();
        }

        // ─── Outcome Window ───────────────────────────────────────────────────────

        private IEnumerator OutcomeWindow()
        {
            yield return new WaitForSeconds(outcomeWaitSeconds);
            CommitShot();
        }

        /// <summary>Locks the pending record, adds it to history, and fires the model query.</summary>
        private void CommitShot()
        {
            _awaitingOutcome = false;
            _shotCommitted   = true;
            _throwInitiated  = false;   // require a new throw before the next coaching cycle

            _history.Enqueue(_pending);
            if (_history.Count > MaxShotHistory)
                _history.Dequeue();

            // Show visual guidance based on pre-computed physics — no LLM parsing needed.
            if (coachVisuals != null && hoopTransform != null)
            {
                coachVisuals.StopTracking();
                (float idealAngle, float idealSpeed) = ComputeIdealShot(_pending.ReleasePosition);
                Vector3 playerPos = playerTransform != null
                    ? playerTransform.position
                    : _pending.ReleasePosition;
                coachVisuals.ShowGuidance(playerPos, _pending.ReleasePosition,
                                          hoopTransform.position, idealAngle, idealSpeed);
            }

            QueryModel();
        }

        // ─── Model Query ──────────────────────────────────────────────────────────

        /// <summary>Builds the prompt and sends it to the active vision model.</summary>
        public void QueryModel()
        {
            // Guard with _shotCommitted to prevent stale or partial pending records
            // from triggering this path before the outcome window has closed.
            if (!_shotCommitted) return;

            // Always update the UI immediately so the previous shot's text never
            // bleeds into the current shot (e.g. old swish message persisting).
            if (coachText != null)
                coachText.text = "Coach is thinking...";

            string prompt = BuildPrompt();

            if (_client.IsBusy)
            {
                // Store the latest prompt so OnModelResponse can send it once the
                // current request completes. This prevents shots from being silently
                // dropped when Ollama is still processing the previous response.
                _pendingPrompt = prompt;
                Debug.LogWarning("[AICoach] Client is busy — queuing prompt for next available slot.");
                return;
            }

            _pendingPrompt = null;
            SendPrompt(prompt);
        }

        /// <summary>
        /// Dispatches the prompt to the client using streaming text mode.
        /// Vision/screenshot is intentionally disabled — image encoding and base64
        /// transmission over an SSH tunnel is the single biggest latency source.
        /// All coaching context is embedded in the text prompt instead.
        /// </summary>
        private void SendPrompt(string prompt)
        {
            _streamBuffer = string.Empty;
            _client.SendStreamingTextRequest(prompt, OnModelToken, OnModelResponse);
        }

        /// <summary>Accumulates streamed tokens and updates the UI on each one for instant perceived feedback.</summary>
        private string _streamBuffer = string.Empty;

        private void OnModelToken(string token)
        {
            _streamBuffer += token;
            if (coachText != null)
                coachText.text = _streamBuffer;
        }

        // ─── Prompt Builder ───────────────────────────────────────────────────────

        private const float AngleFaultThresholdDeg   = 3f;
        private const float SpeedFaultThresholdMs    = 0.5f;
        private const int   ConsecutiveMissThreshold = 3;
        private const int   TrendMinShots            = 3;

        private string BuildPrompt()
        {
            // Pre-compute corrections so the LLM receives pre-digested facts, not raw numbers.
            // This removes any arithmetic the model would otherwise spend tokens on.
            (float idealAngle, float idealSpeed) = ComputeIdealShot(_pending.ReleasePosition);

            float  angleDelta = idealAngle - _pending.ReleaseAngleDeg;
            float  speedDelta = idealSpeed - _pending.ReleaseSpeedMs;

            string angleFault = Mathf.Abs(angleDelta) < AngleFaultThresholdDeg ? "OK"
                              : angleDelta > 0f                                  ? $"flat {angleDelta:F1}°"
                                                                                 : $"steep {Mathf.Abs(angleDelta):F1}°";

            string speedFault = Mathf.Abs(speedDelta) < SpeedFaultThresholdMs ? "OK"
                              : speedDelta > 0f                                 ? $"slow {speedDelta:F1}m/s"
                                                                                : $"fast {Mathf.Abs(speedDelta):F1}m/s";

            int  consecutiveMisses = CountConsecutiveMisses();
            bool persistentlyFlat  = IsConsistentAngleFault(positive: true);
            bool persistentlySteep = IsConsistentAngleFault(positive: false);
            bool persistentlySlow  = IsConsistentSpeedFault(positive: true);
            bool persistentlyFast  = IsConsistentSpeedFault(positive: false);

            // Ultra-compact prompt for fastest response on SSH tunnel
            var sb = new StringBuilder();

            sb.Append($"#{_pending.ShotNumber} {_pending.Outcome}. ");

            if (hoopTransform != null)
            {
                Vector3 toHoop = hoopTransform.position - _pending.ReleasePosition;
                float   hDist  = new Vector2(toHoop.x, toHoop.z).magnitude;
                sb.Append($"{hDist:F0}m {_pending.ReleaseAngleDeg:F0}°/{_pending.ReleaseSpeedMs:F1}m/s ({angleFault}, {speedFault}). ");
            }

            // Trend context — only append when meaningful
            if (consecutiveMisses >= ConsecutiveMissThreshold)
                sb.Append($"{consecutiveMisses} miss streak. ");
            if (persistentlyFlat || persistentlySteep || persistentlySlow || persistentlyFast)
            {
                sb.Append("Trend: ");
                if (persistentlyFlat)  sb.Append("flat ");
                if (persistentlySteep) sb.Append("steep ");
                if (persistentlySlow)  sb.Append("slow ");
                if (persistentlyFast)  sb.Append("hard ");
                sb.Append(". ");
            }

            // Outcome-specific coaching instruction
            if (IsSwish(_pending))
                sb.Append("Perfect swish! Confirm form.");
            else if (IsRimIn(_pending))
                sb.Append("Rim hit—one fix?");
            else
                sb.Append("Miss. How adjust?");

            return sb.ToString();
        }

        // ─── Trend Helpers ────────────────────────────────────────────────────────

        /// <summary>Counts how many of the most recent committed shots were misses (not Score).</summary>
        private int CountConsecutiveMisses()
        {
            int count = _pending.Outcome != "Score" ? 1 : 0;
            foreach (ShotRecord r in _history)
            {
                if (r.Outcome != "Score") count++;
                else break;
            }
            return count;
        }

        /// <summary>
        /// Returns true when at least <see cref="TrendMinShots"/> history entries share the same
        /// angle-fault direction. <paramref name="positive"/> true = too flat; false = too steep.
        /// </summary>
        private bool IsConsistentAngleFault(bool positive)
        {
            if (_history.Count < TrendMinShots) return false;

            (float idealAngle, _) = ComputeIdealShot(_pending.ReleasePosition);
            int faultCount = 0;
            foreach (ShotRecord r in _history)
            {
                float delta = idealAngle - r.ReleaseAngleDeg;
                if (positive ? delta > AngleFaultThresholdDeg : delta < -AngleFaultThresholdDeg)
                    faultCount++;
            }
            return faultCount >= TrendMinShots;
        }

        /// <summary>
        /// Returns true when at least <see cref="TrendMinShots"/> history entries share the same
        /// speed-fault direction. <paramref name="positive"/> true = too slow; false = too fast.
        /// </summary>
        private bool IsConsistentSpeedFault(bool positive)
        {
            if (_history.Count < TrendMinShots) return false;

            (_, float idealSpeed) = ComputeIdealShot(_pending.ReleasePosition);
            int faultCount = 0;
            foreach (ShotRecord r in _history)
            {
                float delta = idealSpeed - r.ReleaseSpeedMs;
                if (positive ? delta > SpeedFaultThresholdMs : delta < -SpeedFaultThresholdMs)
                    faultCount++;
            }
            return faultCount >= TrendMinShots;
        }

        // ─── Model Response ───────────────────────────────────────────────────────

        private void OnModelResponse(string response)
        {
            if (_hideCoroutine != null)
            {
                StopCoroutine(_hideCoroutine);
                _hideCoroutine = null;
            }

            bool hasResponse = !string.IsNullOrEmpty(response);

            if (coachText != null)
            {
                coachText.text = hasResponse
                    ? response
                    : "[Coach offline — is Ollama running? Check the model name in OllamaVLMClient.]";
            }

            // Speak the response aloud when TTS is configured and there is valid text.
            if (coachTTS != null && hasResponse)
                coachTTS.Speak(response);

            // If a newer shot's prompt arrived while this request was in flight, send it now.
            if (_pendingPrompt != null)
            {
                string prompt = _pendingPrompt;
                _pendingPrompt = null;
                Debug.Log("[AICoach] Flushing queued prompt for missed shot.");
                if (coachText != null)
                    coachText.text = "Coach is thinking...";
                SendPrompt(prompt);
            }
        }

        private IEnumerator HideAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            ClearFeedback();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>Clears the coach panel text and hides visual aids immediately.</summary>
        public void ClearFeedback()
        {
            if (coachText != null)
                coachText.text = string.Empty;

            coachVisuals?.Hide();
            coachTTS?.StopSpeaking();
        }

        // ─── Shot Classification ──────────────────────────────────────────────────

        /// <summary>
        /// A swish is a clean score with no rim contact before entry.
        /// </summary>
        private static bool IsSwish(ShotRecord r) =>
            r.Outcome == "Score" && r.RimImpactSpeedMs < 0f;

        /// <summary>
        /// A rim-in is a score where the ball struck the rim at least once before going through.
        /// </summary>
        private static bool IsRimIn(ShotRecord r) =>
            r.Outcome == "Score" && r.RimImpactSpeedMs >= 0f;

        /// <summary>
        /// Computes the physics-ideal release angle and minimum speed to reach the hoop
        /// from the given release position. Used to drive CoachVisuals without LLM parsing.
        /// </summary>
        private (float idealAngleDeg, float idealSpeedMs) ComputeIdealShot(Vector3 releasePos)
        {
            if (hoopTransform == null) return (45f, 0f);

            Vector3 toHoop         = hoopTransform.position - releasePos;
            float   horizontalDist = new Vector2(toHoop.x, toHoop.z).magnitude;
            float   heightDiff     = hoopTransform.position.y - releasePos.y;

            float idealAngle  = 45f + 0.5f * Mathf.Atan2(heightDiff, horizontalDist) * Mathf.Rad2Deg;
            float theta       = idealAngle * Mathf.Deg2Rad;
            float sinTwoTheta = Mathf.Sin(2f * theta);
            float idealSpeed  = sinTwoTheta > 0.01f
                ? Mathf.Sqrt(9.81f * horizontalDist / sinTwoTheta)
                : 0f;

            return (idealAngle, idealSpeed);
        }
    }
}

