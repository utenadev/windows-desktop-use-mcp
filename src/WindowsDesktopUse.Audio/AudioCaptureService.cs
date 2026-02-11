using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using WindowsDesktopUse.Core;

namespace WindowsDesktopUse.Audio;

/// <summary>
/// Service for capturing audio from system or microphone
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    private readonly ConcurrentDictionary<string, AudioSession> _sessions = new();
    private readonly ConcurrentDictionary<string, IWaveIn> _captures = new();
    private readonly ConcurrentDictionary<string, WaveFileWriter> _writers = new();
    private readonly ConcurrentDictionary<string, MemoryStream> _buffers = new();
    private bool _disposed;

    /// <summary>
    /// List available audio capture devices
    /// </summary>
    public static List<AudioDeviceInfo> GetAudioDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var capabilities = WaveIn.GetCapabilities(i);
            devices.Add(new AudioDeviceInfo(
                i,
                capabilities.ProductName,
                "microphone",
                capabilities.Channels
            ));
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            devices.Add(new AudioDeviceInfo(
                -1,
                "System Audio (Loopback)",
                "system",
                defaultDevice.AudioClient.MixFormat.Channels
            ));
        }
        catch
        {
        }

        return devices;
    }

    /// <summary>
    /// Start audio capture session
    /// </summary>
    public AudioSession StartCapture(AudioCaptureSource source, int sampleRate = 44100, int? deviceIndex = null)
    {
        var sessionId = Guid.NewGuid().ToString();
        var tempPath = Path.Combine(Path.GetTempPath(), $"audio_capture_{sessionId}.wav");

        IWaveIn? capture = null;
        WaveFileWriter? writer = null;
        MemoryStream? buffer = null;

        try
        {
            switch (source)
            {
                case AudioCaptureSource.System:
                    capture = new WasapiLoopbackCapture();
                    break;

                case AudioCaptureSource.Microphone:
                    capture = new WaveInEvent
                    {
                        DeviceNumber = deviceIndex ?? 0,
                        WaveFormat = new WaveFormat(sampleRate, 16, 1)
                    };
                    break;

                case AudioCaptureSource.Both:
                    capture = new WasapiLoopbackCapture();
                    break;

                default:
                    throw new ArgumentException($"Unknown audio source: {source}");
            }

            writer = new WaveFileWriter(tempPath, capture.WaveFormat);
            buffer = new MemoryStream();

            capture.DataAvailable += (s, e) =>
            {
                writer.Write(e.Buffer, 0, e.BytesRecorded);
                buffer.Write(e.Buffer, 0, e.BytesRecorded);
            };

            capture.RecordingStopped += (s, e) =>
            {
                writer?.Dispose();

                if (e.Exception != null)
                {
                    Console.Error.WriteLine($"[Audio] Recording error: {e.Exception.Message}");
                }
            };

            capture.StartRecording();

            var session = new AudioSession(sessionId, source, "recording", DateTime.UtcNow, tempPath);
            _sessions[sessionId] = session;
            _captures[sessionId] = capture;
            _writers[sessionId] = writer;
            _buffers[sessionId] = buffer;

            return session;
        }
        catch
        {
            capture?.Dispose();
            writer?.Dispose();
            buffer?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Stop audio capture and return the result
    /// </summary>
    public async Task<AudioCaptureResult> StopCaptureAsync(string sessionId, bool returnBase64 = true)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new ArgumentException($"Audio session {sessionId} not found");
        }

        if (_captures.TryRemove(sessionId, out var capture))
        {
            capture.StopRecording();
        }

        await Task.Delay(100).ConfigureAwait(false);

        if (_writers.TryRemove(sessionId, out var writer))
        {
            writer.Dispose();
        }

        capture?.Dispose();

        if (_buffers.TryRemove(sessionId, out var buffer))
        {
            buffer.Dispose();
        }

        byte[] audioData;
        if (session.OutputPath != null && File.Exists(session.OutputPath))
        {
            audioData = File.ReadAllBytes(session.OutputPath);
        }
        else
        {
            throw new InvalidOperationException("Audio file not found after capture");
        }

        var duration = DateTime.UtcNow - session.StartTime;

        _sessions.TryUpdate(sessionId,
            session with { Status = "completed" },
            session);

        string audioBase64 = returnBase64 ? Convert.ToBase64String(audioData) : "";

        return new AudioCaptureResult(
            sessionId,
            audioBase64,
            "wav",
            44100,
            session.Source == AudioCaptureSource.Microphone ? 1 : 2,
            duration,
            session.OutputPath
        );
    }

    /// <summary>
    /// Get active audio sessions
    /// </summary>
    public List<AudioSession> GetActiveSessions()
    {
        return _sessions.Values.Where(s => s.Status == "recording").ToList();
    }

    /// <summary>
    /// Try to get session by ID
    /// </summary>
    public bool TryGetSession(string sessionId, out AudioSession? session)
    {
        return _sessions.TryGetValue(sessionId, out session);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var sessionId in _captures.Keys.ToList())
            {
                try
                {
                    StopCaptureAsync(sessionId, false).GetAwaiter().GetResult();
                }
                catch { }
            }

            _disposed = true;
        }
    }
}
