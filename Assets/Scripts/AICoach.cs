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

        [Header("Balls")]
        [Tooltip("All basketball Rigidbodies — used to sample velocity when SenseGlove release event is unavailable.")]
        [SerializeField] private Rigidbody[] ballRigidbodies;

        [Header("Vision")]
        [Tooltip("Assign a Camera to enable vision mode — a screenshot is sent with every query. " +
                 "Leave empty to use text-only mode. Requires a vision-capable Ollama model (e.g. llava).")]
        [SerializeField] private Camera visionCamera;

        [Header("Coach UI")]
        [SerializeField] private TextMeshProUGUI coachText;

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
            // Space key: manual trigger for editor testing.
#if ENABLE_INPUT_SYSTEM
            bool spaceDown = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
            bool spaceDown = UnityEngine.Input.GetKeyDown(KeyCode.Space);
#endif
            if (spaceDown)
            {
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

            // ── System role ──────────────────────────────────────────────────────
            sb.AppendLine("You are an AI basketball shooting coach embedded in a VR training simulation.");
            sb.AppendLine("You receive precise sensor data from every throw. Your job is to analyse the data");
            sb.AppendLine("and output EXACTLY three coaching instructions:");
            sb.AppendLine("  1. Position adjustment — how many metres to move forward/back and left/right from the current release spot.");
            sb.AppendLine("  2. Release angle — the optimal angle above horizontal in degrees.");
            sb.AppendLine("  3. Release speed — the optimal speed in m/s.");
            sb.AppendLine("Be direct and specific. Use numbers. Keep each instruction to one sentence.");
            sb.AppendLine("Do NOT add preamble, explanation, or extra text beyond the three numbered points.");
            sb.AppendLine();

            // ── Court context ────────────────────────────────────────────────────
            sb.AppendLine("=== COURT CONTEXT ===");
            if (hoopTransform != null)
            {
                Vector3 hoopPos    = hoopTransform.position;
                Vector3 releasePos = _pending.ReleasePosition;

                Vector3 toHoop         = hoopPos - releasePos;
                float   horizontalDist = new Vector2(toHoop.x, toHoop.z).magnitude;
                float   heightDiff     = hoopPos.y - releasePos.y;

                // Angle from release to hoop relative to world forward.
                float horizontalAngle = Mathf.Atan2(toHoop.x, toHoop.z) * Mathf.Rad2Deg;

                // Theoretical ideal release angle from projectile motion (no wind, no drag).
                // Formula: θ = 45° + 0.5 * atan(h / d)  — gives a good starting point.
                float idealAngleDeg = 45f + 0.5f * Mathf.Atan2(heightDiff, horizontalDist) * Mathf.Rad2Deg;

                // Minimum release speed to reach the hoop assuming that ideal angle.
                // v² = g*d / (sin(2θ) - 2*sin(θ)*h/d)  simplified for reference.
                float g           = 9.81f;
                float theta       = idealAngleDeg * Mathf.Deg2Rad;
                float sinTwoTheta = Mathf.Sin(2f * theta);
                float minSpeed    = (sinTwoTheta > 0.01f)
                    ? Mathf.Sqrt(g * horizontalDist / sinTwoTheta)
                    : 0f;

                sb.AppendLine($"Hoop world position: ({hoopPos.x:F2}, {hoopPos.y:F2}, {hoopPos.z:F2}) m");
                sb.AppendLine($"Player release position: ({releasePos.x:F2}, {releasePos.y:F2}, {releasePos.z:F2}) m");
                sb.AppendLine($"Horizontal distance to hoop: {horizontalDist:F2} m");
                sb.AppendLine($"Hoop height above release point: {heightDiff:F2} m");
                sb.AppendLine($"Horizontal angle to hoop (0=world-forward): {horizontalAngle:F1}°");
                sb.AppendLine($"Physics-ideal release angle (no drag): {idealAngleDeg:F1}°");
                sb.AppendLine($"Physics-minimum release speed at that angle: {minSpeed:F2} m/s");
            }
            else
            {
                sb.AppendLine("Hoop position: unknown (hoopTransform not assigned).");
            }
            sb.AppendLine();

            // ── Wind ─────────────────────────────────────────────────────────────
            sb.AppendLine("=== WIND ===");
            if (windSystem != null)
            {
                sb.AppendLine($"Speed: {_pending.WindSpeedMs:F2} m/s");
                sb.AppendLine($"Direction: {_pending.WindCardinal} ({_pending.WindAngleDeg:F1}°)");
            }
            else
            {
                sb.AppendLine("No wind system detected.");
            }
            sb.AppendLine();

            // ── Current shot ─────────────────────────────────────────────────────
            sb.AppendLine("=== CURRENT SHOT ===");
            sb.AppendLine($"Outcome: {_pending.Outcome}");
            sb.AppendLine($"Release speed: {_pending.ReleaseSpeedMs:F2} m/s");
            sb.AppendLine($"Release angle: {_pending.ReleaseAngleDeg:F1}° above horizontal");
            sb.AppendLine($"Release velocity: ({_pending.ReleaseVelocity.x:F2}, {_pending.ReleaseVelocity.y:F2}, {_pending.ReleaseVelocity.z:F2}) m/s");
            sb.AppendLine($"Grab duration: {_pending.GrabDurationSec:F2} s");

            if (_pending.FingerFlexion != null && _pending.FingerFlexion.Length >= 5)
            {
                sb.AppendLine($"Finger flexion at release (0=open, 1=closed): " +
                              $"Thumb={_pending.FingerFlexion[0]:F2} " +
                              $"Index={_pending.FingerFlexion[1]:F2} " +
                              $"Middle={_pending.FingerFlexion[2]:F2} " +
                              $"Ring={_pending.FingerFlexion[3]:F2} " +
                              $"Pinky={_pending.FingerFlexion[4]:F2}");
            }

            if (_pending.EntrySpeedMs >= 0f)
            {
                sb.AppendLine($"Entry speed at hoop: {_pending.EntrySpeedMs:F2} m/s");
                sb.AppendLine($"Entry angle from vertical: {_pending.EntryAngleDeg:F1}° (0°=straight down, ideal <45°)");
            }

            if (_pending.RimImpactSpeedMs >= 0f)
                sb.AppendLine($"Rim impact speed: {_pending.RimImpactSpeedMs:F2} m/s");

            sb.AppendLine();

            // ── Shot history ─────────────────────────────────────────────────────
            if (_history.Count > 0)
            {
                sb.AppendLine("=== RECENT SHOT HISTORY (oldest → newest) ===");
                foreach (ShotRecord r in _history)
                {
                    sb.Append($"Shot #{r.ShotNumber}: {r.Outcome} | ");
                    sb.Append($"speed={r.ReleaseSpeedMs:F2} m/s | angle={r.ReleaseAngleDeg:F1}° | ");
                    if (r.EntryAngleDeg >= 0f)
                        sb.Append($"entry angle={r.EntryAngleDeg:F1}° | ");
                    if (r.RimImpactSpeedMs >= 0f)
                        sb.Append($"rim speed={r.RimImpactSpeedMs:F2} m/s | ");
                    sb.AppendLine($"wind={r.WindSpeedMs:F1} m/s {r.WindCardinal}");
                }
                sb.AppendLine();
            }

            // ── Task ─────────────────────────────────────────────────────────────
            sb.AppendLine("=== YOUR TASK ===");
            sb.AppendLine("Based on all the data above, give the player exactly three numbered coaching instructions:");
            sb.AppendLine("1. Position: move X m forward/backward and Y m left/right from current release spot.");
            sb.AppendLine("2. Release angle: use X degrees above horizontal.");
            sb.AppendLine("3. Release speed: throw at X m/s.");

            return sb.ToString();
        }

        // ─── Model Response ───────────────────────────────────────────────────────

        private void OnModelResponse(string response)
        {
            if (_hideCoroutine != null)
                StopCoroutine(_hideCoroutine);

            if (coachText != null)
            {
                coachText.text = string.IsNullOrEmpty(response)
                    ? "[Coach offline — is Ollama running? Check the model name in OllamaVLMClient.]"
                    : response;
            }

            _hideCoroutine = StartCoroutine(HideAfterDelay(feedbackDisplaySeconds));
        }

        private IEnumerator HideAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            ClearFeedback();
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>Clears the coach panel text immediately.</summary>
        public void ClearFeedback()
        {
            if (coachText != null)
                coachText.text = string.Empty;
        }
    }
}

