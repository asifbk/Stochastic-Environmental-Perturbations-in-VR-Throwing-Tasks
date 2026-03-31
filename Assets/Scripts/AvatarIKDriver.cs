using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Basketball
{
    /// <summary>
    /// Drives the MikeAlger Humanoid avatar using Animation Rigging TwoBoneIK constraints.
    ///
    /// Setup (automatic via SetupRig()):
    ///   - Adds RigBuilder to the Animator GameObject.
    ///   - Creates a child Rig layer with two TwoBoneIKConstraints (arms) and a
    ///     MultiParentConstraint (head).
    ///   - IK targets are proxy GameObjects that mirror the live SenseGlove wrist
    ///     trackers and the HMD camera each frame in LateUpdate.
    ///   - Hip position is estimated from the midpoint between the two wrist targets,
    ///     offset downward, so the spine follows naturally without a waist tracker.
    ///
    /// Conflict resolution:
    ///   AvatarAnimationController disables the Animator to apply procedural poses.
    ///   This script re-enables it (Animation Rigging requires an active Animator)
    ///   and disables AvatarAnimationController so they never fight.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class AvatarIKDriver : MonoBehaviour
    {
        // ── Tracker Sources (assign in Inspector) ─────────────────────────────
        [Header("VR Tracker Sources")]
        [Tooltip("Transform of the HMD Camera — drives the head bone.")]
        [SerializeField] private Transform hmdTransform;

        [Tooltip("Transform of SGHand Right — drives the right arm IK target.")]
        [SerializeField] private Transform rightWristTracker;

        [Tooltip("Transform of SGHand Left — drives the left arm IK target.")]
        [SerializeField] private Transform leftWristTracker;

        // ── Tuning ────────────────────────────────────────────────────────────
        [Header("IK Weights")]
        [SerializeField] [Range(0f, 1f)] private float armPositionWeight  = 1f;
        [SerializeField] [Range(0f, 1f)] private float armRotationWeight  = 1f;
        [SerializeField] [Range(0f, 1f)] private float headWeight         = 1f;

        [Header("Hip Estimation")]
        [Tooltip("Downward offset from the midpoint of the two wrists to the estimated hip position.")]
        [SerializeField] private float hipDropMeters = 0.9f;
        [Tooltip("How quickly the avatar hip follows the estimated position (smoothing).")]
        [SerializeField] [Range(1f, 30f)] private float hipFollowSpeed = 12f;

        [Header("Wrist Rotation Offset")]
        [Tooltip("Euler offset applied to the right wrist tracker rotation to align the glove with the avatar hand bone axes.")]
        [SerializeField] private Vector3 rightWristRotationOffset = new Vector3(0f, 0f, 0f);
        [Tooltip("Euler offset applied to the left wrist tracker rotation.")]
        [SerializeField] private Vector3 leftWristRotationOffset  = new Vector3(0f, 0f, 0f);

        // ── Runtime IK Target Proxies (created at runtime) ────────────────────
        private Transform _rightArmTarget;
        private Transform _leftArmTarget;
        private Transform _rightArmHint;
        private Transform _leftArmHint;
        private Transform _headTarget;

        // ── Bone references (found by name in the Humanoid rig) ───────────────
        private Transform _rightUpperArm;
        private Transform _rightLowerArm;
        private Transform _rightHand;
        private Transform _leftUpperArm;
        private Transform _leftLowerArm;
        private Transform _leftHand;
        private Transform _head;
        private Transform _hips;

        // ── Animation Rigging components ──────────────────────────────────────
        private RigBuilder        _rigBuilder;
        private TwoBoneIKConstraint _rightArmIK;
        private TwoBoneIKConstraint _leftArmIK;

        private Animator          _animator;
        private bool              _rigReady;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _animator = GetComponent<Animator>();

            // Disable AvatarAnimationController — it disables the Animator which
            // breaks Animation Rigging. These two systems are mutually exclusive.
            var procedural = GetComponent<AvatarAnimationController>();
            if (procedural != null)
            {
                procedural.enabled = false;
                Debug.Log("[AvatarIKDriver] Disabled AvatarAnimationController to allow Animation Rigging.");
            }

            // Ensure the Animator is on and using the Humanoid avatar.
            _animator.enabled = true;
        }

        private void Start()
        {
            if (!FindBones())
            {
                Debug.LogError("[AvatarIKDriver] One or more required bones not found. IK disabled.");
                enabled = false;
                return;
            }

            BuildRig();
        }

        // ── Bone Discovery ────────────────────────────────────────────────────

        /// <summary>Finds all bones required for IK by their Mixamo rig names.</summary>
        private bool FindBones()
        {
            _hips          = FindBone("mixamorig_Hips");
            _rightUpperArm = FindBone("mixamorig_RightArm");
            _rightLowerArm = FindBone("mixamorig_RightForeArm");
            _rightHand     = FindBone("mixamorig_RightHand");
            _leftUpperArm  = FindBone("mixamorig_LeftArm");
            _leftLowerArm  = FindBone("mixamorig_LeftForeArm");
            _leftHand      = FindBone("mixamorig_LeftHand");
            _head          = FindBone("mixamorig_Head");

            return _rightUpperArm && _rightLowerArm && _rightHand
                && _leftUpperArm  && _leftLowerArm  && _leftHand
                && _head && _hips;
        }

        private Transform FindBone(string boneName)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name == boneName) return t;

            Debug.LogWarning($"[AvatarIKDriver] Bone not found: '{boneName}'");
            return null;
        }

        // ── Rig Construction ──────────────────────────────────────────────────

        /// <summary>
        /// Programmatically builds the Animation Rigging graph:
        /// RigBuilder → Rig → TwoBoneIKConstraint (x2) + MultiParentConstraint (head).
        /// All IK target proxy GameObjects are created as children of the Rig.
        /// </summary>
        private void BuildRig()
        {
            // 1. RigBuilder on the Animator root.
            _rigBuilder = gameObject.GetComponent<RigBuilder>()
                          ?? gameObject.AddComponent<RigBuilder>();

            // 2. Rig layer child.
            var rigGO = new GameObject("IK_Rig");
            rigGO.transform.SetParent(transform, false);
            var rig = rigGO.AddComponent<Rig>();

            // 3. Register the rig layer.
            _rigBuilder.layers.Clear();
            _rigBuilder.layers.Add(new RigLayer(rig, true));

            // 4. Build IK target proxies.
            _rightArmTarget = CreateProxy(rigGO.transform, "RightArmTarget");
            _leftArmTarget  = CreateProxy(rigGO.transform, "LeftArmTarget");
            _headTarget     = CreateProxy(rigGO.transform, "HeadTarget");

            // 5. Elbow hints — start slightly in front of and below the shoulder.
            _rightArmHint = CreateProxy(rigGO.transform, "RightElbowHint");
            _leftArmHint  = CreateProxy(rigGO.transform, "LeftElbowHint");
            _rightArmHint.position = _rightUpperArm.position + new Vector3(0f, -0.3f, 0.3f);
            _leftArmHint.position  = _leftUpperArm.position  + new Vector3(0f, -0.3f, 0.3f);

            // 6. Right arm TwoBoneIK.
            _rightArmIK = CreateTwoBoneIK(
                rigGO, "RightArmIK",
                _rightUpperArm, _rightLowerArm, _rightHand,
                _rightArmTarget, _rightArmHint);

            // 7. Left arm TwoBoneIK.
            _leftArmIK = CreateTwoBoneIK(
                rigGO, "LeftArmIK",
                _leftUpperArm, _leftLowerArm, _leftHand,
                _leftArmTarget, _leftArmHint);

            // 8. Head MultiParentConstraint — constrains head bone to HMD proxy.
            CreateHeadConstraint(rigGO, _head, _headTarget);

            // 9. Build the PlayableGraph.
            _rigBuilder.Build();
            _rigReady = true;

            Debug.Log("[AvatarIKDriver] Animation Rigging graph built successfully.");
        }

        private static Transform CreateProxy(Transform parent, string proxyName)
        {
            var go = new GameObject(proxyName);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static TwoBoneIKConstraint CreateTwoBoneIK(
            GameObject rigGO, string constraintName,
            Transform root, Transform mid, Transform tip,
            Transform target, Transform hint)
        {
            var constraintGO = new GameObject(constraintName);
            constraintGO.transform.SetParent(rigGO.transform, false);

            var ik = constraintGO.AddComponent<TwoBoneIKConstraint>();
            ik.data.root   = root;
            ik.data.mid    = mid;
            ik.data.tip    = tip;
            ik.data.target = target;
            ik.data.hint   = hint;
            ik.data.targetPositionWeight = 1f;
            ik.data.targetRotationWeight = 1f;
            ik.data.hintWeight           = 0.8f;
            ik.weight = 1f;
            return ik;
        }

        private static void CreateHeadConstraint(
            GameObject rigGO, Transform headBone, Transform headTarget)
        {
            var constraintGO = new GameObject("HeadConstraint");
            constraintGO.transform.SetParent(rigGO.transform, false);

            var constraint = constraintGO.AddComponent<MultiParentConstraint>();

            var sourceObjects = new WeightedTransformArray(1);
            sourceObjects[0] = new WeightedTransform(headTarget, 1f);

            constraint.data.constrainedObject   = headBone;
            constraint.data.sourceObjects       = sourceObjects;
            constraint.data.constrainedPositionXAxis = true;
            constraint.data.constrainedPositionYAxis = true;
            constraint.data.constrainedPositionZAxis = true;
            constraint.data.constrainedRotationXAxis = true;
            constraint.data.constrainedRotationYAxis = true;
            constraint.data.constrainedRotationZAxis = true;
            constraint.weight = 1f;
        }

        // ── Per-Frame Proxy Updates ───────────────────────────────────────────

        private void LateUpdate()
        {
            if (!_rigReady) return;

            UpdateArmTargets();
            UpdateHeadTarget();
            UpdateHipEstimate();
            UpdateElbowHints();
            UpdateIKWeights();
        }

        /// <summary>
        /// Mirrors SenseGlove wrist tracker transforms into the IK target proxies.
        /// A per-hand rotation offset corrects for the physical glove mounting angle.
        /// </summary>
        private void UpdateArmTargets()
        {
            if (rightWristTracker != null)
            {
                _rightArmTarget.position = rightWristTracker.position;
                _rightArmTarget.rotation = rightWristTracker.rotation
                                           * Quaternion.Euler(rightWristRotationOffset);
            }

            if (leftWristTracker != null)
            {
                _leftArmTarget.position = leftWristTracker.position;
                _leftArmTarget.rotation = leftWristTracker.rotation
                                          * Quaternion.Euler(leftWristRotationOffset);
            }
        }

        /// <summary>
        /// Mirrors the HMD camera transform to the head IK target,
        /// stripping any neck-to-head offset so the bone sits at the right height.
        /// </summary>
        private void UpdateHeadTarget()
        {
            if (hmdTransform == null) return;

            _headTarget.position = hmdTransform.position;
            _headTarget.rotation = hmdTransform.rotation;
        }

        /// <summary>
        /// Estimates hip position as the midpoint of the two wrist targets, dropped
        /// downward by <see cref="hipDropMeters"/> and smoothed to avoid jitter.
        /// </summary>
        private void UpdateHipEstimate()
        {
            if (rightWristTracker == null || leftWristTracker == null || _hips == null) return;

            Vector3 midWrist = (rightWristTracker.position + leftWristTracker.position) * 0.5f;
            Vector3 targetHipPos = midWrist + Vector3.down * hipDropMeters;

            _hips.position = Vector3.Lerp(
                _hips.position, targetHipPos,
                Time.deltaTime * hipFollowSpeed);
        }

        /// <summary>
        /// Keeps the elbow hints at a fixed offset behind and below each upper-arm bone
        /// so the IK solver bends the elbow in a naturally human direction.
        /// </summary>
        private void UpdateElbowHints()
        {
            if (_rightUpperArm != null)
                _rightArmHint.position = _rightUpperArm.position + new Vector3( 0.15f, -0.25f, -0.2f);

            if (_leftUpperArm != null)
                _leftArmHint.position  = _leftUpperArm.position  + new Vector3(-0.15f, -0.25f, -0.2f);
        }

        private void UpdateIKWeights()
        {
            if (_rightArmIK != null) _rightArmIK.weight = armPositionWeight;
            if (_leftArmIK  != null) _leftArmIK.weight  = armPositionWeight;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Sets IK weight for both arms simultaneously (e.g. fade in on session start).</summary>
        public void SetArmIKWeight(float weight)
        {
            armPositionWeight = Mathf.Clamp01(weight);
        }

        /// <summary>Sets head constraint weight.</summary>
        public void SetHeadIKWeight(float weight)
        {
            headWeight = Mathf.Clamp01(weight);
        }
    }
}
