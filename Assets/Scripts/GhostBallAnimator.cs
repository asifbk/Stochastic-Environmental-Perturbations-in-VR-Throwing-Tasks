using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Animates a semi-transparent ghost basketball along the physics-ideal trajectory
    /// so the player can see exactly how a perfect shot should travel.
    ///
    /// Call <see cref="Play"/> after a shot is committed to start the animation.
    /// Call <see cref="Stop"/> to hide and cancel immediately.
    ///
    /// The ghost ball loops <see cref="loopCount"/> times and then hides itself.
    /// </summary>
    public class GhostBallAnimator : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────────

        [Tooltip("The ghost ball renderer to animate. Assign the child MeshRenderer GameObject.")]
        [SerializeField] private Renderer ghostRenderer;

        [Tooltip("How many times the ghost ball loops along the ideal arc before hiding.")]
        [SerializeField] [Min(1)] private int loopCount = 3;

        [Tooltip("Duration in seconds for the ghost ball to travel the full arc once.")]
        [SerializeField] private float travelSeconds = 1.2f;

        [Tooltip("Seconds to pause at the hoop between loops.")]
        [SerializeField] private float pauseAtHoopSeconds = 0.25f;

        [Tooltip("Physics gravity used for arc simulation. Matches Physics.gravity by default.")]
        [SerializeField] private float gravityMs2 = 9.81f;

        [Tooltip("Time-step for pre-computing the arc point list. Smaller = smoother path.")]
        [SerializeField] private float simStep = 0.016f;

        // ─── Private state ────────────────────────────────────────────────────────

        private Coroutine        _animCoroutine;
        private List<Vector3>    _arcPoints = new List<Vector3>();
        private const float      ArcMaxSeconds = 5f;

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Pre-computes the ideal arc and starts the ghost ball animation.
        /// </summary>
        /// <param name="origin">Launch position (camera rig / hand release point).</param>
        /// <param name="target">Hoop centre world position.</param>
        /// <param name="angleDeg">Ideal release angle in degrees.</param>
        /// <param name="speedMs">Ideal release speed in m/s.</param>
        public void Play(Vector3 origin, Vector3 target, float angleDeg, float speedMs)
        {
            Stop();

            if (ghostRenderer == null || speedMs <= 0f) return;

            BuildArc(origin, target, angleDeg, speedMs);
            if (_arcPoints.Count < 2) return;

            ghostRenderer.gameObject.SetActive(true);
            _animCoroutine = StartCoroutine(AnimateCoroutine());
        }

        /// <summary>Stops the animation and hides the ghost ball immediately.</summary>
        public void Stop()
        {
            if (_animCoroutine != null)
            {
                StopCoroutine(_animCoroutine);
                _animCoroutine = null;
            }

            if (ghostRenderer != null)
                ghostRenderer.gameObject.SetActive(false);
        }

        // ─── Arc computation ──────────────────────────────────────────────────────

        /// <summary>
        /// Simulates the ideal ballistic arc step-by-step and stores every position.
        /// Uses the elevated-target ballistic formula for the initial velocity so the
        /// arc actually reaches the hoop regardless of height difference.
        /// </summary>
        private void BuildArc(Vector3 origin, Vector3 target, float angleDeg, float speedMs)
        {
            _arcPoints.Clear();

            Vector3 toTarget      = target - origin;
            Vector3 horizontal    = new Vector3(toTarget.x, 0f, toTarget.z).normalized;
            float   horizDist     = new Vector2(toTarget.x, toTarget.z).magnitude;

            float   angleRad      = angleDeg * Mathf.Deg2Rad;
            float   correctedSpeed = ComputeSpeedForTarget(origin, target, angleDeg);
            float   useSpeed      = correctedSpeed > 0f ? correctedSpeed : speedMs;

            Vector3 velocity = (horizontal * Mathf.Cos(angleRad)
                              + Vector3.up  * Mathf.Sin(angleRad)) * useSpeed;
            Vector3 pos      = origin;
            float   elapsed  = 0f;

            while (elapsed < ArcMaxSeconds)
            {
                _arcPoints.Add(pos);
                velocity  += Vector3.down * gravityMs2 * simStep;
                pos       += velocity * simStep;
                elapsed   += simStep;

                float covered = new Vector2(pos.x - origin.x, pos.z - origin.z).magnitude;
                if (covered >= horizDist * 0.97f && velocity.y < 0f)
                {
                    _arcPoints.Add(target);
                    break;
                }

                if (pos.y < origin.y - 5f) break;
            }
        }

        /// <summary>
        /// Elevated-target ballistic speed: derives the launch speed needed to reach
        /// <paramref name="target"/> at <paramref name="angleDeg"/> accounting for height delta.
        /// Returns 0 when the shot is geometrically impossible at that angle.
        /// </summary>
        private float ComputeSpeedForTarget(Vector3 origin, Vector3 target, float angleDeg)
        {
            Vector3 delta     = target - origin;
            float   horizDist = new Vector2(delta.x, delta.z).magnitude;
            float   heightDiff = delta.y;
            float   theta     = angleDeg * Mathf.Deg2Rad;
            float   cosTheta  = Mathf.Cos(theta);
            float   tanTheta  = Mathf.Tan(theta);
            float   denom     = 2f * cosTheta * cosTheta * (horizDist * tanTheta - heightDiff);
            if (denom <= 0f) return 0f;
            return Mathf.Sqrt(gravityMs2 * horizDist * horizDist / denom);
        }

        // ─── Animation coroutine ──────────────────────────────────────────────────

        private IEnumerator AnimateCoroutine()
        {
            int   pointCount    = _arcPoints.Count;
            float stepInterval  = travelSeconds / (pointCount - 1);

            for (int loop = 0; loop < loopCount; loop++)
            {
                // Animate forward along the arc.
                for (int i = 0; i < pointCount; i++)
                {
                    ghostRenderer.transform.position = _arcPoints[i];

                    // Rotate the ball to face its travel direction.
                    if (i < pointCount - 1)
                    {
                        Vector3 dir = (_arcPoints[i + 1] - _arcPoints[i]);
                        if (dir.sqrMagnitude > 0.0001f)
                            ghostRenderer.transform.rotation = Quaternion.LookRotation(dir);
                    }

                    yield return new WaitForSeconds(stepInterval);
                }

                // Brief pause at the hoop before the next loop.
                if (pauseAtHoopSeconds > 0f)
                    yield return new WaitForSeconds(pauseAtHoopSeconds);
            }

            Stop();
        }
    }
}
