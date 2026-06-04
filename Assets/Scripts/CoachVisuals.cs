using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Renders two visual coaching aids after each committed shot:
    ///   - A dotted circle floor marker beneath the player's feet (drawn with a LineRenderer).
    ///   - A trajectory arc showing the ideal throw path from the camera rig
    ///     (player origin) to the hoop centre via a physics-simulated parabola.
    /// Call ShowGuidance() after a shot is committed and Hide() when a new throw begins.
    /// </summary>
    public class CoachVisuals : MonoBehaviour
    {
        [Header("Ghost Ball")]
        [Tooltip("GhostBallAnimator that flies along the ideal arc after each shot.")]
        [SerializeField] private GhostBallAnimator ghostBallAnimator;

        [Header("Floor Marker")]
        [Tooltip("LineRenderer used to draw the dotted circle on the floor beneath the player.")]
        [SerializeField] private LineRenderer floorMarkerRenderer;

        [Tooltip("Radius of the dotted circle floor marker in metres.")]
        [SerializeField] private float floorMarkerRadius = 0.3f;

        [Tooltip("Number of dots drawn around the circle. Higher = more dots, denser pattern.")]
        [SerializeField] private int floorMarkerDotCount = 24;

        [Tooltip("Fraction of each dot segment that is visible (0–1). 0.4 = 40 % on, 60 % off.")]
        [SerializeField][Range(0.01f, 0.99f)] private float dotDuty = 0.4f;

        [Header("Release Zone Indicator")]
        [Tooltip("LineRenderer used to draw the 3D ring at the ideal release point. " +
                 "Faces the hoop so the player can see where and at what height to let go of the ball.")]
        [SerializeField] private LineRenderer releaseZoneIndicator;

        [Tooltip("Angle wedge renderer that visualises the angular error between actual and ideal launch vectors.")]
        [SerializeField] private AngleWedgeRenderer angleWedge;

        [Tooltip("Radius of the release zone ring in metres.")]
        [SerializeField] private float releaseZoneRadius = 0.18f;

        [Tooltip("Number of segments used to approximate the ring circle.")]
        [SerializeField] [Min(8)] private int releaseZoneSegments = 32;

        [Header("Trajectory Arc")]
        [SerializeField] private LineRenderer trajectoryArc;

        [Tooltip("Assign the [CameraRig]/Camera Transform. Reserved for future use — the arc now always " +
                 "originates from the recorded release position rather than the camera rig.")]
        [SerializeField] private Transform cameraRigTransform;

        [Tooltip("Physics simulation time-step for the arc. Smaller = smoother but more points.")]
        [SerializeField] private float arcSimStep = 0.05f;

        [Header("Actual Trajectory")]
        [Tooltip("LineRenderer used to trace the player's actual throw path. " +
                 "Color varies red with ball speed — darker at low speed, vivid at peak speed.")]
        [SerializeField] private LineRenderer actualTrajectoryArc;

        private Coroutine _trackingCoroutine;

        private const int   MaxTrajectoryPoints = 500;
        private const float TrajectoryRecordHz  = 50f;

        private const float FloorRaycastMaxDist = 20f;
        private const float ArcMaxSimSeconds    = 5f;
        private const float GravityMs2          = 9.81f;
        private const int   GradientKeyCount    = 8;   // Unity Gradient max is 8 color/alpha keys.
        private const float NetExitDepthM       = 0.45f; // Distance below hoop rim the arc tail extends through the net.

        private void Awake()
        {
            Hide();
        }

        /// <summary>
        /// Places the dotted circle floor marker beneath the player's standing position and draws
        /// the ideal trajectory arc from the camera rig (player body) to the hoop.
        /// </summary>
        /// <param name="playerPos">Player head/body world position — floor disc projected down from here.</param>
        /// <param name="releasePos">Actual ball release position — used as arc origin fallback when
        /// <see cref="cameraRigTransform"/> is not assigned.</param>
        /// <param name="hoopPos">Hoop centre world position — arc terminates here.</param>
        /// <param name="idealAngleDeg">Physics-ideal release angle in degrees.</param>
        /// <param name="idealSpeedMs">Physics-ideal release speed in m/s.</param>
        public void ShowGuidance(Vector3 playerPos, Vector3 releasePos, Vector3 hoopPos,
                                 float idealAngleDeg, float idealSpeedMs, float actualAngleDeg = float.NaN)
        {
            // Floor marker is placed at the release position so the circle always
            // tracks the actual ball origin regardless of shot type (hand throw / AutoShot).
            PlaceFloorMarker(releasePos);

            // Arc origin is always the recorded release position so that the ghost ball,
            // trajectory arc, and release zone ring all start from where the ball actually left.
            Vector3 arcOrigin = releasePos;

            // Re-derive speed using the correct elevated-target formula so that the
            // arc actually reaches the hoop even when it is above the release point.
            float correctedSpeed = ComputeSpeedForTarget(arcOrigin, hoopPos, idealAngleDeg);
            float useSpeed       = correctedSpeed > 0f ? correctedSpeed : idealSpeedMs;

            DrawArc(arcOrigin, hoopPos, idealAngleDeg, useSpeed);
            PlaceReleaseZone(arcOrigin, hoopPos);

            // Loop the ghost ball indefinitely so the player can repeatedly observe
            // the ideal throw before taking their next shot.
            ghostBallAnimator?.PlayLooping(arcOrigin, hoopPos, idealAngleDeg, useSpeed);

            if (!float.IsNaN(actualAngleDeg))
                angleWedge?.Show(releasePos, hoopPos, actualAngleDeg, idealAngleDeg);
        }

        /// <summary>Hides both visual aids and stops any active ball tracking.</summary>
        public void Hide()
        {
            if (floorMarkerRenderer  != null) floorMarkerRenderer.gameObject.SetActive(false);
            if (trajectoryArc        != null) trajectoryArc.gameObject.SetActive(false);
            if (actualTrajectoryArc  != null) actualTrajectoryArc.gameObject.SetActive(false);
            if (releaseZoneIndicator != null) releaseZoneIndicator.gameObject.SetActive(false);
            StopTracking();
            ghostBallAnimator?.Stop();
            angleWedge?.Hide();
        }

        // ─── Actual trajectory recording ──────────────────────────────────────────

        /// <summary>
        /// Starts recording the ball's world-space path into <see cref="actualTrajectoryArc"/>.
        /// Recording stops automatically once the ball descends past <paramref name="hoopPosition"/>.y,
        /// limiting the red arc to the throw arc up to the net.
        /// </summary>
        /// <param name="ball">The Rigidbody of the released basketball to track.</param>
        /// <param name="hoopPosition">World-space centre of the hoop. Recording stops when the ball
        /// descends below this height while moving downward.</param>
        public void StartTracking(Rigidbody ball, Vector3 hoopPosition)
        {
            StopTracking();

            if (actualTrajectoryArc != null)
            {
                actualTrajectoryArc.positionCount = 0;
                actualTrajectoryArc.gameObject.SetActive(true);
            }

            if (ball != null)
                _trackingCoroutine = StartCoroutine(TrackBall(ball, hoopPosition));
        }

        /// <summary>Stops recording ball positions. The drawn line remains visible.</summary>
        public void StopTracking()
        {
            if (_trackingCoroutine == null) return;
            StopCoroutine(_trackingCoroutine);
            _trackingCoroutine = null;
        }

        private IEnumerator TrackBall(Rigidbody ball, Vector3 hoopPosition)
        {
            var   positions      = new List<Vector3>();
            var   speeds         = new List<float>();
            float recordInterval = 1f / TrajectoryRecordHz;
            float nextSample     = Time.time;

            while (ball != null && positions.Count < MaxTrajectoryPoints)
            {
                if (Time.time >= nextSample)
                {
                    positions.Add(ball.position);
                    speeds.Add(ball.velocity.magnitude);
                    nextSample = Time.time + recordInterval;

                    UpdateActualTrajectory(positions, speeds);

                    // Stop once the ball descends past the hoop height — the arc up to the net is complete.
                    if (ball.position.y <= hoopPosition.y && ball.velocity.y < 0f)
                        break;
                }

                yield return null;
            }

            _trackingCoroutine = null;
        }

        private void UpdateActualTrajectory(List<Vector3> positions, List<float> speeds)
        {
            if (actualTrajectoryArc == null || positions.Count == 0) return;

            actualTrajectoryArc.positionCount = positions.Count;
            actualTrajectoryArc.SetPositions(positions.ToArray());

            if (positions.Count < 2) return;

            float minSpeed = float.MaxValue, maxSpeed = float.MinValue;
            foreach (float s in speeds)
            {
                if (s < minSpeed) minSpeed = s;
                if (s > maxSpeed) maxSpeed = s;
            }
            float speedRange = Mathf.Max(maxSpeed - minSpeed, 0.001f);

            int n        = speeds.Count;
            int keyCount = Mathf.Min(GradientKeyCount, n);
            var colorKeys = new GradientColorKey[keyCount];
            var alphaKeys = new GradientAlphaKey[keyCount];

            for (int k = 0; k < keyCount; k++)
            {
                float time      = keyCount > 1 ? (float)k / (keyCount - 1) : 0f;
                int   sampleIdx = Mathf.RoundToInt(time * (n - 1));
                float t         = Mathf.Clamp01((speeds[sampleIdx] - minSpeed) / speedRange);

                // Low speed → dim red; high speed → vivid bright red.
                colorKeys[k] = new GradientColorKey(new Color(1f, Mathf.Lerp(0.45f, 0f, t), 0f), time);
                alphaKeys[k] = new GradientAlphaKey(Mathf.Lerp(0.4f, 1f, t), time);
            }

            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            actualTrajectoryArc.colorGradient = gradient;
        }

        // ─── Floor marker (dotted circle) ─────────────────────────────────────────

        private void PlaceFloorMarker(Vector3 playerPos)
        {
            if (floorMarkerRenderer == null) return;

            Vector3 origin   = new Vector3(playerPos.x, playerPos.y + 0.2f, playerPos.z);
            Vector3 floorPos = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, FloorRaycastMaxDist)
                ? hit.point + Vector3.up * 0.01f
                : new Vector3(playerPos.x, 0.01f, playerPos.z);

            BuildDottedCircle(floorPos);
            floorMarkerRenderer.gameObject.SetActive(true);
        }

        /// <summary>
        /// Populates the floor LineRenderer with a dotted circle pattern.
        /// Each "dot" is drawn as a short arc segment; gaps are created by jumping
        /// the pen to the start of the next dot without drawing.
        /// </summary>
        private void BuildDottedCircle(Vector3 centre)
        {
            // Each dot occupies (2π / dotCount) radians; dotDuty controls the on fraction.
            float segmentAngle = 2f * Mathf.PI / floorMarkerDotCount;
            float onAngle      = segmentAngle * dotDuty;
            int   stepsPerDot  = Mathf.Max(2, Mathf.RoundToInt(onAngle / (Mathf.PI / 32f)));

            // 2 points per gap jump + stepsPerDot points per dot.
            int totalPoints = floorMarkerDotCount * (stepsPerDot + 1);
            var points      = new Vector3[totalPoints];
            int idx         = 0;

            for (int d = 0; d < floorMarkerDotCount; d++)
            {
                float startAngle = d * segmentAngle;

                // Draw the visible dot segment.
                for (int s = 0; s < stepsPerDot; s++)
                {
                    float a = startAngle + onAngle * s / (stepsPerDot - 1);
                    points[idx++] = centre + new Vector3(
                        Mathf.Cos(a) * floorMarkerRadius,
                        0f,
                        Mathf.Sin(a) * floorMarkerRadius);
                }

                // Jump (invisible) to the start of the next dot by duplicating the
                // last point — the gap is covered because the next iteration starts
                // at a different angle.
                float nextAngle = (d + 1) * segmentAngle;
                points[idx++] = centre + new Vector3(
                    Mathf.Cos(nextAngle) * floorMarkerRadius,
                    0f,
                    Mathf.Sin(nextAngle) * floorMarkerRadius);
            }

            floorMarkerRenderer.positionCount = totalPoints;
            floorMarkerRenderer.SetPositions(points);
            floorMarkerRenderer.loop = false;
        }

        // ─── Release zone indicator ───────────────────────────────────────────────

        /// <summary>
        /// Draws a gold ring at <paramref name="arcOrigin"/> whose plane faces the hoop.
        /// The ring acts as a spatial "release window" — the player should let go of the
        /// ball when their hand passes through this position at the correct height.
        /// </summary>
        private void PlaceReleaseZone(Vector3 arcOrigin, Vector3 hoopPos)
        {
            if (releaseZoneIndicator == null) return;

            // Build an orthonormal basis where forward points toward the hoop.
            Vector3 forward = (hoopPos - arcOrigin);
            forward.y = 0f;                              // keep horizontal for a clean ring face
            if (forward.sqrMagnitude < 0.0001f) return;
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 up    = Vector3.Cross(forward, right).normalized;

            int n      = releaseZoneSegments;
            var points = new Vector3[n + 1];             // +1 to close the loop
            for (int i = 0; i <= n; i++)
            {
                float angle = 2f * Mathf.PI * i / n;
                points[i] = arcOrigin
                           + right * (Mathf.Cos(angle) * releaseZoneRadius)
                           + up    * (Mathf.Sin(angle) * releaseZoneRadius);
            }

            releaseZoneIndicator.positionCount = n + 1;
            releaseZoneIndicator.SetPositions(points);
            releaseZoneIndicator.loop = false;           // last point equals first — already closed

            // Gold/yellow gradient so it reads clearly against the green arc and red trajectory.
            var colorKeys = new GradientColorKey[]
            {
                new GradientColorKey(new Color(1f, 0.85f, 0f), 0f),
                new GradientColorKey(new Color(1f, 0.85f, 0f), 1f)
            };
            var alphaKeys = new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            };
            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            releaseZoneIndicator.colorGradient = gradient;

            releaseZoneIndicator.gameObject.SetActive(true);
        }

        // ─── Trajectory arc ───────────────────────────────────────────────────────        /// <summary>
        /// Derives the launch speed required to reach <paramref name="target"/> from
        /// <paramref name="origin"/> at <paramref name="angleDeg"/>, accounting for the
        /// height difference between origin and target (elevated-target ballistic formula).
        /// Returns 0 if the shot is geometrically impossible at the given angle.
        /// </summary>
        private static float ComputeSpeedForTarget(Vector3 origin, Vector3 target, float angleDeg)
        {
            Vector3 delta         = target - origin;
            float   horizDist     = new Vector2(delta.x, delta.z).magnitude;
            float   heightDiff    = delta.y;                 // positive when hoop is above origin
            float   theta         = angleDeg * Mathf.Deg2Rad;
            float   cosTheta      = Mathf.Cos(theta);
            float   tanTheta      = Mathf.Tan(theta);

            // Derived from: y = x·tan(θ) - (g·x²) / (2·v²·cos²θ)
            // Solving for v: v² = g·x² / (2·cos²θ·(x·tan(θ) - y))
            float denom = 2f * cosTheta * cosTheta * (horizDist * tanTheta - heightDiff);
            if (denom <= 0f) return 0f;

            return Mathf.Sqrt(GravityMs2 * horizDist * horizDist / denom);
        }

        private void DrawArc(Vector3 arcOrigin, Vector3 hoopPos,
                             float idealAngleDeg, float idealSpeedMs)
        {
            if (trajectoryArc == null || idealSpeedMs <= 0f) return;

            // Horizontal direction from the arc origin toward the hoop.
            Vector3 toHoop         = hoopPos - arcOrigin;
            Vector3 horizontal     = new Vector3(toHoop.x, 0f, toHoop.z).normalized;
            float   totalHorizDist = new Vector2(toHoop.x, toHoop.z).magnitude;

            float   angleRad = idealAngleDeg * Mathf.Deg2Rad;
            Vector3 velocity = (horizontal * Mathf.Cos(angleRad)
                               + Vector3.up  * Mathf.Sin(angleRad)) * idealSpeedMs;

            // Simulate the full arc from origin to hoop.
            var  allPoints = new List<Vector3>();
            var  allSpeeds = new List<float>();
            Vector3 pos     = arcOrigin;
            float   elapsed = 0f;

            while (elapsed < ArcMaxSimSeconds)
            {
                allPoints.Add(pos);
                allSpeeds.Add(velocity.magnitude);

                velocity += Physics.gravity * arcSimStep;
                pos      += velocity * arcSimStep;
                elapsed  += arcSimStep;

                float horizCovered = new Vector2(pos.x - arcOrigin.x,
                                                 pos.z - arcOrigin.z).magnitude;
                if (horizCovered >= totalHorizDist * 0.9f && velocity.y < 0f)
                {
                    // Snap to the exact hoop centre, then extend downward through the net.
                    allPoints.Add(hoopPos);
                    allSpeeds.Add(velocity.magnitude);
                    allPoints.Add(hoopPos + Vector3.down * NetExitDepthM);
                    allSpeeds.Add(velocity.magnitude);
                    break;
                }

                if (pos.y < arcOrigin.y - 5f) break;
            }

            int count = allPoints.Count;
            if (count < 2) { trajectoryArc.positionCount = 0; return; }

            trajectoryArc.positionCount = count;
            for (int i = 0; i < count; i++)
                trajectoryArc.SetPosition(i, allPoints[i]);

            // Build gradient across the full arc speed range.
            float minSpeed = float.MaxValue;
            float maxSpeed = float.MinValue;
            foreach (float s in allSpeeds)
            {
                minSpeed = Mathf.Min(minSpeed, s);
                maxSpeed = Mathf.Max(maxSpeed, s);
            }
            float speedRange = Mathf.Max(maxSpeed - minSpeed, 0.001f);

            var colorKeys = new GradientColorKey[GradientKeyCount];
            var alphaKeys = new GradientAlphaKey[GradientKeyCount];

            for (int k = 0; k < GradientKeyCount; k++)
            {
                float time      = (float)k / (GradientKeyCount - 1);
                int   sampleIdx = Mathf.RoundToInt(time * (count - 1));
                float t         = Mathf.Clamp01((allSpeeds[sampleIdx] - minSpeed) / speedRange);

                colorKeys[k] = new GradientColorKey(new Color(0f, Mathf.Lerp(0.45f, 1f, t), 0f), time);
                alphaKeys[k] = new GradientAlphaKey(Mathf.Lerp(0.5f, 1f, t), time);
            }

            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            trajectoryArc.colorGradient = gradient;

            trajectoryArc.gameObject.SetActive(true);
        }
    }
}
