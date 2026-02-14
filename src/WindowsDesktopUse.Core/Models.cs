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
public class StreamSession : IDisposable
{
    public string Id { get; set; } = "";
    public string TargetType { get; set; } = "monitor";
    public uint MonIdx { get; set; }
    public long Hwnd { get; set; }
    public int Interval { get; set; }
    public int Quality { get; set; }
    public int MaxW { get; set; }
    public int RegionX { get; set; }
    public int RegionY { get; set; }
    public int RegionW { get; set; }
    public int RegionH { get; set; }
    public CancellationTokenSource Cts { get; set; } = new();
    public Channel<string> Channel { get; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the relative time in seconds from session start
    /// </summary>
    public double RelativeTime => (DateTime.UtcNow - StartTime).TotalSeconds;

    private string _latestFrame = "";
    private readonly object _frameLock = new();
    private bool _disposed;

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

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Cts?.Cancel();
                Cts?.Dispose();
            }
            _disposed = true;
        }
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
    IReadOnlyList<CaptureTarget> Monitors,
    IReadOnlyList<CaptureTarget> Windows,
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
    IReadOnlyList<TranscriptionSegment> Segments,
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

/// <summary>
/// Video capture payload for LLM consumption
/// </summary>
public record VideoPayload(
    string Timestamp,
    string SystemTime,
    VideoWindowInfo WindowInfo,
    VideoVisualMetadata VisualMetadata,
    string ImageData,
    string? OcrText = null
);

/// <summary>
/// Window information for video payload
/// </summary>
public record VideoWindowInfo(
    string Title,
    bool IsActive
);

/// <summary>
/// Visual metadata for video payload
/// </summary>
public record VideoVisualMetadata(
    bool HasChange,
    string EventTag
);

/// <summary>
/// Video target information for UI automation
/// </summary>
public record VideoTargetInfo(
    IntPtr WindowHandle,
    string WindowTitle,
    string ElementName,
    int X,
    int Y,
    int Width,
    int Height
);

/// <summary>
/// Video stream session configuration
/// </summary>
public record VideoStreamConfig(
    string TargetName,
    int Fps,
    int Quality,
    int MaxWidth,
    bool EnableChangeDetection,
    double ChangeThreshold,
    int KeyFrameIntervalSeconds
);

/// <summary>
/// Unified event payload for video and audio timeline integration
/// </summary>
public record UnifiedEventPayload(
    string SessionId,
    string SystemTime,
    double RelativeTime,
    string Type, // "video" or "audio"
    string Data, // Base64 image or transcription text
    UnifiedEventMetadata Metadata
);

/// <summary>
/// Metadata for unified event payload
/// </summary>
public record UnifiedEventMetadata(
    string? WindowTitle = null,
    float? Volume = null,
    string? Hash = null,
    string? Language = null,
    double? SegmentStartTime = null,
    double? SegmentEndTime = null
);

/// <summary>
/// Monitor sensitivity levels for change detection
/// </summary>
public enum MonitorSensitivity
{
    High = 1,   // Threshold 0.01 (1%)
    Medium = 5, // Threshold 0.05 (5%)
    Low = 15    // Threshold 0.15 (15%)
}

/// <summary>
/// Monitor session information
/// </summary>
public class MonitorSession : IDisposable
{
    public string Id { get; set; } = "";
    public long Hwnd { get; set; }
    public MonitorSensitivity Sensitivity { get; set; } = MonitorSensitivity.Medium;
    public int IntervalMs { get; set; } = 500;
    public CancellationTokenSource Cts { get; set; } = new();
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    private bool _disposed;

    public void Dispose()
    {
        if (!_disposed)
        {
            Cts.Cancel();
            Cts.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Video co-view payload for synchronized video/audio capture
/// </summary>
public record VideoCoViewPayload(
    string SessionId,
    double Ts,
    string Frame,
    string? Transcript,
    string WindowTitle
);

/// <summary>
/// Video co-view session configuration
/// </summary>
public class VideoCoViewSession : IDisposable
{
    public string Id { get; set; } = "";
    public long Hwnd { get; set; }
    public int IntervalMs { get; set; } = 2000;
    public int Quality { get; set; } = 60;
    public int MaxWidth { get; set; } = 640;
    public string ModelSize { get; set; } = "base";
    public string Language { get; set; } = "";
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public CancellationTokenSource Cts { get; set; } = new();
    private bool _disposed;

    public double GetTs() => (DateTime.UtcNow - StartTime).TotalSeconds;

    public void Dispose()
    {
        if (!_disposed)
        {
            Cts.Cancel();
            Cts.Dispose();
            _disposed = true;
        }
    }
}
