using System.Collections.Generic;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Renders two visual coaching aids after each committed shot:
    ///   - A floor disc marking where the player is standing.
    ///   - A trajectory arc showing the ideal throw path from the camera rig
    ///     (player origin) to the hoop centre via a physics-simulated parabola.
    /// Call ShowGuidance() after a shot is committed and Hide() when a new throw begins.
    /// </summary>
    public class CoachVisuals : MonoBehaviour
    {
        [Header("Floor Marker")]
        [SerializeField] private Transform floorMarker;

        [Header("Trajectory Arc")]
        [SerializeField] private LineRenderer trajectoryArc;

        [Tooltip("Assign the [CameraRig]/Camera Transform. The arc starts from this position " +
                 "at average hand-raise height so it originates from the player's body.")]
        [SerializeField] private Transform cameraRigTransform;

        [Tooltip("Height offset above the camera rig origin to approximate the hand release point " +
                 "when no actual release position is available.")]
        [SerializeField] private float handHeightOffset = 0.3f;

        [Tooltip("Physics simulation time-step for the arc. Smaller = smoother but more points.")]
        [SerializeField] private float arcSimStep = 0.05f;

        private const float FloorRaycastMaxDist = 20f;
        private const float ArcMaxSimSeconds    = 5f;

        private void Awake()
        {
            Hide();
        }

        /// <summary>
        /// Places the floor disc beneath the player's standing position and draws the ideal
        /// trajectory arc from the camera rig (player body) to the hoop.
        /// </summary>
        /// <param name="playerPos">Player head/body world position — floor disc projected down from here.</param>
        /// <param name="releasePos">Actual ball release position — used as arc origin fallback when
        /// <see cref="cameraRigTransform"/> is not assigned.</param>
        /// <param name="hoopPos">Hoop centre world position — arc terminates here.</param>
        /// <param name="idealAngleDeg">Physics-ideal release angle in degrees.</param>
        /// <param name="idealSpeedMs">Physics-ideal release speed in m/s.</param>
        public void ShowGuidance(Vector3 playerPos, Vector3 releasePos, Vector3 hoopPos,
                                 float idealAngleDeg, float idealSpeedMs)
        {
            PlaceFloorMarker(playerPos);

            // Arc origin: use the camera rig's XZ position at hand-raise height.
            // Falls back to the recorded release position when cameraRigTransform is not assigned.
            Vector3 arcOrigin = cameraRigTransform != null
                ? new Vector3(cameraRigTransform.position.x,
                              cameraRigTransform.position.y + handHeightOffset,
                              cameraRigTransform.position.z)
                : releasePos;

            DrawArc(arcOrigin, hoopPos, idealAngleDeg, idealSpeedMs);
        }

        /// <summary>Hides both visual aids.</summary>
        public void Hide()
        {
            if (floorMarker != null)   floorMarker.gameObject.SetActive(false);
            if (trajectoryArc != null) trajectoryArc.gameObject.SetActive(false);
        }

        // ─── Floor marker ─────────────────────────────────────────────────────────

        private void PlaceFloorMarker(Vector3 playerPos)
        {
            if (floorMarker == null) return;

            Vector3 origin   = new Vector3(playerPos.x, playerPos.y + 0.2f, playerPos.z);
            Vector3 floorPos = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, FloorRaycastMaxDist)
                ? hit.point + Vector3.up * 0.01f
                : new Vector3(playerPos.x, 0.01f, playerPos.z);

            floorMarker.position = floorPos;
            floorMarker.gameObject.SetActive(true);
        }

        // ─── Trajectory arc ───────────────────────────────────────────────────────

        private void DrawArc(Vector3 arcOrigin, Vector3 hoopPos,
                             float idealAngleDeg, float idealSpeedMs)
        {
            if (trajectoryArc == null || idealSpeedMs <= 0f) return;

            // Horizontal direction from the camera rig position toward the hoop.
            Vector3 toHoop         = hoopPos - arcOrigin;
            Vector3 horizontal     = new Vector3(toHoop.x, 0f, toHoop.z).normalized;
            float   totalHorizDist = new Vector2(toHoop.x, toHoop.z).magnitude;

            float   angleRad = idealAngleDeg * Mathf.Deg2Rad;
            Vector3 velocity = (horizontal * Mathf.Cos(angleRad)
                               + Vector3.up  * Mathf.Sin(angleRad)) * idealSpeedMs;

            List<Vector3> points  = new List<Vector3>();
            Vector3       pos     = arcOrigin;
            float         elapsed = 0f;

            while (elapsed < ArcMaxSimSeconds)
            {
                points.Add(pos);

                velocity += Physics.gravity * arcSimStep;
                pos      += velocity * arcSimStep;
                elapsed  += arcSimStep;

                // Once the simulated ball has covered 90 % of the horizontal distance
                // and is descending, snap the final point to the hoop centre and stop.
                float horizCovered = new Vector2(pos.x - arcOrigin.x,
                                                 pos.z - arcOrigin.z).magnitude;
                if (horizCovered >= totalHorizDist * 0.9f && velocity.y < 0f)
                {
                    points.Add(hoopPos);
                    break;
                }

                // Safety: abort if the arc falls far below the origin.
                if (pos.y < arcOrigin.y - 5f) break;
            }

            trajectoryArc.positionCount = points.Count;
            trajectoryArc.SetPositions(points.ToArray());
            trajectoryArc.gameObject.SetActive(true);
        }
    }
}
