using System.Threading.Channels;

namespace WindowsDesktopUse.Core;

/// <summary>
/// Monitor information
/// </summary>
public record MonitorInfo(uint Idx, string Name, int W, int H, int X, int Y);

/// <summary>
/// Window information
/// </summary>
public record WindowInfo(long Hwnd, string Title, int W, int H, int X, int Y);

/// <summary>
/// Stream session for continuous capture
/// </summary>
public class StreamSession
{
    public string Id = "";
    public string TargetType = "monitor";
    public uint MonIdx;
    public long Hwnd;
    public int Interval;
    public int Quality;
    public int MaxW;
    public CancellationTokenSource Cts = new();
    public Channel<string> Channel { get; }

    private string _latestFrame = "";
    private readonly object _frameLock = new();

    public string LatestFrame
    {
        get
        {
            lock (_frameLock)
            {
                return _latestFrame;
            }
        }
        set
        {
            lock (_frameLock)
            {
                _latestFrame = value;
                LastFrameHash = ComputeHash(value);
            }
        }
    }

    public string LastFrameHash { get; private set; } = "";
    public DateTime LastCaptureTime { get; set; }

    private static string ComputeHash(string data)
    {
        if (string.IsNullOrEmpty(data)) return "";
        var bytes = System.Text.Encoding.UTF8.GetBytes(data);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    public StreamSession()
    {
        Channel = System.Threading.Channels.Channel.CreateUnbounded<string>();
    }
}

/// <summary>
/// Capture target information
/// </summary>
public record CaptureTarget(
    string Type,
    string Id,
    string Name,
    int Width,
    int Height,
    int X,
    int Y
);

/// <summary>
/// List of capture targets
/// </summary>
public record CaptureTargets(
    List<CaptureTarget> Monitors,
    List<CaptureTarget> Windows,
    int TotalCount
);

/// <summary>
/// Capture result
/// </summary>
public record CaptureResult(
    string ImageData,
    string MimeType,
    int Width,
    int Height,
    string TargetType,
    string TargetId
);

/// <summary>
/// Watch session information
/// </summary>
public record WatchSession(
    string SessionId,
    string TargetType,
    string TargetId,
    int IntervalMs,
    string Status
);

/// <summary>
/// Audio capture source type
/// </summary>
public enum AudioCaptureSource
{
    System,
    Microphone,
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
    TimeSpan Duration,
    string? OutputPath = null
);

/// <summary>
/// Audio device information
/// </summary>
public record AudioDeviceInfo(
    int Index,
    string Name,
    string Type,
    int Channels
);

/// <summary>
/// Whisper model sizes
/// </summary>
public enum WhisperModelSize
{
    Tiny,
    Base,
    Small,
    Medium,
    Large
}

/// <summary>
/// Transcription segment with timing
/// </summary>
public record TranscriptionSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    double Probability,
    string? Language = null
);

/// <summary>
/// Transcription result
/// </summary>
public record TranscriptionResult(
    string SessionId,
    List<TranscriptionSegment> Segments,
    string Language,
    TimeSpan Duration,
    string ModelUsed
);

/// <summary>
/// Model information
/// </summary>
public record ModelInfo(
    string Size,
    string Performance,
    string BestFor
);
