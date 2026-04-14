using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Basketball
{
    /// <summary>
    /// Sends prompts — optionally with a screenshot — to a local Ollama model via
    /// the /api/generate endpoint and returns the text response.
    ///
    /// Text-only models (mistral, llama3, …): use SendTextRequest.
    /// Vision models    (llava, bakllava, …): use SendVisionRequest.
    /// </summary>
    public class OllamaVLMClient : MonoBehaviour
    {
        private const string GenerateEndpoint = "/api/generate";
        
        /// <summary>Maximum time (in seconds) to wait for ANY request, including the network timeout.
        /// Acts as a safety net to ensure IsBusy is always cleared.</summary>
        private const float MaxTotalWaitSeconds = 360f;

        /// <summary>Base URL of the local Ollama server.</summary>
        [SerializeField] private string ollamaBaseUrl = "http://localhost:11434";

        /// <summary>
        /// Ollama model name. Use a vision-capable model (e.g. "llava:7b", "llava:13b")
        /// when calling SendVisionRequest.
        /// </summary>
        [SerializeField] private string modelName = "moondream";

        /// <summary>Seconds before the UnityWebRequest is abandoned (network timeout).
        /// moondream (~1.7 GB) runs fully on GPU and typically responds in 5–15 s.</summary>
        [SerializeField] private float timeoutSeconds = 60f;

        /// <summary>True while a request is in flight. New requests are rejected until this clears.</summary>
        public bool IsBusy { get; private set; }
        
        /// <summary>Time when the current request started (for timeout watchdog).</summary>
        private float _requestStartTime = -1f;
        
        /// <summary>Coroutine reference for the current request (used for cleanup).</summary>
        private Coroutine _currentRequestCoroutine;

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Sends a text-only prompt to Ollama. Suitable for any model.
        /// Invokes onComplete with the response string, or null on failure.
        /// </summary>
        public void SendTextRequest(string prompt, Action<string> onComplete)
        {
            if (!ValidateRequest(prompt, onComplete)) return;
            _requestStartTime = Time.time;
            _currentRequestCoroutine = StartCoroutine(SendCoroutine(prompt, imageBase64: null, onComplete));
        }

        /// <summary>
        /// Encodes texture as JPEG, then sends the prompt + image to a vision model.
        /// The texture must already be readable (e.g. captured via ScreenCapture).
        /// Invokes onComplete with the response string, or null on failure.
        /// </summary>
        public void SendVisionRequest(string prompt, Texture2D texture, Action<string> onComplete)
        {
            if (!ValidateRequest(prompt, onComplete)) return;

            if (texture == null)
            {
                Debug.LogWarning("[OllamaClient] SendVisionRequest called with null texture — falling back to text-only.");
                _requestStartTime = Time.time;
                _currentRequestCoroutine = StartCoroutine(SendCoroutine(prompt, imageBase64: null, onComplete));
                return;
            }

            // Downscale to MaxImageSize on the longest side before encoding.
            // Keeping the image small reduces VRAM usage during llava vision inference.
            const int MaxImageSize = 256;
            byte[] jpegBytes = null;
            string imageBase64 = null;

            try
            {
                Texture2D toEncode = texture;
                bool createdResized = false;

                if (texture.width > MaxImageSize || texture.height > MaxImageSize)
                {
                    float scale = MaxImageSize / (float)Mathf.Max(texture.width, texture.height);
                    int   w     = Mathf.Max(1, Mathf.RoundToInt(texture.width  * scale));
                    int   h     = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
                    toEncode = ResizeTexture(texture, w, h);
                    createdResized = true;
                    Debug.Log($"[OllamaClient] Screenshot scaled {texture.width}×{texture.height} → {w}×{h}.");
                }

                jpegBytes = toEncode.EncodeToJPG(quality: 25);
                imageBase64 = Convert.ToBase64String(jpegBytes);
                Debug.Log($"[OllamaClient] Image encoded — {jpegBytes.Length / 1024} KB JPEG.");

                if (createdResized)
                    UnityEngine.Object.Destroy(toEncode);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OllamaClient] Failed to encode image to JPEG: {ex.Message}");
                IsBusy = false;
                onComplete?.Invoke(null);
                return;
            }

            _requestStartTime = Time.time;
            _currentRequestCoroutine = StartCoroutine(SendCoroutine(prompt, imageBase64, onComplete));
        }

        // ─── Private ──────────────────────────────────────────────────────────────

        private bool ValidateRequest(string prompt, Action<string> onComplete)
        {
            if (string.IsNullOrEmpty(prompt))
            {
                Debug.LogWarning("[OllamaClient] Called with empty prompt.");
                onComplete?.Invoke(null);
                return false;
            }
            if (IsBusy)
            {
                Debug.LogWarning("[OllamaClient] A request is already in flight. Skipping.");
                return false;
            }
            IsBusy = true;
            return true;
        }
        
        private void Update()
        {
            // Timeout watchdog: if a request has been active for too long, force-clear it
            if (IsBusy && _requestStartTime > 0f)
            {
                float elapsedTime = Time.time - _requestStartTime;
                if (elapsedTime > MaxTotalWaitSeconds)
                {
                    Debug.LogError($"[OllamaClient] WATCHDOG: Request exceeded max wait time ({MaxTotalWaitSeconds}s). " +
                                   $"Forcibly clearing IsBusy. This should not normally happen.");
                    
                    // Force clear the busy state
                    IsBusy = false;
                    _requestStartTime = -1f;
                    
                    // Stop the coroutine if it's still running
                    if (_currentRequestCoroutine != null)
                    {
                        StopCoroutine(_currentRequestCoroutine);
                        _currentRequestCoroutine = null;
                    }
                }
            }
        }

        private IEnumerator SendCoroutine(string prompt, string imageBase64, Action<string> onComplete)
        {
            string jsonBody = BuildRequestJson(prompt, imageBase64);
            string url      = ollamaBaseUrl.TrimEnd('/') + GenerateEndpoint;

            bool isVision = imageBase64 != null;
            Debug.Log($"[OllamaClient] POST → {url}  model={modelName}  vision={isVision}  timeout={timeoutSeconds}s");

            // Pre-flight: verify Ollama is reachable before sending the full payload.
            string tagsUrl = ollamaBaseUrl.TrimEnd('/') + "/api/tags";
            using (UnityWebRequest ping = UnityWebRequest.Get(tagsUrl))
            {
                ping.timeout = 5;
                yield return ping.SendWebRequest();
                if (ping.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[OllamaClient] Ollama server not reachable at {ollamaBaseUrl} — is it running? ({ping.error})");
                    onComplete?.Invoke(null);
                    IsBusy = false;
                    _requestStartTime        = -1f;
                    _currentRequestCoroutine = null;
                    yield break;
                }
            }

            using (UnityWebRequest www = UnityWebRequest.Post(url, jsonBody, "application/json"))
            {
                www.timeout = Mathf.CeilToInt(timeoutSeconds);

                yield return www.SendWebRequest();

                float elapsed = Time.time - _requestStartTime;

                if (www.result == UnityWebRequest.Result.Success)
                {
                    string response = ParseResponse(www.downloadHandler.text);
                    Debug.Log($"[OllamaClient] Response received after {elapsed:F1}s ({www.downloadHandler.text.Length} chars).");
                    onComplete?.Invoke(response);
                }
                else
                {
                    Debug.LogError($"[OllamaClient] Request failed after {elapsed:F1}s ({www.result}): {www.error}\n" +
                                   $"Status: {www.responseCode}  Body: {www.downloadHandler?.text}");
                    onComplete?.Invoke(null);
                }
            }

            IsBusy = false;
            _requestStartTime        = -1f;
            _currentRequestCoroutine = null;
        }

        /// <summary>
        /// Builds the Ollama /api/generate JSON payload.
        /// When imageBase64 is provided the "images" array is included (vision models only).
        /// num_ctx caps the KV-cache size to reduce VRAM usage; num_predict limits response length.
        /// </summary>
        private string BuildRequestJson(string prompt, string imageBase64)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":\"{modelName}\",");
            sb.Append($"\"prompt\":{JsonEscape(prompt)},");
            if (imageBase64 != null)
                sb.Append($"\"images\":[\"{imageBase64}\"],");
            sb.Append("\"stream\":false,");
            sb.Append("\"options\":{\"num_ctx\":2048,\"num_predict\":200}");
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>Extracts the "response" field from Ollama's non-streaming JSON reply.</summary>
        private static string ParseResponse(string json)
        {
            try
            {
                OllamaResponse parsed = JsonUtility.FromJson<OllamaResponse>(json);
                return parsed.response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OllamaClient] Failed to parse response JSON: {ex.Message}\nRaw: {json}");
                return null;
            }
        }

        /// <summary>
        /// Blits the source texture into a new Texture2D of the given dimensions using a
        /// RenderTexture, which works even when the source is not CPU-readable.
        /// Caller is responsible for destroying the returned texture when done.
        /// </summary>
        private static Texture2D ResizeTexture(Texture2D source, int width, int height)
        {
            RenderTexture rt  = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Graphics.Blit(source, rt);

            Texture2D result = new Texture2D(width, height, TextureFormat.RGB24, mipChain: false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        /// <summary>Wraps a string in JSON quotes and escapes unsafe characters.</summary>
        private static string JsonEscape(string value)
        {
            if (value == null) return "\"\"";

            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:   sb.Append(c);      break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // ─── Response DTO ─────────────────────────────────────────────────────────

        [Serializable]
        private struct OllamaResponse
        {
            public string response;
        }
    }
}
