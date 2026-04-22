using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Drives the Coach avatar's jaw bone to simulate lip movement while the
    /// CoachTTSClient is speaking. Works in LateUpdate so it overrides the Animator
    /// (same pattern as AvatarAnimationController).
    ///
    /// Attach to the Coach root GameObject alongside CoachTTSClient.
    /// Assign the jaw bone Transform in the Inspector.
    /// </summary>
    public class CoachLipSync : MonoBehaviour
    {
        private const string LogPrefix = "[CoachLipSync]";

        [Header("References")]
        [Tooltip("The jaw bone of the Coach avatar.")]
        [SerializeField] private Transform jawBone;

        [Tooltip("The CoachTTSClient that drives TTS speech on this Coach.")]
        [SerializeField] private CoachTTSClient ttsClient;

        [Header("Jaw Motion")]
        [Tooltip("Maximum jaw open rotation around the X axis (degrees).")]
        [SerializeField] [Range(1f, 30f)] private float maxJawOpenDeg = 12f;

        [Tooltip("How fast the jaw oscillates while speaking (Hz).")]
        [SerializeField] [Range(1f, 10f)] private float jawFrequency = 4f;

        [Tooltip("Seconds to blend the jaw motion in when speech starts.")]
        [SerializeField] [Range(0.05f, 0.5f)] private float blendInSeconds  = 0.1f;

        [Tooltip("Seconds to blend the jaw motion out when speech ends.")]
        [SerializeField] [Range(0.05f, 0.5f)] private float blendOutSeconds = 0.2f;

        // ─── Private state ────────────────────────────────────────────────────────

        private Quaternion _jawRestRotation;
        private float      _blendWeight;   // 0 = closed, 1 = full motion
        private float      _speakTimer;
        private bool       _wasSpeaking;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            if (jawBone == null)
            {
                Debug.LogError($"{LogPrefix} Jaw bone is not assigned — disabling.");
                enabled = false;
                return;
            }

            if (ttsClient == null)
                ttsClient = GetComponent<CoachTTSClient>();

            if (ttsClient == null)
            {
                Debug.LogError($"{LogPrefix} CoachTTSClient not found — disabling.");
                enabled = false;
                return;
            }

            _jawRestRotation = jawBone.localRotation;
        }

        private void Update()
        {
            bool isSpeaking = ttsClient.IsBusy;

            // Blend weight: ramp up when speaking starts, ramp down when it stops.
            float blendSpeed = isSpeaking
                ? Time.deltaTime / blendInSeconds
                : Time.deltaTime / blendOutSeconds;

            _blendWeight = Mathf.Clamp01(_blendWeight + (isSpeaking ? blendSpeed : -blendSpeed));

            if (isSpeaking)
                _speakTimer += Time.deltaTime;
            else if (_blendWeight <= 0f)
                _speakTimer = 0f;

            _wasSpeaking = isSpeaking;
        }

        /// <summary>
        /// Applied in LateUpdate so it runs after the Animator — matching the
        /// AvatarAnimationController pattern and ensuring the jaw rotation sticks.
        /// </summary>
        private void LateUpdate()
        {
            if (jawBone == null) return;

            // Sine-wave jaw open angle, clamped so jaw never opens past maxJawOpenDeg.
            float openDeg  = Mathf.Abs(Mathf.Sin(_speakTimer * jawFrequency * Mathf.PI)) * maxJawOpenDeg;
            float blendedDeg = openDeg * _blendWeight;

            jawBone.localRotation = _jawRestRotation * Quaternion.Euler(blendedDeg, 0f, 0f);
        }
    }
}
