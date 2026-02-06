using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Collections.Concurrent;

/// <summary>
/// Audio capture source type
/// </summary>
public enum AudioCaptureSource
{
    /// <summary>System audio output (loopback)</summary>
    System,
    /// <summary>Microphone input</summary>
    Microphone,
    /// <summary>Both system and microphone mixed</summary>
    Both
}

/// <summary>
/// Audio session information
/// </summary>
public record AudioSession(
    string SessionId,
    AudioCaptureSource Source,
    string Status,
    DateTime StartTime,
    string? OutputPath = null
);

/// <summary>
/// Audio capture result
/// </summary>
public record AudioCaptureResult(
    string SessionId,
    string AudioDataBase64,
    string Format,
    int SampleRate,
    int Channels,
    TimeSpan Duration
);

/// <summary>
/// Service for capturing audio from system or microphone
/// </summary>
public class AudioCaptureService : IDisposable
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
        
        // Get microphone devices
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

        // Check if loopback is available (Windows Vista+)
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            devices.Add(new AudioDeviceInfo(
                -1, // Special index for system audio
                "System Audio (Loopback)",
                "system",
                defaultDevice.AudioClient.MixFormat.Channels
            ));
        }
        catch
        {
            // Loopback not available
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
                    // For both, we'll capture system audio as primary
                    // and mix microphone in later if needed
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
    public AudioCaptureResult StopCapture(string sessionId, bool returnBase64 = true)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new ArgumentException($"Audio session {sessionId} not found");
        }

        // Stop recording first
        if (_captures.TryRemove(sessionId, out var capture))
        {
            capture.StopRecording();
        }

        // Wait a moment for the RecordingStopped event to complete
        Thread.Sleep(100);

        // Close writer - this finalizes the WAV file with proper headers
        if (_writers.TryRemove(sessionId, out var writer))
        {
            writer.Dispose();
        }

        // Dispose capture
        capture?.Dispose();

        // Clean up buffer (we don't use it for return - file has proper WAV headers)
        if (_buffers.TryRemove(sessionId, out var buffer))
        {
            buffer.Dispose();
        }

        // Read the properly formatted WAV file
        byte[] audioData;
        if (session.OutputPath != null && File.Exists(session.OutputPath))
        {
            audioData = File.ReadAllBytes(session.OutputPath);
        }
        else
        {
            throw new InvalidOperationException("Audio file not found after capture");
        }

        // Calculate duration
        var duration = DateTime.UtcNow - session.StartTime;

        // Update session status
        _sessions.TryUpdate(sessionId, 
            session with { Status = "completed" }, 
            session);

        // Convert to base64 if requested
        string audioBase64 = returnBase64 ? Convert.ToBase64String(audioData) : "";

        return new AudioCaptureResult(
            sessionId,
            audioBase64,
            "wav",
            44100, // Default sample rate
            session.Source == AudioCaptureSource.Microphone ? 1 : 2,
            duration
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
            // Stop all active captures
            foreach (var sessionId in _captures.Keys.ToList())
            {
                try
                {
                    StopCapture(sessionId, false);
                }
                catch { }
            }

            _disposed = true;
        }
    }
}

/// <summary>
/// Audio device information
/// </summary>
public record AudioDeviceInfo(
    int Index,
    string Name,
    string Type,
    int Channels
);
