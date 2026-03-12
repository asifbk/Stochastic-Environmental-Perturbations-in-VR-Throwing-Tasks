using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Basketball.Editor
{
    /// <summary>
    /// Pins one edge of the flag cloth to simulate a real flag on a pole.
    /// Run each menu option and pick the one that looks correct in Play mode.
    /// </summary>
    public static class FixFlagClothConstraints
    {
        private const string ClothModelName = "Cloth model";

        // Fraction of the axis range used to detect the pole-edge vertex column.
        private const float PoleEdgeThresholdFraction = 0.06f;

        // How far free vertices can move in world space (metres).
        // Mesh world-space width ≈ 0.26 m — keep MaxDistance well below that.
        private const float FreeVertexMaxDistance = 0.04f;

        [MenuItem("Tools/Basketball/Fix Flag Cloth → Pin Left Edge (min X)")]
        private static void PinMinX() => Apply(Axis.X, pinMin: true);

        [MenuItem("Tools/Basketball/Fix Flag Cloth → Pin Right Edge (max X)")]
        private static void PinMaxX() => Apply(Axis.X, pinMin: false);

        [MenuItem("Tools/Basketball/Fix Flag Cloth → Pin Bottom Edge (min Y)")]
        private static void PinMinY() => Apply(Axis.Y, pinMin: true);

        [MenuItem("Tools/Basketball/Fix Flag Cloth → Pin Top Edge (max Y)")]
        private static void PinMaxY() => Apply(Axis.Y, pinMin: false);

        [MenuItem("Tools/Basketball/Fix Flag Cloth → Pin Bottom Edge (min Z)")]
        private static void PinMinZ() => Apply(Axis.Z, pinMin: true);

        [MenuItem("Tools/Basketball/Fix Flag Cloth → Pin Top Edge (max Z)")]
        private static void PinMaxZ() => Apply(Axis.Z, pinMin: false);

        private enum Axis { X, Y, Z }

        private static void Apply(Axis axis, bool pinMin)
        {
            Cloth cloth = FindClothComponent();
            if (cloth == null)
            {
                Debug.LogError("[FixFlagCloth] Could not find a Cloth component on a GameObject named 'Cloth model'.");
                return;
            }

            SkinnedMeshRenderer smr = cloth.GetComponent<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null)
            {
                Debug.LogError("[FixFlagCloth] No SkinnedMeshRenderer or sharedMesh found.");
                return;
            }

            Vector3[] vertices = smr.sharedMesh.vertices;
            if (vertices.Length == 0)
            {
                Debug.LogError("[FixFlagCloth] Mesh has no vertices.");
                return;
            }

            ApplyConstraints(cloth, vertices, axis, pinMin);
        }

        private static Cloth FindClothComponent()
        {
            return Object.FindObjectsOfType<Cloth>()
                .FirstOrDefault(c => c.gameObject.name == ClothModelName);
        }

        private static void ApplyConstraints(Cloth cloth, Vector3[] vertices, Axis axis, bool pinMin)
        {
            float[] values = vertices.Select(v => axis == Axis.X ? v.x : axis == Axis.Y ? v.y : v.z).ToArray();
            float minVal = values.Min();
            float maxVal = values.Max();
            float edgeValue = pinMin ? minVal : maxVal;
            float threshold = (maxVal - minVal) * PoleEdgeThresholdFraction;

            ClothSkinningCoefficient[] coefficients = new ClothSkinningCoefficient[vertices.Length];
            int pinnedCount = 0;

            for (int i = 0; i < vertices.Length; i++)
            {
                bool isPinned = Mathf.Abs(values[i] - edgeValue) <= threshold;

                coefficients[i] = new ClothSkinningCoefficient
                {
                    maxDistance = isPinned ? 0f : FreeVertexMaxDistance,
                    collisionSphereDistance = float.MaxValue,
                };

                if (isPinned)
                    pinnedCount++;
            }

            cloth.coefficients = coefficients;
            EditorUtility.SetDirty(cloth);

            string edgeLabel = $"{(pinMin ? "min" : "max")} {axis}";
            Debug.Log($"[FixFlagCloth] Applied — pinned {pinnedCount}/{vertices.Length} vertices at {edgeLabel} edge. Free MaxDistance = {FreeVertexMaxDistance} m.");
        }
    }
}
