using System.Diagnostics;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Speaks the AI coach's responses using the Windows built-in speech engine (SAPI)
    /// via a PowerShell one-liner. No DLL import, no Docker, no installation required.
    /// Works on any Windows PC that has PowerShell (all Windows 10/11 machines do).
    /// Audio plays through the default Windows audio device.
    /// </summary>
    public class CoachTTSClient : MonoBehaviour
    {
        private const string LogPrefix = "[CoachTTS]";

        // ─── Inspector ────────────────────────────────────────────────────────────

        [Header("Voice Settings")]
        [Tooltip("Speech rate passed to SAPI (-10 slow … 0 normal … 10 fast).")]
        [Range(-10, 10)]
        [SerializeField] private int speechRate = 0;

        [Tooltip("Speech volume passed to SAPI (0–100).")]
        [Range(0, 100)]
        [SerializeField] private int speechVolume = 100;

        // ─── Private State ────────────────────────────────────────────────────────

        private Thread  _speakThread;
        private Process _powershellProcess;
        private volatile bool _isSpeaking;
        private readonly object _lock = new object();

        /// <summary>True while a speech thread is running.</summary>
        public bool IsBusy => _isSpeaking;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────────

        private void OnDestroy()
        {
            StopSpeaking();
        }

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Speaks <paramref name="text"/> through Windows SAPI via PowerShell.
        /// Any currently playing speech is cancelled first.
        /// </summary>
        public void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                UnityEngine.Debug.LogWarning($"{LogPrefix} Speak called with empty text — ignoring.");
                return;
            }

            StopSpeaking();

            string safeText = SanitiseForPowerShell(text);
            string script   = BuildPowerShellScript(safeText);

            _isSpeaking  = true;
            _speakThread = new Thread(() => RunPowerShell(script))
            {
                IsBackground = true,
                Name         = "CoachTTS"
            };
            _speakThread.Start();

            UnityEngine.Debug.Log($"{LogPrefix} Speaking ({text.Length} chars).");
        }

        /// <summary>Kills the PowerShell process and stops speech immediately.</summary>
        public void StopSpeaking()
        {
            lock (_lock)
            {
                try
                {
                    if (_powershellProcess != null && !_powershellProcess.HasExited)
                        _powershellProcess.Kill();
                }
                catch { /* process may have already exited */ }

                _powershellProcess = null;
            }

            _isSpeaking = false;
        }

        // ─── Private ──────────────────────────────────────────────────────────────

        private void RunPowerShell(string script)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName               = "powershell.exe",
                    Arguments              = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError  = false,
                };

                lock (_lock)
                {
                    _powershellProcess = new Process { StartInfo = startInfo };
                    _powershellProcess.Start();
                }

                _powershellProcess.WaitForExit();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"{LogPrefix} PowerShell error: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    _powershellProcess?.Dispose();
                    _powershellProcess = null;
                }

                _isSpeaking = false;
            }
        }

        /// <summary>Builds the PowerShell SAPI one-liner with rate and volume applied.</summary>
        private string BuildPowerShellScript(string safeText)
        {
            return $"Add-Type -AssemblyName System.Speech; " +
                   $"$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                   $"$s.Rate = {speechRate}; " +
                   $"$s.Volume = {speechVolume}; " +
                   $"$s.Speak('{safeText}');";
        }

        /// <summary>
        /// Removes characters that would break the PowerShell single-quoted string.
        /// Single quotes are doubled (PowerShell escape), newlines are replaced with spaces.
        /// </summary>
        private static string SanitiseForPowerShell(string text)
        {
            if (text == null) return string.Empty;

            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\'': sb.Append("''"); break;  // PowerShell escapes ' as ''
                    case '\n':
                    case '\r': sb.Append(' ');  break;
                    default:   sb.Append(c);    break;
                }
            }
            return sb.ToString();
        }
    }
}
