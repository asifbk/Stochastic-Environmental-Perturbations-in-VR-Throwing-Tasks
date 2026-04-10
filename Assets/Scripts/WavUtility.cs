using System;
using UnityEngine;

namespace Basketball
{
    /// <summary>
    /// Converts a raw WAV byte array (PCM 16-bit, mono or stereo) into a Unity AudioClip.
    /// Supports standard WAV files with a RIFF header as returned by most local TTS servers.
    /// </summary>
    public static class WavUtility
    {
        private const int RiffHeaderSize   = 44; // Minimum canonical WAV header size
        private const int SampleSizeBytes  = 2;  // 16-bit PCM = 2 bytes per sample

        /// <summary>
        /// Decodes <paramref name="wavData"/> and returns a new AudioClip,
        /// or null if the data is invalid or unsupported.
        /// The caller is responsible for destroying the returned clip when done.
        /// </summary>
        public static AudioClip ToAudioClip(byte[] wavData)
        {
            if (wavData == null || wavData.Length < RiffHeaderSize)
            {
                Debug.LogError("[WavUtility] Data is null or too short to be a valid WAV file.");
                return null;
            }

            // Verify RIFF signature.
            if (wavData[0] != 'R' || wavData[1] != 'I' || wavData[2] != 'F' || wavData[3] != 'F')
            {
                Debug.LogError("[WavUtility] Missing RIFF header — not a valid WAV file.");
                return null;
            }

            // Parse header fields (little-endian).
            int    channels      = BitConverter.ToInt16(wavData, 22);
            int    sampleRate    = BitConverter.ToInt32(wavData, 24);
            short  bitsPerSample = BitConverter.ToInt16(wavData, 34);

            if (bitsPerSample != 16)
            {
                Debug.LogError($"[WavUtility] Unsupported bit depth: {bitsPerSample} (only 16-bit PCM is supported).");
                return null;
            }

            // Locate the 'data' sub-chunk to find where PCM samples start.
            int dataOffset = FindDataChunkOffset(wavData);
            if (dataOffset < 0)
            {
                Debug.LogError("[WavUtility] Could not find 'data' sub-chunk in WAV file.");
                return null;
            }

            int dataSize    = BitConverter.ToInt32(wavData, dataOffset + 4);
            int sampleStart = dataOffset + 8;

            // Guard against truncated files.
            int availableBytes = wavData.Length - sampleStart;
            if (availableBytes <= 0)
            {
                Debug.LogError("[WavUtility] WAV data chunk is empty.");
                return null;
            }

            int bytesToRead  = Mathf.Min(dataSize, availableBytes);
            int sampleCount  = bytesToRead / SampleSizeBytes;

            // Convert 16-bit PCM bytes to normalised float samples.
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int byteIndex  = sampleStart + i * SampleSizeBytes;
                short pcm      = BitConverter.ToInt16(wavData, byteIndex);
                samples[i]     = pcm / 32768f; // Normalise to [-1, 1]
            }

            AudioClip clip = AudioClip.Create(
                name:       "CoachVoice",
                lengthSamples: sampleCount / channels,
                channels:   channels,
                frequency:  sampleRate,
                stream:     false
            );

            clip.SetData(samples, offsetSamples: 0);
            return clip;
        }

        /// <summary>
        /// Scans the WAV byte array for the 'data' sub-chunk and returns its offset,
        /// or -1 if not found. This handles non-canonical headers that include extra
        /// sub-chunks (e.g. 'LIST', 'fact') before the data payload.
        /// </summary>
        private static int FindDataChunkOffset(byte[] data)
        {
            // WAV sub-chunks start after the 12-byte RIFF header.
            int offset = 12;
            while (offset + 8 <= data.Length)
            {
                char c0 = (char)data[offset];
                char c1 = (char)data[offset + 1];
                char c2 = (char)data[offset + 2];
                char c3 = (char)data[offset + 3];

                if (c0 == 'd' && c1 == 'a' && c2 == 't' && c3 == 'a')
                    return offset;

                // Skip this sub-chunk: 4-byte ID + 4-byte size + <size> bytes of payload.
                int chunkSize = BitConverter.ToInt32(data, offset + 4);
                offset += 8 + chunkSize;
            }

            return -1;
        }
    }
}
