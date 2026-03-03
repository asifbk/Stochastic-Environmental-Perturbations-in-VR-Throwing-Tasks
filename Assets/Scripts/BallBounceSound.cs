using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Plays a bounce sound when the ball collides with a surface.
    /// Volume and pitch scale with impact velocity for natural feel.
    /// Requires an AudioSource on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BallBounceSound : MonoBehaviour
    {
        [Header("Audio")]
        [SerializeField] private AudioClip bounceClip;

        [Header("Impact Thresholds")]
        [Tooltip("Minimum collision speed (m/s) required to trigger a sound. Filters out micro-touches.")]
        [SerializeField] [Min(0f)] private float minImpactSpeed = 0.8f;
        [Tooltip("Collision speed (m/s) that maps to maximum volume.")]
        [SerializeField] [Min(0.1f)] private float maxImpactSpeed = 8f;

        [Header("Volume")]
        [SerializeField] [Range(0f, 1f)] private float minVolume = 0.1f;
        [SerializeField] [Range(0f, 1f)] private float maxVolume = 1f;

        [Header("Pitch Variation")]
        [SerializeField] [Range(0.5f, 1f)] private float minPitch = 0.9f;
        [SerializeField] [Range(1f, 2f)]  private float maxPitch = 1.1f;

        [Header("Cooldown")]
        [Tooltip("Minimum seconds between two sound triggers. Prevents rapid-fire on multi-point contacts.")]
        [SerializeField] [Min(0f)] private float cooldownSeconds = 0.08f;

        private AudioSource _audioSource;
        private float _lastPlayTime = -999f;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 1f; // full 3D audio
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (bounceClip == null) return;
            if (Time.time - _lastPlayTime < cooldownSeconds) return;

            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed < minImpactSpeed) return;

            float t = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);
            _audioSource.volume = Mathf.Lerp(minVolume, maxVolume, t);
            _audioSource.pitch  = Mathf.Lerp(minPitch,  maxPitch,  t);
            _audioSource.PlayOneShot(bounceClip);

            _lastPlayTime = Time.time;
        }
    }
}
