using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Reads the local bounds of a sibling MeshFilter and resizes/repositions
    /// the ScoreboardCanvas RectTransform to exactly match the screen mesh face.
    ///
    /// Attach to the Screen GameObject (which has both MeshFilter and ScoreboardCanvas as child).
    /// Call Align() once from Start, then remove this component.
    /// </summary>
    [ExecuteAlways]
    public class ScreenCanvasAligner : MonoBehaviour
    {
        [Tooltip("The canvas that should align with this screen mesh.")]
        [SerializeField] private RectTransform canvasRect;

        [Tooltip("If true, alignment runs every frame in Edit Mode. Disable after setup.")]
        [SerializeField] private bool alignEveryFrame = true;

        private void OnEnable()  => Align();
        private void Start()    => Align();
        private void OnValidate() => Align();

#if UNITY_EDITOR
        private void Update()
        {
            if (!Application.isPlaying && alignEveryFrame)
                Align();
        }
#endif

        /// <summary>
        /// Reads the mesh local bounds and applies width/height/center to the canvas RectTransform.
        /// </summary>
        [ContextMenu("Align Now")]
        public void Align()
        {
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                Debug.LogError("[ScreenCanvasAligner] No MeshFilter or sharedMesh found on this GameObject.");
                return;
            }

            if (canvasRect == null)
            {
                Debug.LogError("[ScreenCanvasAligner] Canvas RectTransform is not assigned.");
                return;
            }

            Bounds b = mf.sharedMesh.bounds;

            Debug.Log($"[ScreenCanvasAligner] Mesh bounds — center: {b.center}, size: {b.size}");

            // The canvas renders on its local XY plane.
            // The screen mesh face is aligned with Screen's local XY plane after all rotations.
            // We use mesh bounds X and Y for the canvas dimensions.
            float canvasLocalScale = canvasRect.localScale.x;
            if (Mathf.Approximately(canvasLocalScale, 0f))
            {
                Debug.LogError("[ScreenCanvasAligner] Canvas localScale is zero.");
                return;
            }

            // Determine which two axes represent the visible face.
            // Compare extents to find the two largest axes (the face), ignoring the thin depth axis.
            Vector3 size  = b.size;
            float extentX = size.x;
            float extentY = size.y;
            float extentZ = size.z;

            float faceWidth, faceHeight;
            float centerU, centerV;

            if (extentZ <= extentX && extentZ <= extentY)
            {
                // Z is depth — face is in XY plane
                faceWidth  = extentX;
                faceHeight = extentY;
                centerU    = b.center.x;
                centerV    = b.center.y;
                Debug.Log("[ScreenCanvasAligner] Using XY face.");
            }
            else if (extentY <= extentX && extentY <= extentZ)
            {
                // Y is depth — face is in XZ plane
                faceWidth  = extentX;
                faceHeight = extentZ;
                centerU    = b.center.x;
                centerV    = b.center.z;
                Debug.Log("[ScreenCanvasAligner] Using XZ face.");
            }
            else
            {
                // X is depth — face is in YZ plane
                faceWidth  = extentY;
                faceHeight = extentZ;
                centerU    = b.center.y;
                centerV    = b.center.z;
                Debug.Log("[ScreenCanvasAligner] Using YZ face.");
            }

            Vector2 newSize   = new Vector2(faceWidth / canvasLocalScale, faceHeight / canvasLocalScale);
            Vector2 newCenter = new Vector2(centerU, centerV);

            canvasRect.sizeDelta         = newSize;
            canvasRect.anchoredPosition  = newCenter;

            Debug.Log($"[ScreenCanvasAligner] Applied → sizeDelta: {newSize}, anchoredPosition: {newCenter}");
        }
    }
}
