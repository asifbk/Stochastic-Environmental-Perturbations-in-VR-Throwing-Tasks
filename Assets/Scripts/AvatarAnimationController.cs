using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Basketball
{
    /// <summary>
    /// Drives procedural animations on the MikeAlger (Mixamo) avatar.
    ///
    /// ROOT CAUSE FIX: Unity's Animator evaluates before LateUpdate. Any bone
    /// rotations set inside a coroutine (which runs during Update) are silently
    /// overridden by the Animator the same frame. The fix is to store all target
    /// rotations in a dictionary and write them to bones inside LateUpdate, which
    /// executes AFTER the Animator — making our values stick.
    ///
    /// Key bindings (keyboard):
    ///   1 — Wave        2 — Celebrate
    ///   3 — Dribble     4 — Point
    ///   5 — Taunt       R — Rest pose
    /// </summary>
    public class AvatarAnimationController : MonoBehaviour
    {
        [Header("Avatar Root")]
        [Tooltip("Root transform of the avatar. Defaults to this GameObject.")]
        [SerializeField] private Transform avatarRoot;

        [Header("Tuning")]
        [SerializeField] [Range(0.1f, 2f)]  private float blendInDuration   = 0.35f;
        [SerializeField] [Range(0.1f, 2f)]  private float blendOutDuration  = 0.4f;
        [SerializeField] [Range(1f,  10f)]  private float waveFrequency     = 5f;
        [SerializeField] [Range(1f,  10f)]  private float dribbleFrequency  = 6f;

        // ── Bones ──────────────────────────────────────────────────────────────
        private Transform _hips;
        private Transform _spine, _spine1, _spine2;
        private Transform _neck, _head;
        private Transform _rightShoulder, _rightArm, _rightForeArm, _rightHand;
        private Transform _leftShoulder,  _leftArm,  _leftForeArm,  _leftHand;

        // ── Pose state ─────────────────────────────────────────────────────────
        /// <summary>T-pose local rotations captured at Awake before Animator runs.</summary>
        private readonly Dictionary<Transform, Quaternion> _restPose = new();

        /// <summary>
        /// Target rotations written by coroutines, applied to bones every LateUpdate.
        /// This is the key to overriding the Animator.
        /// </summary>
        private readonly Dictionary<Transform, Quaternion> _targetPose = new();

        private Coroutine _activeAnimation;
        private Animator _animator;

        // ──────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (avatarRoot == null)
                avatarRoot = transform;

            FindBones();

            // Capture rest pose BEFORE disabling the Animator, while bones are
            // still in their Humanoid bind pose (T-pose).
            CaptureRestPose();

            // CRITICAL: Humanoid Avatars use an internal muscle representation.
            // Even with no AnimatorController assigned, the Animator forces bones
            // back to bind pose every frame — overriding any localRotation writes
            // including those in LateUpdate. Disabling the component stops this
            // entirely and hands full bone control to our LateUpdate loop.
            // Re-enable the Animator when integrating with Animation Rigging / VR IK.
            _animator = GetComponent<Animator>();
            if (_animator != null)
            {
                _animator.enabled = false;
                Debug.Log("[AvatarAnimationController] Animator disabled for procedural animation mode.");
            }

            // Initialise target pose to rest so LateUpdate has valid data immediately.
            foreach (var kvp in _restPose)
                _targetPose[kvp.Key] = kvp.Value;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if      (kb.digit1Key.wasPressedThisFrame) Trigger(Wave());
            else if (kb.digit2Key.wasPressedThisFrame) Trigger(Celebrate());
            else if (kb.digit3Key.wasPressedThisFrame) Trigger(Dribble());
            else if (kb.digit4Key.wasPressedThisFrame) Trigger(Point());
            else if (kb.digit5Key.wasPressedThisFrame) Trigger(Taunt());
            else if (kb.rKey.wasPressedThisFrame)      Trigger(GoToRest(blendOutDuration));
        }

        /// <summary>
        /// Applies _targetPose to bones AFTER the Animator has run.
        /// This is the only place bones are written — coroutines only update _targetPose.
        /// </summary>
        private void LateUpdate()
        {
            foreach (var kvp in _targetPose)
                if (kvp.Key != null)
                    kvp.Key.localRotation = kvp.Value;
        }

        // ── Animations ────────────────────────────────────────────────────────

        /// <summary>Raises right arm and waves the hand side to side.</summary>
        private IEnumerator Wave()
        {
            var pose = new Dictionary<Transform, Quaternion>
            {
                [_rightArm]     = Rest(_rightArm)     * Quaternion.Euler( 0f,   0f, -80f),
                [_rightForeArm] = Rest(_rightForeArm) * Quaternion.Euler(45f,   0f,   0f),
            };

            yield return BlendToPose(pose, blendInDuration);

            float elapsed = 0f;
            while (elapsed < 3f)
            {
                elapsed += Time.deltaTime;
                float swing = Mathf.Sin(elapsed * waveFrequency) * 35f;
                _targetPose[_rightForeArm] = pose[_rightForeArm] * Quaternion.Euler(swing, 0f, 0f);
                _targetPose[_rightHand]    = Rest(_rightHand)     * Quaternion.Euler(swing * 0.3f, 0f, 0f);
                yield return null;
            }

            yield return GoToRest(blendOutDuration);
        }

        /// <summary>Throws both arms into a victory V shape and pumps them.</summary>
        private IEnumerator Celebrate()
        {
            var pose = new Dictionary<Transform, Quaternion>
            {
                [_rightArm]     = Rest(_rightArm)      * Quaternion.Euler(  0f,  0f, -70f),
                [_leftArm]      = Rest(_leftArm)       * Quaternion.Euler(  0f,  0f,  70f),
                [_rightForeArm] = Rest(_rightForeArm)  * Quaternion.Euler(-20f,  0f,   0f),
                [_leftForeArm]  = Rest(_leftForeArm)   * Quaternion.Euler(-20f,  0f,   0f),
                [_spine]        = Rest(_spine)          * Quaternion.Euler( -8f,  0f,   0f),
                [_head]         = Rest(_head)           * Quaternion.Euler(-15f,  0f,   0f),
            };

            yield return BlendToPose(pose, blendInDuration);

            float elapsed = 0f;
            while (elapsed < 2.5f)
            {
                elapsed += Time.deltaTime;
                float pump = Mathf.Abs(Mathf.Sin(elapsed * 4f)) * 15f;
                _targetPose[_rightArm] = pose[_rightArm] * Quaternion.Euler(0f, 0f,  pump);
                _targetPose[_leftArm]  = pose[_leftArm]  * Quaternion.Euler(0f, 0f, -pump);
                yield return null;
            }

            yield return GoToRest(blendOutDuration);
        }

        /// <summary>Brings right arm forward and bounces in a dribbling motion.</summary>
        private IEnumerator Dribble()
        {
            var pose = new Dictionary<Transform, Quaternion>
            {
                [_rightArm]     = Rest(_rightArm)      * Quaternion.Euler(0f, -70f, -30f),
                [_rightForeArm] = Rest(_rightForeArm)  * Quaternion.Euler(50f,  0f,   0f),
                [_spine1]       = Rest(_spine1)         * Quaternion.Euler(0f, -10f,   0f),
            };

            yield return BlendToPose(pose, blendInDuration);

            float elapsed = 0f;
            while (elapsed < 4f)
            {
                elapsed += Time.deltaTime;
                float bounce = (Mathf.Sin(elapsed * dribbleFrequency) * 0.5f + 0.5f) * 25f;
                _targetPose[_rightForeArm] = pose[_rightForeArm] * Quaternion.Euler( bounce,        0f, 0f);
                _targetPose[_rightHand]    = Rest(_rightHand)     * Quaternion.Euler( bounce * 0.4f, 0f, 0f);
                yield return null;
            }

            yield return GoToRest(blendOutDuration);
        }

        /// <summary>Rotates torso and extends right arm in a forward point gesture.</summary>
        private IEnumerator Point()
        {
            var pose = new Dictionary<Transform, Quaternion>
            {
                [_rightArm]     = Rest(_rightArm)      * Quaternion.Euler(0f, -90f, -20f),
                [_rightForeArm] = Rest(_rightForeArm)  * Quaternion.Euler(0f,   0f,   0f),
                [_spine2]       = Rest(_spine2)         * Quaternion.Euler(0f, -30f,   0f),
                [_head]         = Rest(_head)           * Quaternion.Euler(0f, -25f,   0f),
            };

            yield return BlendToPose(pose, blendInDuration);
            yield return new WaitForSeconds(2f);
            yield return GoToRest(blendOutDuration);
        }

        /// <summary>Shakes head dismissively side to side.</summary>
        private IEnumerator Taunt()
        {
            var pose = new Dictionary<Transform, Quaternion>
            {
                [_rightArm] = Rest(_rightArm) * Quaternion.Euler(0f, 0f, -20f),
                [_leftArm]  = Rest(_leftArm)  * Quaternion.Euler(0f, 0f,  20f),
                [_spine2]   = Rest(_spine2)    * Quaternion.Euler(-5f, 0f, 0f),
            };

            yield return BlendToPose(pose, 0.3f);

            float elapsed = 0f;
            while (elapsed < 3f)
            {
                elapsed += Time.deltaTime;
                float shake = Mathf.Sin(elapsed * 3f) * 20f;
                _targetPose[_head] = Rest(_head) * Quaternion.Euler(0f, shake,        0f);
                _targetPose[_neck] = Rest(_neck) * Quaternion.Euler(0f, shake * 0.4f, 0f);
                yield return null;
            }

            yield return GoToRest(blendOutDuration);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void Trigger(IEnumerator animation)
        {
            if (_activeAnimation != null)
                StopCoroutine(_activeAnimation);

            _activeAnimation = StartCoroutine(RunAnimation(animation));
        }

        private IEnumerator RunAnimation(IEnumerator animation)
        {
            yield return GoToRest(0.2f);
            yield return animation;
        }

        /// <summary>
        /// Writes target rotations into _targetPose over <paramref name="duration"/> seconds.
        /// Does NOT touch bones directly — LateUpdate handles that.
        /// </summary>
        private IEnumerator BlendToPose(Dictionary<Transform, Quaternion> targets, float duration)
        {
            var snapshots = new Dictionary<Transform, Quaternion>();
            foreach (var kvp in targets)
                if (kvp.Key != null)
                    snapshots[kvp.Key] = _targetPose.TryGetValue(kvp.Key, out var cur)
                        ? cur
                        : Rest(kvp.Key);

            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Clamp01(t + Time.deltaTime / duration);
                float smooth = Mathf.SmoothStep(0f, 1f, t);

                foreach (var kvp in targets)
                    if (kvp.Key != null)
                        _targetPose[kvp.Key] = Quaternion.Lerp(snapshots[kvp.Key], kvp.Value, smooth);

                yield return null;
            }
        }

        /// <summary>Smoothly blends all bones in _targetPose back to rest pose.</summary>
        private IEnumerator GoToRest(float duration)
        {
            var snapshots = new Dictionary<Transform, Quaternion>();
            foreach (var kvp in _restPose)
                if (kvp.Key != null)
                    snapshots[kvp.Key] = _targetPose.TryGetValue(kvp.Key, out var cur)
                        ? cur
                        : kvp.Value;

            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Clamp01(t + Time.deltaTime / duration);
                float smooth = Mathf.SmoothStep(0f, 1f, t);

                foreach (var kvp in _restPose)
                    if (kvp.Key != null)
                        _targetPose[kvp.Key] = Quaternion.Lerp(snapshots[kvp.Key], kvp.Value, smooth);

                yield return null;
            }
        }

        private Quaternion Rest(Transform bone) =>
            bone != null && _restPose.TryGetValue(bone, out var rot) ? rot : Quaternion.identity;

        // ── Bone Setup ────────────────────────────────────────────────────────

        private void FindBones()
        {
            _hips          = FindBone("mixamorig_Hips");
            _spine         = FindBone("mixamorig_Spine");
            _spine1        = FindBone("mixamorig_Spine1");
            _spine2        = FindBone("mixamorig_Spine2");
            _neck          = FindBone("mixamorig_Neck");
            _head          = FindBone("mixamorig_Head");
            _rightShoulder = FindBone("mixamorig_RightShoulder");
            _rightArm      = FindBone("mixamorig_RightArm");
            _rightForeArm  = FindBone("mixamorig_RightForeArm");
            _rightHand     = FindBone("mixamorig_RightHand");
            _leftShoulder  = FindBone("mixamorig_LeftShoulder");
            _leftArm       = FindBone("mixamorig_LeftArm");
            _leftForeArm   = FindBone("mixamorig_LeftForeArm");
            _leftHand      = FindBone("mixamorig_LeftHand");
        }

        private Transform FindBone(string boneName)
        {
            foreach (Transform t in avatarRoot.GetComponentsInChildren<Transform>(true))
                if (t.name == boneName) return t;

            Debug.LogWarning($"[AvatarAnimationController] Bone not found: '{boneName}'");
            return null;
        }

        private void CaptureRestPose()
        {
            _restPose.Clear();
            foreach (Transform bone in new[]
            {
                _hips, _spine, _spine1, _spine2, _neck, _head,
                _rightShoulder, _rightArm, _rightForeArm, _rightHand,
                _leftShoulder,  _leftArm,  _leftForeArm,  _leftHand,
            })
            {
                if (bone != null)
                    _restPose[bone] = bone.localRotation;
            }
        }

        // ── Debug HUD ─────────────────────────────────────────────────────────

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            style.normal.textColor = Color.white;

            GUILayout.BeginArea(new Rect(12f, 12f, 220f, 175f));
            GUILayout.Box(
                "Avatar Animations\n"  +
                "─────────────────\n"  +
                "1  →  Wave\n"         +
                "2  →  Celebrate\n"    +
                "3  →  Dribble\n"      +
                "4  →  Point\n"        +
                "5  →  Taunt\n"        +
                "R  →  Rest pose",
                style);
            GUILayout.EndArea();
        }
    }
}
