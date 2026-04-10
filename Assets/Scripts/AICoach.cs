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
        [Tooltip("Assign a Camera to enable vision mode — a screenshot is sent with every query. " +
                 "Leave empty to use text-only mode. Requires a vision-capable Ollama model (e.g. llava).")]
        [SerializeField] private Camera visionCamera;

        [Header("Coach UI")]
        [SerializeField] private TextMeshProUGUI coachText;

        [Header("Coach Visuals")]
        [SerializeField] private CoachVisuals coachVisuals;

        [Header("Timing")]
        [SerializeField] private float feedbackDisplaySeconds = 10f;
        [SerializeField] private float outcomeWaitSeconds     = 2f;

        // ─── Private State ────────────────────────────────────────────────────────

        private ShotRecord _pending;
        private bool       _awaitingOutcome;
        private int        _shotCount;

        private Coroutine _outcomeCoroutine;
        private Coroutine _hideCoroutine;

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

        private void SubscribeToEvents()
        {
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
        }

        // ─── Event Handlers ───────────────────────────────────────────────────────

        private void OnBallReleased(Vector3 releaseVelocity, HandThrow.HandSide side,
                                    Vector3 releasePosition, float grabDuration, float[] fingerFlexion)
        {
            // If a previous shot is still in its outcome window, flush it now.
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

            ClearFeedback();
            Debug.Log($"[AICoach] Shot #{_shotCount} — speed {_pending.ReleaseSpeedMs:F2} m/s, angle {_pending.ReleaseAngleDeg:F1}°. Waiting {outcomeWaitSeconds}s for outcome.");
        }

        private void OnScored(GameObject ball, float entrySpeed, Vector3 entryVelocity)
        {
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

            _history.Enqueue(_pending);
            if (_history.Count > MaxShotHistory)
                _history.Dequeue();

            // Show visual guidance based on pre-computed physics — no LLM parsing needed.
            if (coachVisuals != null && hoopTransform != null)
            {
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
            if (_client.IsBusy)
            {
                Debug.LogWarning("[AICoach] Client is busy — skipping query for this shot.");
                return;
            }

            if (coachText != null)
                coachText.text = "Coach is thinking...";

            if (visionCamera != null)
                StartCoroutine(QueryWithScreenshot());
            else
                _client.SendTextRequest(BuildPrompt(), OnModelResponse);
        }

        /// <summary>
        /// Waits for end of frame so the rendered image is complete, captures a screenshot
        /// from visionCamera, then sends the prompt and image to the vision model.
        /// </summary>
        private IEnumerator QueryWithScreenshot()
        {
            yield return new WaitForEndOfFrame();

            Texture2D screenshot = null;
            try
            {
                screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                Debug.Log($"[AICoach] Screenshot captured ({screenshot.width}×{screenshot.height}) — sending vision request.");

                string prompt = BuildPrompt();
                Debug.Log($"[AICoach] Sending vision prompt to model ({prompt.Length} chars).");

                _client.SendVisionRequest(prompt, screenshot, OnModelResponse);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AICoach] Error in QueryWithScreenshot: {ex.Message}\n{ex.StackTrace}");
                if (coachText != null)
                    coachText.text = "[Coach error — check logs]";
            }
            finally
            {
                // Always clean up the screenshot texture after sending (SendVisionRequest copies what it needs)
                if (screenshot != null)
                {
                    Destroy(screenshot);
                }
            }
        }

        // ─── Prompt Builder ───────────────────────────────────────────────────────

        private string BuildPrompt()
        {
            StringBuilder sb = new StringBuilder();

            // ── System role (concise) ─────────────────────────────────────────────
            sb.AppendLine("You are a VR basketball coach. Respond with ONLY 3 numbered instructions:");
            sb.AppendLine("1. Position: move Xm forward/back and Ym left/right.");
            sb.AppendLine("2. Angle: release at X° above horizontal.");
            sb.AppendLine("3. Speed: throw at X m/s. No extra text.");
            sb.AppendLine();

            // ── Court context ─────────────────────────────────────────────────────
            if (hoopTransform != null)
            {
                (float idealAngle, float minSpeed) = ComputeIdealShot(_pending.ReleasePosition);
                Vector3 toHoop         = hoopTransform.position - _pending.ReleasePosition;
                float   horizontalDist = new Vector2(toHoop.x, toHoop.z).magnitude;
                float   heightDiff     = hoopTransform.position.y - _pending.ReleasePosition.y;
                sb.AppendLine($"Hoop: {horizontalDist:F2}m away, {heightDiff:F2}m above release. Ideal angle={idealAngle:F1}°, min speed={minSpeed:F2}m/s.");
            }

            // ── Wind ─────────────────────────────────────────────────────────────
            if (windSystem != null && _pending.WindSpeedMs > 0.01f)
                sb.AppendLine($"Wind: {_pending.WindSpeedMs:F2}m/s {_pending.WindCardinal} ({_pending.WindAngleDeg:F1}°).");

            // ── Current shot ─────────────────────────────────────────────────────
            sb.Append($"Shot outcome: {_pending.Outcome}. ");
            sb.Append($"Release: {_pending.ReleaseSpeedMs:F2}m/s at {_pending.ReleaseAngleDeg:F1}°. ");

            if (_pending.EntrySpeedMs >= 0f)
                sb.Append($"Entry: {_pending.EntrySpeedMs:F2}m/s at {_pending.EntryAngleDeg:F1}° from vertical. ");

            if (_pending.RimImpactSpeedMs >= 0f)
                sb.Append($"Rim impact: {_pending.RimImpactSpeedMs:F2}m/s. ");

            sb.AppendLine();

            // ── Recent history (compact) ──────────────────────────────────────────
            if (_history.Count > 0)
            {
                sb.Append("History: ");
                foreach (ShotRecord r in _history)
                    sb.Append($"#{r.ShotNumber} {r.Outcome} {r.ReleaseSpeedMs:F1}m/s {r.ReleaseAngleDeg:F0}° | ");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ─── Model Response ───────────────────────────────────────────────────────

        private void OnModelResponse(string response)
        {
            if (_hideCoroutine != null)
            {
                StopCoroutine(_hideCoroutine);
                _hideCoroutine = null;
            }

            if (coachText != null)
            {
                coachText.text = string.IsNullOrEmpty(response)
                    ? "[Coach offline — is Ollama running? Check the model name in OllamaVLMClient.]"
                    : response;
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
        }

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

