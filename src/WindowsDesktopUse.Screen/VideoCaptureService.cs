using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using WindowsDesktopUse.Core;

namespace WindowsDesktopUse.Screen;

/// <summary>
/// High-efficiency video capture service with optimized GDI+ processing
/// </summary>
public sealed class VideoCaptureService : IDisposable
{
    private readonly VideoTargetFinder _targetFinder;
    private readonly HybridCaptureService _captureService;
    private readonly Dictionary<string, VideoSession> _sessions = new();
    private readonly object _lock = new();
    private bool _disposed;

    public VideoCaptureService()
    {
        _targetFinder = new VideoTargetFinder();
        _captureService = new HybridCaptureService(new ScreenCaptureService(0), CaptureApiPreference.Auto);
    }

    /// <summary>
    /// Start video capture session
    /// </summary>
    public async Task<string> StartVideoStreamAsync(
        string targetName,
        int fps = 10,
        int quality = 65,
        int maxWidth = 640,
        bool enableChangeDetection = true,
        double changeThreshold = 0.08,
        int keyFrameInterval = 10,
        Func<VideoPayload, Task>? onFrameCaptured = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VideoCaptureService));

        Console.Error.WriteLine($"[VideoCapture] Starting video stream for target: {targetName}");

        // Find video target
        var target = _targetFinder.FindVideoTarget(targetName);
        if (target == null)
        {
            throw new InvalidOperationException($"Video target not found: {targetName}");
        }

        var sessionId = Guid.NewGuid().ToString();
        
        var session = new VideoSession
        {
            Id = sessionId,
            TargetName = targetName,
            TargetInfo = target,
            Fps = fps,
            Quality = quality,
            MaxWidth = maxWidth,
            EnableChangeDetection = enableChangeDetection,
            ChangeThreshold = changeThreshold,
            KeyFrameInterval = keyFrameInterval,
            FrameIntervalMs = 1000 / fps,
            OnFrameCaptured = onFrameCaptured,
            CancellationToken = cancellationToken
        };

        lock (_lock)
        {
            _sessions[sessionId] = session;
        }

        // Start capture loop
        _ = Task.Run(() => CaptureLoopAsync(session), cancellationToken);

        Console.Error.WriteLine($"[VideoCapture] Video stream started: {sessionId}");
        return sessionId;
    }

    /// <summary>
    /// Stop video capture session
    /// </summary>
    public void StopVideoStream(string sessionId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.CancellationTokenSource?.Cancel();
                session.Dispose();
                _sessions.Remove(sessionId);
                Console.Error.WriteLine($"[VideoCapture] Video stream stopped: {sessionId}");
            }
        }
    }

    /// <summary>
    /// Get latest payload for session
    /// </summary>
    public VideoPayload? GetLatestPayload(string sessionId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                return session.LatestPayload;
            }
        }
        return null;
    }

    /// <summary>
    /// Get all active session IDs
    /// </summary>
    public IReadOnlyList<string> GetActiveSessions()
    {
        lock (_lock)
        {
            return _sessions.Keys.ToList();
        }
    }

    private async Task CaptureLoopAsync(VideoSession session)
    {
        using var detector = session.EnableChangeDetection ? new VisualChangeDetector() : null;
        if (detector != null)
        {
            detector.GridSize = 16;
            detector.ChangeThreshold = session.ChangeThreshold;
            detector.KeyFrameInterval = session.KeyFrameInterval;
        }

        var cts = new CancellationTokenSource();
        session.CancellationTokenSource = cts;

        // 絶対時刻スケジュール: 次のキャプチャ予定時刻を管理
        var nextCaptureTime = session.StartTime;

        try
        {
            while (!cts.Token.IsCancellationRequested && !session.CancellationToken.IsCancellationRequested)
            {
                // 次のキャプチャ時刻まで待機（絶対時刻ベース）
                var waitMs = (int)(nextCaptureTime - DateTime.UtcNow).TotalMilliseconds;
                if (waitMs > 0)
                {
                    try
                    {
                        await Task.Delay(waitMs, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                else if (waitMs < -500)
                {
                    // 大幅な遅延を警告
                    Console.Error.WriteLine($"[VideoCapture] Warning: Capture delayed by {-waitMs}ms");
                }

                try
                {
                    // Get updated target position
                    var target = _targetFinder.GetUpdatedPosition(session.TargetName);
                    if (target == null)
                    {
                        Console.Error.WriteLine($"[VideoCapture] Target lost: {session.TargetName}");
                        break;
                    }

                    session.TargetInfo = target;

                    // Capture frame
                    using var frame = await CaptureVideoFrameAsync(target, session.MaxWidth).ConfigureAwait(false);

                    if (frame == null) continue;

                    // Analyze for changes
                    ChangeAnalysisResult? analysis = null;
                    if (detector != null)
                    {
                        analysis = detector.AnalyzeFrame(frame);
                        if (!analysis.ShouldSend)
                        {
                            // Skip this frame but still schedule next capture
                            nextCaptureTime = nextCaptureTime.AddMilliseconds(session.FrameIntervalMs);
                            continue;
                        }
                    }

                    // Encode frame
                    var imageData = await Task.Run(() => 
                        EncodeToJpeg(frame, session.Quality));

                    // 実測タイムスタンプ: キャプチャ処理完了直後の時刻を使用
                    var captureCompletedTime = DateTime.UtcNow;
                    var ts = (captureCompletedTime - session.StartTime).TotalSeconds;

                    // Create payload with actual capture timestamp
                    var payload = CreateVideoPayload(
                        session, 
                        imageData, 
                        ts,
                        analysis?.EventTag ?? "Frame");

                    session.LatestPayload = payload;

                    // Send to callback
                    if (session.OnFrameCaptured != null)
                    {
                        await session.OnFrameCaptured(payload);
                    }

                    // 次のキャプチャ時刻を更新（厳密な間隔維持）
                    nextCaptureTime = nextCaptureTime.AddMilliseconds(session.FrameIntervalMs);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[VideoCapture] Capture error: {ex.Message}");
                    // エラー時も次のキャプチャ時刻を更新
                    nextCaptureTime = nextCaptureTime.AddMilliseconds(session.FrameIntervalMs);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VideoCapture] Capture loop error: {ex.Message}");
        }
        finally
        {
            Console.Error.WriteLine($"[VideoCapture] Capture loop ended: {session.Id}");
        }
    }

    private async Task<Bitmap?> CaptureVideoFrameAsync(VideoTargetInfo target, int maxWidth)
    {
        try
        {
            // Try modern capture first (supports GPU-accelerated content)
            if (_captureService.IsAvailable)
            {
                try
                {
                    var modernBitmap = await _captureService.CaptureWindowAsync(target.WindowHandle).ConfigureAwait(false);
                    if (modernBitmap != null)
                    {
                        // Resize if needed
                        if (modernBitmap.Width > maxWidth)
                        {
                            var newHeight = (int)(modernBitmap.Height * ((double)maxWidth / modernBitmap.Width));
                            using (modernBitmap)
                            {
                                var resized = new Bitmap(maxWidth, newHeight);
                                using (var g = Graphics.FromImage(resized))
                                {
                                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
                                    g.DrawImage(modernBitmap, 0, 0, maxWidth, newHeight);
                                }
                                return resized;
                            }
                        }
                        return modernBitmap;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[VideoCapture] Modern capture failed: {ex.Message}");
                }
            }

            // Fallback to legacy GDI+ capture
            var width = target.Width;
            var height = target.Height;

            // Skip if too small
            if (width < 100 || height < 100)
                return null;

            // Capture from screen
            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                // Use Bilinear for performance (faster than HighQualityBicubic)
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;

                g.CopyFromScreen(target.X, target.Y, 0, 0, new Size(width, height));
            }

            // Resize if needed
            if (width > maxWidth)
            {
                var newHeight = (int)(height * ((double)maxWidth / width));
                var resized = new Bitmap(maxWidth, newHeight);
                using (var g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
                    g.DrawImage(bmp, 0, 0, maxWidth, newHeight);
                }
                return resized;
            }

            return new Bitmap(bmp);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VideoCapture] Frame capture error: {ex.Message}");
            return null;
        }
    }

    private string EncodeToJpeg(Bitmap frame, int quality)
    {
        using var ms = new MemoryStream();
        
        var codecInfo = GetEncoderInfo(ImageFormat.Jpeg);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);

        frame.Save(ms, codecInfo, encoderParams);
        
        return Convert.ToBase64String(ms.ToArray());
    }

    private VideoPayload CreateVideoPayload(VideoSession session, string imageData, double ts, string eventTag)
    {
        var now = DateTime.UtcNow;
        var target = session.TargetInfo;

        return new VideoPayload(
            Timestamp: TimeSpan.FromSeconds(ts).ToString(@"hh\:mm\:ss\.f"),
            SystemTime: now.ToString("O"),
            WindowInfo: new VideoWindowInfo(
                Title: target.WindowTitle,
                IsActive: true
            ),
            VisualMetadata: new VideoVisualMetadata(
                HasChange: eventTag != "No Change",
                EventTag: eventTag
            ),
            ImageData: imageData,
            OcrText: null // TODO: Implement OCR if needed
        );
    }

    private static ImageCodecInfo GetEncoderInfo(ImageFormat format)
    {
        return ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == format.Guid);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                foreach (var session in _sessions.Values)
                {
                    session.Dispose();
                }
                _sessions.Clear();
            }
            _targetFinder?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Video capture session state
    /// </summary>
    private class VideoSession : IDisposable
    {
        public string Id { get; set; } = "";
        public string TargetName { get; set; } = "";
        public VideoTargetInfo TargetInfo { get; set; } = null!;
        public int Fps { get; set; }
        public int Quality { get; set; }
        public int MaxWidth { get; set; }
        public bool EnableChangeDetection { get; set; }
        public double ChangeThreshold { get; set; }
        public int KeyFrameInterval { get; set; }
        public int FrameIntervalMs { get; set; }
        public Func<VideoPayload, Task>? OnFrameCaptured { get; set; }
        public CancellationToken CancellationToken { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; }
        public VideoPayload? LatestPayload { get; set; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        public TimeSpan GetRelativeTime() => DateTime.UtcNow - StartTime;

        public void Dispose()
        {
            CancellationTokenSource?.Cancel();
            CancellationTokenSource?.Dispose();
        }
    }
}
