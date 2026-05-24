using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace Basketball
{
    /// <summary>
    /// Visualizes eye gaze as two red dots in world space.
    /// Near dot: gaze origin. Far dot: raycast hit on scene geometry.
    /// Requires the Eye Gaze Interaction Profile enabled in Project Settings → XR → OpenXR.
    /// No manual Inspector wiring needed — the action is created in code.
    /// </summary>
    public class EyeTrackingVisualizer : MonoBehaviour
    {
        private const float GazeRayMaxDistance = 20f;
        private const float DotRadius = 0.02f;

        [Header("Visualization")]
        [Tooltip("Layer mask for the gaze raycast.")]
        [SerializeField] private LayerMask raycastMask = Physics.DefaultRaycastLayers;

        private InputAction _gazePositionAction;
        private InputAction _gazeRotationAction;

        private Transform _nearDot;
        private Transform _farDot;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            _nearDot = CreateDot("GazeNearDot");
            _farDot  = CreateDot("GazeFarDot");

            SetDotsVisible(false);

            // Create inline actions bound directly to the Eye Gaze device.
            _gazePositionAction = new InputAction("GazePosition", binding: "<EyeGaze>/pose/position");
            _gazeRotationAction = new InputAction("GazeRotation", binding: "<EyeGaze>/pose/rotation");
        }

        private void OnEnable()
        {
            _gazePositionAction.Enable();
            _gazeRotationAction.Enable();
        }

        private void OnDisable()
        {
            _gazePositionAction.Disable();
            _gazeRotationAction.Disable();
            SetDotsVisible(false);
        }

        private void OnDestroy()
        {
            _gazePositionAction.Dispose();
            _gazeRotationAction.Dispose();
        }

        private void Update()
        {
            // The position control will have a non-zero read when the eye tracker is active.
            Vector3 gazeOrigin = _gazePositionAction.ReadValue<Vector3>();
            Quaternion gazeRotation = _gazeRotationAction.ReadValue<Quaternion>();

            bool isTracked = gazeOrigin != Vector3.zero;

            SetDotsVisible(isTracked);

            if (!isTracked)
                return;

            Vector3 gazeDirection = gazeRotation * Vector3.forward;

            _nearDot.position = gazeOrigin;

            if (Physics.Raycast(gazeOrigin, gazeDirection, out RaycastHit hit, GazeRayMaxDistance, raycastMask))
                _farDot.position = hit.point;
            else
                _farDot.position = gazeOrigin + gazeDirection * GazeRayMaxDistance;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>Creates a red unlit sphere to represent one gaze point.</summary>
        private Transform CreateDot(string dotName)
        {
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = dotName;
            dot.transform.SetParent(transform, worldPositionStays: false);
            dot.transform.localScale = Vector3.one * (DotRadius * 2f);

            Destroy(dot.GetComponent<Collider>());

            Renderer rend = dot.GetComponent<Renderer>();
            Material mat  = new Material(Shader.Find("Unlit/Color"));
            mat.color     = Color.red;
            rend.material = mat;

            return dot.transform;
        }

        /// <summary>Shows or hides both dots.</summary>
        private void SetDotsVisible(bool visible)
        {
            if (_nearDot != null) _nearDot.gameObject.SetActive(visible);
            if (_farDot  != null) _farDot.gameObject.SetActive(visible);
        }
    }
}
