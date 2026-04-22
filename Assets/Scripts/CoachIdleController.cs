using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Drives the rp_nathan coach avatar into a relaxed standing pose with arms at the
    /// sides, and adds subtle talking gestures (body sway, head nod) while the
    /// CoachTTSClient is speaking.
    ///
    /// Arm pose is computed automatically from the FBX T-pose on Start, so there is
    /// no need to guess local bone axis conventions for Generic rigs.
    /// </summary>
    public class CoachIdleController : MonoBehaviour
    {
        private const string LogPrefix = "[CoachIdleController]";

        // ── Inspector ─────────────────────────────────────────────────────────────

        [Header("References")]
        [Tooltip("Root of the rp_nathan skeleton (the direct child FBX GameObject).")]
        [SerializeField] private Transform skeletonRoot;

        [Tooltip("CoachTTSClient on this Coach — used to detect when speech is active.")]
        [SerializeField] private CoachTTSClient ttsClient;

        [Header("Arm Hang Tuning")]
        [Tooltip("Slight outward cant of the hanging arms. Positive = arms drift outward from body.")]
        [SerializeField] [Range(-20f, 20f)] private float armCantOutwardDeg = 4f;

        [Tooltip("Slight forward lean of the hanging arms. Positive = arms drift forward.")]
        [SerializeField] [Range(-20f, 20f)] private float armCantForwardDeg = 3f;

        [Tooltip("Local-space elbow bend offset applied to each lower arm (X = flex).")]
        [SerializeField] private Vector3 lowerArmLocalOffset = new Vector3(-15f, 0f, 0f);

        [Header("Talking Gesture Tuning")]
        [SerializeField] [Range(0f, 5f)]    private float swayAmplitudeDeg = 2f;
        [SerializeField] [Range(0f, 3f)]    private float swayFrequency    = 1.2f;
        [SerializeField] [Range(0f, 5f)]    private float headNodAmplitude = 3f;
        [SerializeField] [Range(0f, 3f)]    private float headNodFrequency = 1.8f;
        [SerializeField] [Range(0.05f, 1f)] private float blendInTime      = 0.25f;
        [SerializeField] [Range(0.05f, 1f)] private float blendOutTime      = 0.4f;

        // ── Bone handles ──────────────────────────────────────────────────────────

        private Transform _spine02, _spine03, _neck, _head;
        private Transform _upperArmL, _lowerArmL;
        private Transform _upperArmR, _lowerArmR;

        // ── Computed arm targets (world-space) ────────────────────────────────────

        // The world-space rotation of each upper arm when it was in T-pose.
        private Quaternion _armLBindWorldRot;
        private Quaternion _armRBindWorldRot;

        // The world-space direction along the arm axis detected from T-pose.
        private Vector3 _armLBindDir;
        private Vector3 _armRBindDir;

        // ── Captured spine/head bind rotations for gesture offsets ────────────────

        private readonly Dictionary<Transform, Quaternion> _bindLocal = new();

        // ── Runtime state ─────────────────────────────────────────────────────────

        private float _talkWeight;
        private float _talkTimer;
        private bool  _ready;

        // ── Unity Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (skeletonRoot == null)
                skeletonRoot = transform;

            if (ttsClient == null)
                ttsClient = GetComponent<CoachTTSClient>();
        }

        private IEnumerator Start()
        {
            // Wait one frame so the Animator (speed=0) settles bones to T-pose.
            yield return null;

            FindBones();

            if (_upperArmL == null || _upperArmR == null)
            {
                Debug.LogError($"{LogPrefix} Arm bones not found — disabling.");
                enabled = false;
                yield break;
            }

            // Record T-pose world rotations and arm directions.
            _armLBindWorldRot = _upperArmL.rotation;
            _armRBindWorldRot = _upperArmR.rotation;
            _armLBindDir      = DetectArmAxis(_upperArmL);
            _armRBindDir      = DetectArmAxis(_upperArmR);

            // Capture spine / head bind rotations for gesture offsets.
            foreach (Transform bone in new[] { _spine02, _spine03, _neck, _head })
                if (bone != null)
                    _bindLocal[bone] = bone.localRotation;

            _ready = true;
            Debug.Log($"{LogPrefix} Ready. ArmL axis={_armLBindDir:F2}  ArmR axis={_armRBindDir:F2}");
        }

        private void Update()
        {
            if (!_ready) return;

            bool speaking = ttsClient != null && ttsClient.IsBusy;

            float delta = speaking
                ? Time.deltaTime / blendInTime
                : -Time.deltaTime / blendOutTime;

            _talkWeight = Mathf.Clamp01(_talkWeight + delta);

            if (_talkWeight > 0f)
                _talkTimer += Time.deltaTime;
            else
                _talkTimer = 0f;
        }

        private void LateUpdate()
        {
            if (!_ready) return;

            ApplyArmPose();
            ApplyGestures();
        }

        // ── Arm pose ──────────────────────────────────────────────────────────────

        private void ApplyArmPose()
        {
            // Arms hang straight down plus small cant in the coach's local frame.
            // We compute the target direction in WORLD space using the Coach transform
            // so the arms rotate correctly when the Coach pivots.
            Transform coachTransform = skeletonRoot.parent != null ? skeletonRoot.parent : skeletonRoot;

            // Down direction in world space, biased by the coach-local cant angles.
            Vector3 hangDirL = coachTransform.TransformDirection(
                (Vector3.down
                 + Mathf.Tan(armCantOutwardDeg  * Mathf.Deg2Rad) * Vector3.left
                 + Mathf.Tan(armCantForwardDeg  * Mathf.Deg2Rad) * Vector3.forward)
                .normalized);

            Vector3 hangDirR = coachTransform.TransformDirection(
                (Vector3.down
                 + Mathf.Tan(armCantOutwardDeg  * Mathf.Deg2Rad) * Vector3.right
                 + Mathf.Tan(armCantForwardDeg  * Mathf.Deg2Rad) * Vector3.forward)
                .normalized);

            // FromToRotation rotates the bone from its T-pose arm direction to the hang
            // direction, then re-applies the original world rotation so twist is preserved.
            _upperArmL.rotation = Quaternion.FromToRotation(_armLBindDir, hangDirL) * _armLBindWorldRot;
            _upperArmR.rotation = Quaternion.FromToRotation(_armRBindDir, hangDirR) * _armRBindWorldRot;

            // Fixed local elbow bend to avoid locked-straight forearms.
            if (_lowerArmL != null) _lowerArmL.localRotation = Quaternion.Euler(lowerArmLocalOffset);
            if (_lowerArmR != null) _lowerArmR.localRotation = Quaternion.Euler(lowerArmLocalOffset);
        }

        // ── Gesture (talking) ─────────────────────────────────────────────────────

        private void ApplyGestures()
        {
            float sway = Mathf.Sin(_talkTimer * swayFrequency    * Mathf.PI * 2f) * swayAmplitudeDeg * _talkWeight;
            float nod  = Mathf.Sin(_talkTimer * headNodFrequency * Mathf.PI * 2f) * headNodAmplitude * _talkWeight;

            ApplyGesture(_spine02, Quaternion.Euler(0f,         sway * 0.4f, 0f));
            ApplyGesture(_spine03, Quaternion.Euler(0f,         sway * 0.3f, 0f));
            ApplyGesture(_neck,    Quaternion.Euler(nod * 0.3f, 0f,          0f));
            ApplyGesture(_head,    Quaternion.Euler(nod,        0f,          0f));
        }

        private void ApplyGesture(Transform bone, Quaternion offset)
        {
            if (bone == null) return;
            Quaternion bind = _bindLocal.TryGetValue(bone, out var b) ? b : bone.localRotation;
            bone.localRotation = bind * offset;
        }

        // ── Auto-detection ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the world-space direction that best represents "along the arm"
        /// by finding the local axis most horizontal in world space (T-pose assumption).
        /// </summary>
        private static Vector3 DetectArmAxis(Transform bone)
        {
            // In T-pose the arm is roughly horizontal. We pick the local axis
            // (±X, ±Y, ±Z) that is most horizontal (smallest |y| component in world).
            Vector3[] candidates =
            {
                 bone.right,  -bone.right,
                 bone.up,     -bone.up,
                 bone.forward, -bone.forward
            };

            Vector3 best     = bone.right;
            float   bestHoriz = -1f;

            foreach (Vector3 c in candidates)
            {
                float horiz = 1f - Mathf.Abs(c.y); // 1 = fully horizontal, 0 = vertical
                if (horiz > bestHoriz)
                {
                    bestHoriz = horiz;
                    best      = c;
                }
            }

            return best;
        }

        // ── Bone discovery ────────────────────────────────────────────────────────

        private void FindBones()
        {
            _spine02   = FindBone("rp_nathan_animated_003_walking_spine_02");
            _spine03   = FindBone("rp_nathan_animated_003_walking_spine_03");
            _neck      = FindBone("rp_nathan_animated_003_walking_neck");
            _head      = FindBone("rp_nathan_animated_003_walking_head");
            _upperArmL = FindBone("rp_nathan_animated_003_walking_upperarm_l");
            _lowerArmL = FindBone("rp_nathan_animated_003_walking_lowerarm_l");
            _upperArmR = FindBone("rp_nathan_animated_003_walking_upperarm_r");
            _lowerArmR = FindBone("rp_nathan_animated_003_walking_lowerarm_r");
        }

        private Transform FindBone(string boneName)
        {
            foreach (Transform t in skeletonRoot.GetComponentsInChildren<Transform>(true))
                if (t.name == boneName) return t;

            Debug.LogWarning($"{LogPrefix} Bone not found: '{boneName}'");
            return null;
        }
    }
}
