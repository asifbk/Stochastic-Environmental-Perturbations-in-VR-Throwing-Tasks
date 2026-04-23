using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Switches the Coach GLB between its lip-sync animation variant and its idle
    /// variant by toggling the two child GameObjects based on TTS activity.
    ///
    /// - While <see cref="CoachTTSClient.IsBusy"/> is true  → <see cref="talkingRoot"/> active, <see cref="idleRoot"/> inactive.
    /// - While <see cref="CoachTTSClient.IsBusy"/> is false → <see cref="idleRoot"/> active, <see cref="talkingRoot"/> inactive.
    ///
    /// Attach to the Coach root GameObject alongside <see cref="CoachTTSClient"/>.
    /// </summary>
    public class CoachAnimationSwitch : MonoBehaviour
    {
        private const string LogPrefix = "[CoachAnimationSwitch]";

        [Header("References")]
        [Tooltip("Child GameObject that contains the talking / lip-sync animation — 'Talking (2)'.")]
        [SerializeField] private GameObject talkingRoot;

        [Tooltip("Child GameObject that contains the idle animation — 'Talking (2)_0'.")]
        [SerializeField] private GameObject idleRoot;

        [Tooltip("CoachTTSClient used to detect when speech is active. Auto-resolved from this GameObject if left empty.")]
        [SerializeField] private CoachTTSClient ttsClient;

        // ── Private State ─────────────────────────────────────────────────────────

        private bool _lastSpeaking;

        // ── Unity Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (ttsClient == null)
                ttsClient = GetComponent<CoachTTSClient>();

            if (ttsClient == null)
            {
                Debug.LogError($"{LogPrefix} CoachTTSClient not found — disabling.");
                enabled = false;
                return;
            }

            if (talkingRoot == null || idleRoot == null)
            {
                Debug.LogError($"{LogPrefix} talkingRoot or idleRoot is not assigned — disabling.");
                enabled = false;
                return;
            }

            // Apply initial state without waiting for the first change.
            ApplyState(ttsClient.IsBusy);
            _lastSpeaking = ttsClient.IsBusy;
        }

        private void Update()
        {
            bool speaking = ttsClient.IsBusy;

            if (speaking == _lastSpeaking) return;

            _lastSpeaking = speaking;
            ApplyState(speaking);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Activates the correct variant and deactivates the other.</summary>
        private void ApplyState(bool speaking)
        {
            talkingRoot.SetActive(speaking);
            idleRoot.SetActive(!speaking);
            Debug.Log($"{LogPrefix} Switched to {(speaking ? "talking" : "idle")} variant.");
        }
    }
}
