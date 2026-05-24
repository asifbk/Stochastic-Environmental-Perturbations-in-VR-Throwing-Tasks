using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Basketball
{
    /// <summary>
    /// Sends prompts — optionally with a screenshot — to a local Ollama server via
    /// the OpenAI-compatible /v1/chat/completions endpoint.
    ///
    /// Designed for Qwen vision models running locally (e.g. qwen3.6:35b).
    /// Mirrors the public API of <see cref="OllamaVLMClient"/> so both clients
    /// are interchangeable from the caller's point of view.
    /// </summary>
    public class QwenVLMClient : MonoBehaviour
    {
        // ─── Constants ────────────────────────────────────────────────────────────

        private const string ChatEndpoint      = "/v1/chat/completions";
        private const float  MaxTotalWaitSec   = 360f;
        private const int    MaxImageSize       = 256;

        // ─── Inspector ────────────────────────────────────────────────────────────

        [Header("Connection")]
        [SerializeField] private string ollamaBaseUrl  = "http://localhost:11434";
        [SerializeField] private string modelName      = "qwen3.6:35b";
        [SerializeField] private float  timeoutSeconds = 120f;

        [Header("Generation")]
        [Tooltip("Maximum tokens in the response. Keep short for fast coaching tips.")]
        [SerializeField] private int maxTokens = 120;

        [Tooltip("0 = greedy/deterministic (fastest). Higher values add creativity.")]
        [SerializeField] [Range(0f, 2f)] private float temperature = 0f;

        [Header("Reproducibility")]
        [Tooltip("Fixed RNG seed passed on every request. Set to -1 to disable.")]
        [SerializeField] private int randomSeed = 42;

        [Tooltip("Quantization level of the loaded model (e.g. Q4_K_M). " +
                 "Recorded in session metadata — does not affect runtime.")]
        [SerializeField] private string modelQuantization = "Q4_K_M";

        // ─── Public metadata accessors ────────────────────────────────────────────

        public string ModelName         => modelName;
        public string ModelQuantization => modelQuantization;
        public int    RandomSeed        => randomSeed;

        // ─── State ────────────────────────────────────────────────────────────────

        /// <summary>True while a request is in flight. New requests are rejected until this clears.</summary>
        public bool IsBusy { get; private set; }

        private float     _requestStartTime        = -1f;
        private Coroutine _currentRequestCoroutine;

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Sends a text-only prompt to the model.
        /// Invokes <paramref name="onComplete"/> with the response string, or null on failure.
        /// </summary>
        public void SendTextRequest(string prompt, Action<string> onComplete)
        {
            if (!ValidateRequest(prompt, onComplete)) return;
            _requestStartTime        = Time.time;
            _currentRequestCoroutine = StartCoroutine(SendCoroutine(prompt, imageBase64: null, onComplete));
        }

        /// <summary>
        /// Encodes <paramref name="texture"/> as JPEG, then sends the prompt + image to the vision model.
        /// The texture must already be readable (e.g. captured via ScreenCapture).
        /// Invokes <paramref name="onComplete"/> with the response string, or null on failure.
        /// </summary>
        public void SendVisionRequest(string prompt, Texture2D texture, Action<string> onComplete)
        {
            if (!ValidateRequest(prompt, onComplete)) return;

            if (texture == null)
            {
                Debug.LogWarning("[QwenClient] SendVisionRequest called with null texture — falling back to text-only.");
                _requestStartTime        = Time.time;
                _currentRequestCoroutine = StartCoroutine(SendCoroutine(prompt, imageBase64: null, onComplete));
                return;
            }

            string imageBase64 = null;
            try
            {
                Texture2D toEncode    = texture;
                bool      createdCopy = false;

                if (texture.width > MaxImageSize || texture.height > MaxImageSize)
                {
                    float scale = MaxImageSize / (float)Mathf.Max(texture.width, texture.height);
                    int   w     = Mathf.Max(1, Mathf.RoundToInt(texture.width  * scale));
                    int   h     = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
                    toEncode    = ResizeTexture(texture, w, h);
                    createdCopy = true;
                    Debug.Log($"[QwenClient] Screenshot scaled {texture.width}×{texture.height} → {w}×{h}.");
                }

                byte[] jpegBytes = toEncode.EncodeToJPG(quality: 25);
                imageBase64 = Convert.ToBase64String(jpegBytes);
                Debug.Log($"[QwenClient] Image encoded — {jpegBytes.Length / 1024} KB JPEG.");

                if (createdCopy)
                    Destroy(toEncode);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QwenClient] Failed to encode image: {ex.Message}");
                IsBusy = false;
                onComplete?.Invoke(null);
                return;
            }

            _requestStartTime        = Time.time;
            _currentRequestCoroutine = StartCoroutine(SendCoroutine(prompt, imageBase64, onComplete));
        }

        // ─── Private ──────────────────────────────────────────────────────────────

        private bool ValidateRequest(string prompt, Action<string> onComplete)
        {
            if (string.IsNullOrEmpty(prompt))
            {
                Debug.LogWarning("[QwenClient] Called with empty prompt.");
                onComplete?.Invoke(null);
                return false;
            }

            if (IsBusy)
            {
                Debug.LogWarning("[QwenClient] A request is already in flight. Skipping.");
                return false;
            }

            IsBusy = true;
            return true;
        }

        private void Update()
        {
            if (!IsBusy || _requestStartTime < 0f) return;

            if (Time.time - _requestStartTime > MaxTotalWaitSec)
            {
                Debug.LogError($"[QwenClient] WATCHDOG: Request exceeded {MaxTotalWaitSec}s. Forcing clear.");
                IsBusy            = false;
                _requestStartTime = -1f;

                if (_currentRequestCoroutine != null)
                {
                    StopCoroutine(_currentRequestCoroutine);
                    _currentRequestCoroutine = null;
                }
            }
        }

        private IEnumerator SendCoroutine(string prompt, string imageBase64, Action<string> onComplete)
        {
            string url      = ollamaBaseUrl.TrimEnd('/') + ChatEndpoint;
            string jsonBody = BuildRequestJson(prompt, imageBase64);
            bool   isVision = imageBase64 != null;

            Debug.Log($"[QwenClient] POST → {url}  model={modelName}  vision={isVision}  timeout={timeoutSeconds}s");

            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            using (UnityWebRequest www = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                www.uploadHandler   = new UploadHandlerRaw(bodyBytes);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.timeout         = Mathf.CeilToInt(timeoutSeconds);
                www.SetRequestHeader("Content-Type", "application/json");

                yield return www.SendWebRequest();

                float elapsed = Time.time - _requestStartTime;

                if (www.result == UnityWebRequest.Result.Success)
                {
                    string parsed = ParseResponse(www.downloadHandler.text);
                    Debug.Log($"[QwenClient] Response received after {elapsed:F1}s.");
                    onComplete?.Invoke(parsed);
                }
                else
                {
                    Debug.LogError($"[QwenClient] Request failed after {elapsed:F1}s ({www.result}): {www.error}\n" +
                                   $"Status: {www.responseCode}  Body: {www.downloadHandler?.text}");
                    onComplete?.Invoke(null);
                }
            }

            IsBusy                   = false;
            _requestStartTime        = -1f;
            _currentRequestCoroutine = null;
        }

        /// <summary>
        /// Builds an OpenAI-compatible /v1/chat/completions JSON payload.
        /// When <paramref name="imageBase64"/> is provided the content array includes
        /// an image_url entry with a base64 data URI (vision models only).
        /// </summary>
        private string BuildRequestJson(string prompt, string imageBase64)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\":{JsonEscape(modelName)},");
            sb.Append($"\"max_tokens\":{maxTokens},");
            sb.Append($"\"temperature\":{temperature.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
            if (randomSeed >= 0)
                sb.Append($"\"seed\":{randomSeed},");

            // messages array
            sb.Append("\"messages\":[{\"role\":\"user\",\"content\":[");

            // text part
            sb.Append("{\"type\":\"text\",\"text\":");
            sb.Append(JsonEscape(prompt));
            sb.Append("}");

            // image part (vision only)
            if (imageBase64 != null)
            {
                sb.Append(",{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/jpeg;base64,");
                sb.Append(imageBase64);
                sb.Append("\"}}");
            }

            sb.Append("]}]}");
            return sb.ToString();
        }

        /// <summary>Extracts the first choice's message content from the OpenAI-compatible response JSON.</summary>
        private static string ParseResponse(string json)
        {
            try
            {
                QwenResponse parsed = JsonUtility.FromJson<QwenResponse>(json);
                if (parsed.choices != null && parsed.choices.Length > 0)
                    return parsed.choices[0].message.content;

                Debug.LogWarning($"[QwenClient] Response contained no choices. Raw: {json}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[QwenClient] Failed to parse response JSON: {ex.Message}\nRaw: {json}");
                return null;
            }
        }

        /// <summary>
        /// Blits the source texture into a new <see cref="Texture2D"/> of the given dimensions
        /// using a RenderTexture. Works even when the source is not CPU-readable.
        /// Caller is responsible for destroying the returned texture.
        /// </summary>
        private static Texture2D ResizeTexture(Texture2D source, int width, int height)
        {
            RenderTexture rt   = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
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

        // ─── Response DTOs ────────────────────────────────────────────────────────

        [Serializable]
        private struct QwenResponse
        {
            public Choice[] choices;

            [Serializable]
            public struct Choice
            {
                public Message message;
            }

            [Serializable]
            public struct Message
            {
                public string content;
            }
        }
    }
}
