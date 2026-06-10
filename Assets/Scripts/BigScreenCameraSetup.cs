using UnityEngine;
using UnityEngine.UI;

namespace Basketball
{
    /// <summary>
    /// Creates a RenderTexture at runtime, assigns it to the spectator camera,
    /// and displays the live feed via a RawImage on the BigScreenCanvas.
    /// </summary>
    public class BigScreenCameraSetup : MonoBehaviour
    {
        [Header("References")]
        public Camera spectatorCamera;
        public RawImage bigScreenImage;

        [Header("Render Texture Settings")]
        public int renderWidth = 1920;
        public int renderHeight = 1080;
        public int antiAliasing = 2;

        private RenderTexture _renderTexture;

        private void Awake()
        {
            if (spectatorCamera == null)
            {
                Debug.LogError("[BigScreenCameraSetup] spectatorCamera is not assigned.", this);
                return;
            }

            if (bigScreenImage == null)
            {
                Debug.LogError("[BigScreenCameraSetup] bigScreenImage is not assigned.", this);
                return;
            }

            CreateRenderTexture();
            ApplyToCamera();
            ApplyToScreen();
        }

        private void CreateRenderTexture()
        {
            _renderTexture = new RenderTexture(renderWidth, renderHeight, 24)
            {
                antiAliasing = antiAliasing,
                name = "BigScreenRT"
            };
            _renderTexture.Create();
        }

        private void ApplyToCamera()
        {
            spectatorCamera.targetTexture = _renderTexture;
        }

        private void ApplyToScreen()
        {
            bigScreenImage.texture = _renderTexture;
        }

        private void OnDestroy()
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
        }
    }
}
