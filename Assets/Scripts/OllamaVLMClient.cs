using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Basketball
{
    /// <summary>
    /// Ollama /api/chat client optimised for low-latency VR coaching feedback
    /// over an SSH tunnel to a remote Bizon GPU cluster.
    ///
    /// Design choices for speed:
    ///   - Uses /api/chat with a messages array (system + user roles).
    ///   - Streaming tokens so the first word appears before generation finishes.
    ///   - Text-only by default; optional base64 image path for qwen2.5vl:7b.
    ///   - Greedy decoding (temperature 0) — deterministic and fastest path.
    ///   - Minimal num_ctx / num_predict to reduce KV-cache pressure.
    ///   - Request queue so back-to-back shots never block each other.
    ///
    /// SSH tunnel (run on LOCAL machine, keep open while using Unity):
    ///   ssh -L 11435:localhost:11434 mkarim1@bizon.host.ualr.edu -p 22415 -N
    ///
    /// Ollama endpoint: http://localhost:11435/api/chat
    /// </summary>
    public class OllamaVLMClient : MonoBehaviour
    {
        // ─── Endpoint Constants ───────────────────────────────────────────────────

        private const string ChatEndpoint    = "/api/chat";
        private const string TagsEndpoint    = "/api/tags";
        private const float  WatchdogSeconds = 120f;

        // ─── Hardcoded Configuration ──────────────────────────────────────────────

        /// <summary>Bizon Ollama via SSH tunnel: ssh -L 11435:localhost:11434 mkarim1@bizon.host.ualr.edu -p 22415 -N</summary>
        private const string BizonBaseUrl   = "http://localhost:11435";
        private const string BizonModel     = "qwen2.5:7b";
        private const float  RequestTimeout = 60f;

        // Generation — tuned for low-latency VR coaching
        private const int   CtxWindow      = 512;  // small = faster KV-cache
        private const int   MaxTokens      = 60;   // ~3 short sentences
        private const float Temp           = 0f;   // greedy / deterministic
        private const int   TopKValue      = 1;    // true greedy with temp=0
        private const float TopPValue      = 1.0f;
        private const float RepeatPen      = 1.0f;
        private const int   FixedSeed      = 42;

        private const string CoachPrompt =
            "You are a VR basketball coach. Give 2 short, direct sentences of feedback. No lists.";

        // ─── Public metadata (read by SessionMetadataLogger) ──────────────────────

        public string ModelName         => BizonModel;
        public int    RandomSeed        => FixedSeed;
        public string ModelQuantization => "Q4_K_M";
        public string OllamaVersion     => "unknown";

        // ─── State ────────────────────────────────────────────────────────────────

        /// <summary>True while a request is in flight or queued.</summary>
        public bool IsBusy => _queue.Count > 0 || _activeCoroutine != null;

        private readonly Queue<IEnumerator> _queue = new Queue<IEnumerator>();
        private Coroutine _activeCoroutine;
        private float     _requestStartTime = -1f;

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        private void Start()
        {
            StartCoroutine(RunHealthCheck());
        }

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Sends a text-only prompt and receives the full assembled response in one callback.
        /// Use only when incremental token updates are not needed.
        /// </summary>
        public void SendTextRequest(string prompt, Action<string> onComplete)
        {
            Enqueue(RunRequest(prompt, imageBase64: null, onToken: null, onComplete));
        }

        /// <summary>
        /// Sends a text-only prompt and fires <paramref name="onToken"/> for every streamed token,
        /// then fires <paramref name="onComplete"/> with the full assembled response.
        /// Prefer this over <see cref="SendTextRequest"/> for lowest perceived latency.
        /// </summary>
        public void SendStreamingTextRequest(
            string prompt,
            Action<string> onToken,
            Action<string> onComplete)
        {
            Enqueue(RunRequest(prompt, imageBase64: null, onToken, onComplete));
        }

        /// <summary>
        /// Sends a vision request with an optional texture encoded as base64 JPEG.
        /// Requires a vision-capable model such as qwen2.5vl:7b.
        /// Falls back to text-only if <paramref name="frame"/> is null.
        /// </summary>
        public void SendVisionRequest(string prompt, Texture2D frame, Action<string> onComplete)
        {
            string b64 = EncodeTexture(frame);
            Enqueue(RunRequest(prompt, b64, onToken: null, onComplete));
        }

        /// <summary>
        /// Streaming vision variant. Requires a vision-capable model such as qwen2.5vl:7b.
        /// Falls back to streaming text-only if <paramref name="frame"/> is null.
        /// </summary>
        public void SendStreamingVisionRequest(
            string prompt,
            Texture2D frame,
            Action<string> onToken,
            Action<string> onComplete)
        {
            string b64 = EncodeTexture(frame);
            Enqueue(RunRequest(prompt, b64, onToken, onComplete));
        }

        // ─── Queue ────────────────────────────────────────────────────────────────

        private void Enqueue(IEnumerator request)
        {
            _queue.Enqueue(request);
            if (_activeCoroutine == null)
                DrainQueue();
        }

        private void DrainQueue()
        {
            if (_queue.Count == 0) return;
            _activeCoroutine = StartCoroutine(RunNext());
        }

        private IEnumerator RunNext()
        {
            yield return StartCoroutine(_queue.Dequeue());
            _activeCoroutine = null;
            DrainQueue();
        }

        // ─── Watchdog ─────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_requestStartTime < 0f) return;
            if (!(Time.time - _requestStartTime > WatchdogSeconds)) return;

            Debug.LogError("[OllamaClient] Watchdog: request exceeded time limit. Resetting queue.");
            _requestStartTime = -1f;
            if (_activeCoroutine != null)
            {
                StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
            }
            _queue.Clear();
        }

        // ─── Health Check ─────────────────────────────────────────────────────────

        /// <summary>
        /// Fires on Start. Hits /api/tags to verify the SSH tunnel is alive and
        /// the configured model exists on Bizon. Prints a clear diagnosis to the Console.
        /// </summary>
        private IEnumerator RunHealthCheck()
        {
            string url = BizonBaseUrl.TrimEnd('/') + TagsEndpoint;
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.timeout = 10;
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(
                        $"[OllamaClient] HealthCheck FAILED — cannot reach {url}\n" +
                        $"Error: {www.error}\n\n" +
                        $"Fix: On your LOCAL machine run and keep open:\n" +
                        $"  ssh -L 11435:localhost:11434 mkarim1@bizon.host.ualr.edu -p 22415 -N");
                    yield break;
                }

                string body = www.downloadHandler.text;
                if (body.Contains($"\"name\":\"{BizonModel}\""))
                {
                    Debug.Log($"[OllamaClient] HealthCheck OK — tunnel live, model '{BizonModel}' ready at {BizonBaseUrl}");
                }
                else
                {
                    Debug.LogError(
                        $"[OllamaClient] HealthCheck — tunnel live but model '{BizonModel}' not found.\n" +
                        $"Available models:\n{body}\n\n" +
                        $"Fix: Run on Bizon: ollama pull {BizonModel}");
                }
            }
        }

        // ─── Core Request ─────────────────────────────────────────────────────────

        private IEnumerator RunRequest(
            string prompt,
            string imageBase64,
            Action<string> onToken,
            Action<string> onComplete)
        {
            _requestStartTime = Time.time;

            bool   streaming = onToken != null;
            string url       = BizonBaseUrl.TrimEnd('/') + ChatEndpoint;
            string json      = BuildRequestJson(prompt, imageBase64, streaming);
            byte[] body      = Encoding.UTF8.GetBytes(json);

            Debug.Log($"[OllamaClient] POST {url}\n{json}");

            using (UnityWebRequest www = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                www.uploadHandler = new UploadHandlerRaw(body);
                www.SetRequestHeader("Content-Type", "application/json");

                // Streaming must have no timeout — the tunnel keeps the socket open
                // until the model finishes. Non-streaming can be bounded.
                www.timeout = streaming ? 0 : Mathf.CeilToInt(RequestTimeout);

                if (streaming)
                {
                    var handler = new StreamingDownloadHandler(onToken);
                    www.downloadHandler = handler;
                    www.SendWebRequest();

                    while (!www.isDone)
                    {
                        handler.Flush();
                        yield return null;
                    }

                    handler.Flush();

                    if (www.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[OllamaClient] Streaming failed: {www.error}\n" +
                                       $"Response body: {handler.RawErrorText}");
                        onComplete?.Invoke(null);
                    }
                    else
                    {
                        onComplete?.Invoke(handler.FullText);
                    }
                }
                else
                {
                    www.downloadHandler = new DownloadHandlerBuffer();
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        string response = ParseChatResponse(www.downloadHandler.text);
                        onComplete?.Invoke(response);
                    }
                    else
                    {
                        Debug.LogError($"[OllamaClient] Request failed: {www.error}");
                        onComplete?.Invoke(null);
                    }
                }
            }

            _requestStartTime = -1f;
        }

        // ─── JSON Builder — /api/chat ─────────────────────────────────────────────

        private string BuildRequestJson(string prompt, string imageBase64, bool stream)
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append($"\"model\":{JsonEscape(BizonModel)},");
            sb.Append($"\"stream\":{(stream ? "true" : "false")},");

            sb.Append("\"messages\":[");

            sb.Append("{\"role\":\"system\",\"content\":");
            sb.Append(JsonEscape(CoachPrompt));
            sb.Append("},");

            sb.Append("{\"role\":\"user\",\"content\":");
            sb.Append(JsonEscape(prompt));

            if (!string.IsNullOrEmpty(imageBase64))
            {
                sb.Append(",\"images\":[");
                sb.Append(JsonEscape(imageBase64));
                sb.Append(']');
            }

            sb.Append('}');   // close user message
            sb.Append("],");  // close messages

            sb.Append("\"options\":{");
            sb.Append($"\"num_ctx\":{CtxWindow},");
            sb.Append($"\"num_predict\":{MaxTokens},");
            sb.Append($"\"temperature\":{Temp.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"top_k\":{TopKValue},");
            sb.Append($"\"top_p\":{TopPValue.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"repeat_penalty\":{RepeatPen.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"seed\":{FixedSeed}");
            sb.Append('}');   // close options

            sb.Append('}');   // close root
            return sb.ToString();
        }

        // ─── Non-streaming Response Parser ────────────────────────────────────────

        [Serializable]
        private class ChatChunk
        {
            public ChatMessage message;
            public bool        done;
        }

        [Serializable]
        private class ChatMessage
        {
            public string role;
            public string content;
        }

        private static string ParseChatResponse(string body)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (string line in body.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    ChatChunk chunk = JsonUtility.FromJson<ChatChunk>(trimmed);
                    if (chunk?.message?.content != null)
                        sb.Append(chunk.message.content);
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OllamaClient] Parse error: {ex.Message}");
                return null;
            }
        }

        // ─── Texture Encoder ──────────────────────────────────────────────────────

        private static string EncodeTexture(Texture2D tex)
        {
            if (tex == null) return null;
            try
            {
                byte[] jpg = tex.EncodeToJPG(quality: 60);
                return Convert.ToBase64String(jpg);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OllamaClient] Texture encode failed, sending text-only: {ex.Message}");
                return null;
            }
        }

        // ─── JSON Escape ──────────────────────────────────────────────────────────

        private static string JsonEscape(string value)
        {
            if (value == null) return "\"\"";
            var sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
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

        // ─── Streaming Download Handler ───────────────────────────────────────────

        /// <summary>
        /// Custom DownloadHandlerScript for Ollama's /api/chat NDJSON stream.
        /// Each line: { "message": { "role": "assistant", "content": "token" }, "done": false }
        /// ReceiveData runs on the network thread; Flush() dispatches on the main thread.
        /// </summary>
        private sealed class StreamingDownloadHandler : DownloadHandlerScript
        {
            private readonly Action<string> _onToken;
            private readonly Queue<string>  _pending  = new Queue<string>();
            private readonly StringBuilder  _lineBuf  = new StringBuilder();
            private readonly StringBuilder  _fullText = new StringBuilder();
            private readonly StringBuilder  _rawBuf   = new StringBuilder();

            public string FullText     => _fullText.ToString();
            /// <summary>Raw bytes received — used to surface Ollama error bodies on failure.</summary>
            public string RawErrorText => _rawBuf.ToString();

            public StreamingDownloadHandler(Action<string> onToken)
                : base(new byte[32768])
            {
                _onToken = onToken;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                string chunk = Encoding.UTF8.GetString(data, 0, dataLength);
                _rawBuf.Append(chunk);
                foreach (char c in chunk)
                {
                    if (c == '\n')
                    {
                        string line = _lineBuf.ToString();
                        _lineBuf.Clear();
                        string token = ExtractContentToken(line);
                        if (!string.IsNullOrEmpty(token))
                            lock (_pending) _pending.Enqueue(token);
                    }
                    else
                    {
                        _lineBuf.Append(c);
                    }
                }
                return true;
            }

            /// <summary>Dispatches queued tokens to the onToken callback on the main thread.</summary>
            public void Flush()
            {
                while (true)
                {
                    string token;
                    lock (_pending)
                    {
                        if (_pending.Count == 0) break;
                        token = _pending.Dequeue();
                    }
                    _fullText.Append(token);
                    _onToken?.Invoke(token);
                }
            }

            /// <summary>
            /// Fast string scan for "content":"TOKEN" inside a single NDJSON line.
            /// Avoids per-token JSON deserialization and GC pressure.
            /// </summary>
            private static string ExtractContentToken(string line)
            {
                const string key = "\"content\":\"";
                int start = line.IndexOf(key, StringComparison.Ordinal);
                if (start < 0) return null;

                start += key.Length;
                int end = start;
                while (end < line.Length)
                {
                    if (line[end] == '\\') { end += 2; continue; }
                    if (line[end] == '"')  break;
                    end++;
                }
                if (end >= line.Length) return null;

                return line.Substring(start, end - start)
                           .Replace("\\n",  "\n")
                           .Replace("\\r",  "\r")
                           .Replace("\\t",  "\t")
                           .Replace("\\\"", "\"")
                           .Replace("\\\\", "\\");
            }
        }
    }
}
