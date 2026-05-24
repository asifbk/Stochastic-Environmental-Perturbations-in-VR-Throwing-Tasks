using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Writes a JSON sidecar file at session start that captures every parameter
    /// needed to reproduce or replicate the study environment.
    ///
    /// The file is written once when <see cref="WriteMetadata"/> is called (typically
    /// triggered by the experimenter panel after participant ID and condition are set).
    /// Output path mirrors the TrialDataLogger convention so both files share the same
    /// directory and timestamp suffix.
    ///
    /// Example filename: basketball_meta_P03_20250601_143200.json
    /// </summary>
    public class SessionMetadataLogger : MonoBehaviour
    {
        // ─── Inspector ────────────────────────────────────────────────────────────

        [Header("Dependencies")]
        [Tooltip("The OllamaVLMClient used during the session. " +
                 "Its model, seed, version, and quantization are read and recorded.")]
        [SerializeField] private OllamaVLMClient ollamaClient;

        [Header("Output")]
        [Tooltip("Leave empty to use Application.persistentDataPath. " +
                 "Set to an absolute path (e.g. C:/CHI_Study/Data) for easy retrieval.")]
        [SerializeField] private string outputDirectory = "";

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Writes the session metadata JSON file to disk.
        /// Call this once after the participant ID and condition have been set,
        /// before the first trial begins.
        /// </summary>
        /// <param name="participantId">Participant identifier (e.g. "P03").</param>
        /// <param name="conditionLabel">Study condition label (e.g. "AICoach_Wind").</param>
        public void WriteMetadata(string participantId, string conditionLabel)
        {
            string json = BuildJson(participantId, conditionLabel);
            string path = ResolvePath(participantId);

            try
            {
                File.WriteAllText(path, json, Encoding.UTF8);
                Debug.Log($"[SessionMetadataLogger] Metadata written to: {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SessionMetadataLogger] Failed to write metadata: {ex.Message}");
            }
        }

        // ─── Private ──────────────────────────────────────────────────────────────

        private string ResolvePath(string participantId)
        {
            string dir       = string.IsNullOrEmpty(outputDirectory)
                ? Application.persistentDataPath
                : outputDirectory;
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename  = $"basketball_meta_{participantId}_{timestamp}.json";
            return Path.Combine(dir, filename);
        }

        private string BuildJson(string participantId, string conditionLabel)
        {
            // Manually built JSON — avoids a dependency on Newtonsoft or any serialization
            // package while keeping the output human-readable for paper supplementary materials.
            var sb = new StringBuilder();
            sb.AppendLine("{");

            // ── Session identity ──────────────────────────────────────────────────
            sb.AppendLine($"  \"participantId\": {Q(participantId)},");
            sb.AppendLine($"  \"conditionLabel\": {Q(conditionLabel)},");
            sb.AppendLine($"  \"sessionTimestampUtc\": {Q(DateTime.UtcNow.ToString("o"))},");
            sb.AppendLine($"  \"sessionTimestampLocal\": {Q(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))},");

            // ── Unity environment ─────────────────────────────────────────────────
            sb.AppendLine($"  \"unityVersion\": {Q(Application.unityVersion)},");
            sb.AppendLine($"  \"platform\": {Q(Application.platform.ToString())},");
            sb.AppendLine($"  \"applicationVersion\": {Q(Application.version)},");
            sb.AppendLine($"  \"targetFrameRate\": {Application.targetFrameRate},");

            // ── Hardware ──────────────────────────────────────────────────────────
            sb.AppendLine($"  \"deviceName\": {Q(SystemInfo.deviceName)},");
            sb.AppendLine($"  \"operatingSystem\": {Q(SystemInfo.operatingSystem)},");
            sb.AppendLine($"  \"processorType\": {Q(SystemInfo.processorType)},");
            sb.AppendLine($"  \"processorCount\": {SystemInfo.processorCount},");
            sb.AppendLine($"  \"systemMemoryMB\": {SystemInfo.systemMemorySize},");
            sb.AppendLine($"  \"graphicsDeviceName\": {Q(SystemInfo.graphicsDeviceName)},");
            sb.AppendLine($"  \"graphicsMemoryMB\": {SystemInfo.graphicsMemorySize},");
            sb.AppendLine($"  \"graphicsDeviceVersion\": {Q(SystemInfo.graphicsDeviceVersion)},");

            // ── Physics ───────────────────────────────────────────────────────────
            sb.AppendLine($"  \"physicsGravityY\": {Physics.gravity.y},");
            sb.AppendLine($"  \"fixedDeltaTime\": {Time.fixedDeltaTime},");

            // ── LLM configuration ─────────────────────────────────────────────────
            sb.AppendLine("  \"llm\": {");
            if (ollamaClient != null)
            {
                sb.AppendLine($"    \"modelName\": {Q(ollamaClient.ModelName)},");
                sb.AppendLine($"    \"modelQuantization\": {Q(ollamaClient.ModelQuantization)},");
                sb.AppendLine($"    \"ollamaVersion\": {Q(ollamaClient.OllamaVersion)},");
                sb.AppendLine($"    \"randomSeed\": {ollamaClient.RandomSeed},");
                sb.AppendLine($"    \"deterministicMode\": {(ollamaClient.RandomSeed >= 0 ? "true" : "false")}");
            }
            else
            {
                sb.AppendLine("    \"error\": \"OllamaVLMClient not assigned\"");
            }
            sb.AppendLine("  }");

            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>Wraps a string value in JSON double-quotes.</summary>
        private static string Q(string value)
        {
            if (value == null) return "\"\"";
            // Escape backslashes and double-quotes to keep the JSON valid.
            return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        }
    }
}
