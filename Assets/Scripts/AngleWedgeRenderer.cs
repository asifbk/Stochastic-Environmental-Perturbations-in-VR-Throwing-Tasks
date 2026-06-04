using TMPro;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Draws a filled semi-transparent sector mesh and a 3D label in world space
    /// between the actual and ideal release vectors at the release point.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class AngleWedgeRenderer : MonoBehaviour
    {
        private const float AngleGreenThreshold  = 3f;
        private const float AngleYellowThreshold = 7f;

        private static readonly Color ColorGreen  = new Color(0.2f, 0.9f, 0.2f,  0.35f);
        private static readonly Color ColorYellow = new Color(1f,   0.85f, 0.1f, 0.35f);
        private static readonly Color ColorRed    = new Color(1f,   0.2f,  0.1f, 0.35f);

        [SerializeField] private Material    wedgeMaterial;
        [SerializeField] private int         arcSegments = 32;
        [SerializeField] private float       wedgeRadius = 0.6f;
        [SerializeField] private TextMeshPro angleLabel;
        [SerializeField] private LineRenderer actualRay;
        [SerializeField] private LineRenderer idealRay;

        private MeshFilter   _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh         _wedgeMesh;

        private void Awake()
        {
            EnsureInitialized();
            Hide();
        }

        private void EnsureInitialized()
        {
            if (_wedgeMesh != null) return;

            _meshFilter   = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            _wedgeMesh    = new Mesh { name = "AngleWedge" };
            _meshFilter.mesh = _wedgeMesh;

            if (wedgeMaterial != null)
            {
                ConfigureMaterialForTransparency(wedgeMaterial);
                _meshRenderer.material = wedgeMaterial;
            }
        }

        /// <summary>
        /// Forces Standard-shader transparency + ZTest Always so the wedge renders
        /// on top of court geometry in VR regardless of inspector render queue.
        /// </summary>
        private static void ConfigureMaterialForTransparency(Material mat)
        {
            mat.SetFloat("_Mode",     3f);   // Standard Transparent
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite",   0f);
            mat.SetFloat("_ZTest",    (float)UnityEngine.Rendering.CompareFunction.Always);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>
        /// Shows the wedge between <paramref name="actualAngleDeg"/> and
        /// <paramref name="idealAngleDeg"/> in the vertical plane facing the hoop
        /// from <paramref name="releasePos"/>.
        /// </summary>
        public void Show(Vector3 releasePos, Vector3 hoopPos,
                         float actualAngleDeg, float idealAngleDeg)
        {
            EnsureInitialized();
            float angleDelta = actualAngleDeg - idealAngleDeg;
            float absDelta   = Mathf.Abs(angleDelta);

            Color wedgeColor = absDelta < AngleGreenThreshold  ? ColorGreen
                             : absDelta < AngleYellowThreshold ? ColorYellow
                             : ColorRed;

            // Horizontal forward direction from release toward hoop (XZ only).
            Vector3 toHoop  = hoopPos - releasePos;
            Vector3 forward = new Vector3(toHoop.x, 0f, toHoop.z).normalized;
            Vector3 up      = Vector3.up;

            float minAngle = Mathf.Min(actualAngleDeg, idealAngleDeg);
            float maxAngle = Mathf.Max(actualAngleDeg, idealAngleDeg);

            BuildWedgeMesh(releasePos, forward, up, minAngle, maxAngle, wedgeColor);
            PlaceRay(actualRay, releasePos, forward, up, actualAngleDeg, wedgeColor);
            PlaceRay(idealRay,  releasePos, forward, up, idealAngleDeg,  wedgeColor);
            PlaceLabel(releasePos, forward, up, minAngle, maxAngle, angleDelta);

            gameObject.SetActive(true);
            if (angleLabel != null) angleLabel.gameObject.SetActive(true);
        }

        /// <summary>Hides the wedge, rays, and label.</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
            if (angleLabel  != null) angleLabel.gameObject.SetActive(false);
            if (actualRay   != null) actualRay.gameObject.SetActive(false);
            if (idealRay    != null) idealRay.gameObject.SetActive(false);
        }

        // ─── Mesh ────────────────────────────────────────────────────────────────

        private void BuildWedgeMesh(Vector3 pivot, Vector3 forward, Vector3 up,
                                    float minAngleDeg, float maxAngleDeg, Color color)
        {
            int   n        = Mathf.Max(arcSegments, 2);
            var   vertices = new Vector3[n + 2];  // pivot + (n+1) arc verts
            var   triangles = new int[n * 3];

            vertices[0] = pivot;

            for (int i = 0; i <= n; i++)
            {
                float t       = (float)i / n;
                float angleDeg = Mathf.Lerp(minAngleDeg, maxAngleDeg, t);
                float angleRad = angleDeg * Mathf.Deg2Rad;
                vertices[i + 1] = pivot
                    + (forward * Mathf.Cos(angleRad) + up * Mathf.Sin(angleRad)) * wedgeRadius;
            }

            for (int i = 0; i < n; i++)
            {
                triangles[i * 3 + 0] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            // Build colors array — same color for all vertices.
            var colors = new Color[vertices.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = color;

            _wedgeMesh.Clear();
            _wedgeMesh.vertices  = vertices;
            _wedgeMesh.triangles = triangles;
            _wedgeMesh.colors    = colors;
            _wedgeMesh.RecalculateNormals();

            if (wedgeMaterial != null)
                _meshRenderer.material.color = color;
        }

        // ─── Ray lines ───────────────────────────────────────────────────────────

        private void PlaceRay(LineRenderer lr, Vector3 pivot, Vector3 forward, Vector3 up,
                               float angleDeg, Color color)
        {
            if (lr == null) return;

            float   rad = angleDeg * Mathf.Deg2Rad;
            Vector3 tip = pivot + (forward * Mathf.Cos(rad) + up * Mathf.Sin(rad)) * wedgeRadius;

            lr.positionCount = 2;
            lr.SetPosition(0, pivot);
            lr.SetPosition(1, tip);

            // Full-opacity outline — same hue as the wedge face.
            Color lineColor = new Color(color.r, color.g, color.b, 1f);
            lr.startColor = lineColor;
            lr.endColor   = lineColor;
            lr.gameObject.SetActive(true);
        }

        // ─── Label ───────────────────────────────────────────────────────────────

        private void PlaceLabel(Vector3 pivot, Vector3 forward, Vector3 up,
                                float minAngleDeg, float maxAngleDeg, float angleDelta)
        {
            if (angleLabel == null) return;

            float   midAngleDeg = (minAngleDeg + maxAngleDeg) * 0.5f;
            float   midRad      = midAngleDeg * Mathf.Deg2Rad;
            Vector3 arcMid      = pivot + (forward * Mathf.Cos(midRad) + up * Mathf.Sin(midRad))
                                        * (wedgeRadius * 1.2f);

            angleLabel.transform.position = arcMid;

            // Face the label toward the hoop (billboard around Y).
            if (forward != Vector3.zero)
                angleLabel.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);

            float absDelta = Mathf.Abs(angleDelta);
            if (absDelta < AngleGreenThreshold)
            {
                angleLabel.text = "\u2713 angle OK";
            }
            else if (angleDelta > 0f)
            {
                angleLabel.text = $"\u2191 too flat {absDelta:F1}\u00B0";
            }
            else
            {
                angleLabel.text = $"\u2193 too steep {absDelta:F1}\u00B0";
            }
        }
    }
}
