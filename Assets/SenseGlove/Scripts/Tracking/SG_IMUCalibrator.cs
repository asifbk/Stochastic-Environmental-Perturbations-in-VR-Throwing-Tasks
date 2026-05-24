using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SG.Util
{
    /// <summary>
    /// One-click IMU rotation calibrator for SG_IMUTracking.
    /// Hold your hand in the neutral reference pose (palm down, fingers forward),
    /// then press the calibration key or click the Inspector button.
    /// The script measures the gap between the current IMU-driven rotation and the
    /// tracker's reference orientation and writes the corrective delta back into
    /// SG_IMUTracking.rotationOffsetEuler — no manual Euler tweaking required.
    /// </summary>
    [RequireComponent(typeof(SG_IMUTracking))]
    public class SG_IMUCalibrator : MonoBehaviour
    {
        [Header("Reference")]
        [Tooltip("The tracker Transform that defines the neutral world-space orientation " +
                 "(e.g. ViveTracker_Right). Must match SG_IMUTracking.relativeTo.")]
        public Transform referenceTracker;

        [Header("Neutral Pose Definition")]
        [Tooltip("Local-space rotation that describes what 'palm down, fingers forward' looks like " +
                 "relative to the tracker. Zero means the tracker's own axes match that pose exactly.")]
        public Vector3 neutralPoseOffsetEuler = Vector3.zero;

        [Header("Input")]
        [Tooltip("Press this key at runtime to trigger calibration.")]
        public KeyCode calibrateKey = KeyCode.C;

        private SG_IMUTracking _imuTracking;

        private void Awake()
        {
            _imuTracking = GetComponent<SG_IMUTracking>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(calibrateKey))
                Calibrate();
        }

        /// <summary>
        /// Computes and applies the corrective rotation offset to SG_IMUTracking.rotationOffsetEuler.
        /// Call this while holding the glove in the neutral reference pose.
        /// </summary>
        public void Calibrate()
        {
            if (_imuTracking == null)
                _imuTracking = GetComponent<SG_IMUTracking>();

            if (_imuTracking == null)
            {
                Debug.LogError("[SG_IMUCalibrator] No SG_IMUTracking found on this GameObject.");
                return;
            }

            if (referenceTracker == null)
            {
                Debug.LogError("[SG_IMUCalibrator] referenceTracker is not assigned.");
                return;
            }

            if (_imuTracking.imuSource == null)
            {
                Debug.LogError("[SG_IMUCalibrator] SG_IMUTracking.imuSource is not assigned.");
                return;
            }

            // Desired world-space rotation: tracker orientation + neutral pose offset.
            Quaternion desiredRotation = referenceTracker.rotation * Quaternion.Euler(neutralPoseOffsetEuler);

            // Temporarily zero out the offset so we read the raw calibrated IMU rotation.
            Vector3 savedOffset = _imuTracking.rotationOffsetEuler;
            _imuTracking.rotationOffsetEuler = Vector3.zero;
            _imuTracking.UpdateRotation();
            Quaternion rawRotation = transform.rotation;

            // Restore saved offset in case calibration fails below.
            _imuTracking.rotationOffsetEuler = savedOffset;

            // Delta = desired * inverse(raw) — this is the corrective world-space rotation.
            Quaternion delta = desiredRotation * Quaternion.Inverse(rawRotation);
            Vector3 newEuler = delta.eulerAngles;

            // Normalise angles to [-180, 180] for readability.
            newEuler = NormaliseEuler(newEuler);

            _imuTracking.rotationOffsetEuler    = newEuler;
            _imuTracking.rotationOffsetIsWorldSpace = true;

            Debug.Log($"[SG_IMUCalibrator] Calibration complete. " +
                      $"rotationOffsetEuler set to {newEuler:F1}");

#if UNITY_EDITOR
            // Mark the component dirty so the new value is saved to the scene.
            EditorUtility.SetDirty(_imuTracking);
#endif
        }

        /// <summary>Remaps each Euler component from [0,360) to [-180,180).</summary>
        private static Vector3 NormaliseEuler(Vector3 euler)
        {
            return new Vector3(
                NormaliseAngle(euler.x),
                NormaliseAngle(euler.y),
                NormaliseAngle(euler.z));
        }

        private static float NormaliseAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)  angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(SG_IMUCalibrator))]
    public class SG_IMUCalibratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(6);

            SG_IMUCalibrator calibrator = (SG_IMUCalibrator)target;

            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
            if (GUILayout.Button("▶  Calibrate IMU Rotation", GUILayout.Height(36)))
            {
                if (Application.isPlaying)
                {
                    calibrator.Calibrate();
                }
                else
                {
                    Debug.LogWarning("[SG_IMUCalibrator] Enter Play Mode before calibrating — " +
                                     "the glove IMU must be live.");
                }
            }
            GUI.backgroundColor = Color.white;

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Hold glove in neutral pose (palm down, fingers forward), then click Calibrate.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode with the glove connected, then click Calibrate.",
                    MessageType.Warning);
            }
        }
    }
#endif
}
