using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WindowsDesktopUse.Core;

namespace WindowsDesktopUse.Screen;

/// <summary>
/// Service for capturing screen, monitors, and windows
/// </summary>
public class ScreenCaptureService
{
    private readonly uint _defaultMon;
    private readonly Dictionary<string, StreamSession> _sessions = new();
    private List<MonitorInfo> _monitors = new();
    private readonly Dictionary<string, DateTime> _streamStartTimes = new();

    public Func<string, string, Task>? OnFrameCaptured { get; set; }

    /// <summary>
    /// When true, overlays timestamp and event tags on captured frames for AI-friendly understanding.
    /// </summary>
    public bool EnableOverlay { get; set; } = false;

    /// <summary>
    /// Optional event tag provider for overlay (e.g., "SCENE CHANGE").
    /// </summary>
    public Func<string, string?>? GetEventTag { get; set; }

    public ScreenCaptureService(uint defaultMon) => _defaultMon = defaultMon;

    public void InitializeMonitors()
    {
        _monitors = EnumMonitors();
        Console.Error.WriteLine($"[Capture] Found {_monitors.Count} monitors");
    }

#pragma warning disable CA1024
    public IReadOnlyList<MonitorInfo> GetMonitors() => _monitors;
#pragma warning restore CA1024

    public string CaptureSingle(uint idx, int maxW, int quality)
    {
        return CaptureSingleInternal(idx, maxW, quality, "default");
    }

    private string CaptureSingleInternal(uint idx, int maxW, int quality, string streamId)
    {
        if (idx >= _monitors.Count)
            throw new ArgumentOutOfRangeException(nameof(idx), $"Monitor index {idx} is out of range. Available: 0-{_monitors.Count - 1}");
        var mon = _monitors[(int)idx];
        using var bmp = new Bitmap(mon.W, mon.H);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(mon.X, mon.Y, 0, 0, new Size(mon.W, mon.H));
        }

        // Apply AI-friendly overlays if enabled
        if (EnableOverlay)
        {
            var startTime = _streamStartTimes.GetValueOrDefault(streamId, DateTime.UtcNow);
            var elapsed = DateTime.UtcNow - startTime;
            ImageOverlayService.OverlayTimestamp(bmp, elapsed);

            var eventTag = GetEventTag?.Invoke(streamId);
            ImageOverlayService.OverlayEventTag(bmp, eventTag);
        }

        return ToJpegBase64(bmp, maxW, quality);
    }

    public string StartStream(uint idx, int interval, int quality, int maxW)
    {
        var id = Guid.NewGuid().ToString();
        var sess = new StreamSession { Id = id, TargetType = "monitor", MonIdx = idx, Interval = interval, Quality = quality, MaxW = maxW };
        _sessions[id] = sess;
        _streamStartTimes[id] = DateTime.UtcNow;
        _ = StreamLoop(sess);
        return id;
    }

    public string StartWindowStream(long hwnd, int interval, int quality, int maxW)
    {
        var id = Guid.NewGuid().ToString();
        var sess = new StreamSession { Id = id, TargetType = "window", Hwnd = hwnd, Interval = interval, Quality = quality, MaxW = maxW };
        _sessions[id] = sess;
        _streamStartTimes[id] = DateTime.UtcNow;
        _ = StreamLoop(sess);
        return id;
    }

    public void StopStream(string id)
    {
        if (_sessions.Remove(id, out var s))
        {
            s.Cts.Cancel();
            s.Cts.Dispose();
        }
        _streamStartTimes.Remove(id);
    }

    public bool TryGetSession(string id, out StreamSession? s) => _sessions.TryGetValue(id, out s);

    public StreamSession? GetSession(string id)
    {
        _sessions.TryGetValue(id, out var s);
        return s;
    }

    public void StopAllStreams()
    {
        Console.Error.WriteLine($"[Capture] Stopping all {_sessions.Count} streams...");
        foreach (var session in _sessions.Values)
        {
            session.Cts.Cancel();
        }
        _sessions.Clear();
        _streamStartTimes.Clear();
    }

    private async Task StreamLoop(StreamSession s)
    {
        try
        {
            while (!s.Cts.Token.IsCancellationRequested)
            {
                var start = DateTime.UtcNow;
                try
                {
                    string img;
                    if (s.TargetType == "window")
                    {
                        img = CaptureWindowInternal(s.Hwnd, s.MaxW, s.Quality, s.Id);
                    }
                    else
                    {
                        img = CaptureSingleInternal(s.MonIdx, s.MaxW, s.Quality, s.Id);
                    }
                    await s.Channel.Writer.WriteAsync(img, s.Cts.Token).ConfigureAwait(false);

                    s.LatestFrame = img;
                    s.LastCaptureTime = DateTime.UtcNow;

                    if (OnFrameCaptured != null)
                    {
                        try
                        {
                            await OnFrameCaptured(s.Id, img).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[Stream {s.Id}] Failed to send notification: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.Error.WriteLine($"[Stream {s.Id}] Capture error: {ex.Message}");
                    await Task.Delay(1000, s.Cts.Token).ConfigureAwait(false);
                    continue;
                }

                var elapsed = (int)(DateTime.UtcNow - start).TotalMilliseconds;
                var delay = s.Interval - elapsed;
                if (delay > 0)
                {
                    try
                    {
                        await Task.Delay(delay, s.Cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Stream {s.Id}] Fatal error: {ex.Message}");
        }
        finally
        {
            s.Channel.Writer.Complete();
            Console.Error.WriteLine($"[Stream {s.Id}] Completed");
        }
    }

    private static string ToJpegBase64(Bitmap src, int maxW, int q)
    {
        using var ms = new MemoryStream();
        var target = src.Width > maxW ? new Bitmap(src, new Size(maxW, (int)(src.Height * ((double)maxW / src.Width)))) : src;
        try
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(Encoder.Quality, q);
            target.Save(ms, codec, p);
            return Convert.ToBase64String(ms.ToArray());
        }
        finally { if (target != src) target.Dispose(); }
    }

    private static List<MonitorInfo> EnumMonitors()
    {
        var list = new List<MonitorInfo>();
        var handle = GCHandle.Alloc(list);
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumMonCallback, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return list;
    }

    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr rc, EnumMonDelegate del, IntPtr dw);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsDelegate lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    delegate bool EnumMonDelegate(IntPtr h, IntPtr hdc, ref RECT rc, IntPtr d);
    delegate bool EnumWindowsDelegate(IntPtr hWnd, IntPtr lParam);

    static bool EnumMonCallback(IntPtr h, IntPtr hdc, ref RECT rc, IntPtr d)
    {
        var list = (List<MonitorInfo>)GCHandle.FromIntPtr(d).Target!;
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (GetMonitorInfo(h, ref mi))
        {
            var w = mi.rcMonitor.Right - mi.rcMonitor.Left;
            var hgt = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
            list.Add(new MonitorInfo((uint)list.Count, mi.szDevice, w, hgt, mi.rcMonitor.Left, mi.rcMonitor.Top));
        }
        return true;
    }

    public static IReadOnlyList<WindowInfo> GetWindows()
    {
        var windows = new List<WindowInfo>();
        var handle = GCHandle.Alloc(windows);
        try
        {
            EnumWindows((hwnd, param) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                var sb = new System.Text.StringBuilder(256);
                _ = GetWindowText(hwnd, sb, 256);
                var title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                GetWindowRect(hwnd, out var rect);
                var w = rect.Right - rect.Left;
                var h = rect.Bottom - rect.Top;
                if (w <= 0 || h <= 0) return true;

                var list = (List<WindowInfo>)GCHandle.FromIntPtr(param).Target!;
                list.Add(new WindowInfo(hwnd.ToInt64(), title, w, h, rect.Left, rect.Top));
                return true;
            }, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return windows;
    }

    public static string CaptureWindow(long hwnd, int maxW, int quality)
    {
        var hWnd = new IntPtr(hwnd);
        if (!IsWindowVisible(hWnd))
            throw new ArgumentException($"Window {hwnd} is not visible or does not exist");

        GetWindowRect(hWnd, out var rect);
        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0)
            throw new ArgumentException($"Window {hwnd} has invalid dimensions: {w}x{h}");

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

            var hdcDest = g.GetHdc();
            try
            {
                const uint PW_RENDERFULLCONTENT = 0x00000002;
                bool success = PrintWindow(hWnd, hdcDest, PW_RENDERFULLCONTENT);

                if (!success)
                {
                    success = PrintWindow(hWnd, hdcDest, 0);
                }

                if (!success)
                {
                    throw new InvalidOperationException($"PrintWindow failed for window {hwnd}");
                }
            }
            finally
            {
                g.ReleaseHdc(hdcDest);
            }
        }
        return ToJpegBase64(bmp, maxW, quality);
    }

    private string CaptureWindowInternal(long hwnd, int maxW, int quality, string streamId)
    {
        var hWnd = new IntPtr(hwnd);
        if (!IsWindowVisible(hWnd))
            throw new ArgumentException($"Window {hwnd} is not visible or does not exist");

        GetWindowRect(hWnd, out var rect);
        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0)
            throw new ArgumentException($"Window {hwnd} has invalid dimensions: {w}x{h}");

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

            var hdcDest = g.GetHdc();
            try
            {
                const uint PW_RENDERFULLCONTENT = 0x00000002;
                bool success = PrintWindow(hWnd, hdcDest, PW_RENDERFULLCONTENT);

                if (!success)
                {
                    success = PrintWindow(hWnd, hdcDest, 0);
                }

                if (!success)
                {
                    throw new InvalidOperationException($"PrintWindow failed for window {hwnd}");
                }
            }
            finally
            {
                g.ReleaseHdc(hdcDest);
            }
        }

        // Apply AI-friendly overlays if enabled
        if (EnableOverlay)
        {
            var startTime = _streamStartTimes.GetValueOrDefault(streamId, DateTime.UtcNow);
            var elapsed = DateTime.UtcNow - startTime;
            ImageOverlayService.OverlayTimestamp(bmp, elapsed);

            var eventTag = GetEventTag?.Invoke(streamId);
            ImageOverlayService.OverlayEventTag(bmp, eventTag);
        }

        return ToJpegBase64(bmp, maxW, quality);
    }

    public static string CaptureRegion(int x, int y, int w, int h, int maxW, int quality)
    {
        if (w <= 0 || h <= 0)
            throw new ArgumentException($"Invalid region dimensions: {w}x{h}");

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        }
        return ToJpegBase64(bmp, maxW, quality);
    }

    private const int Stream2Width = 640;
    private const int Stream2Height = 360;
    private const int Stream2Fps = 15;

    public string StartRegionStream2(int x, int y, int w, int h, int quality)
    {
        Console.Error.WriteLine($"[Stream2] StartRegionStream2 called: x={x}, y={y}, w={w}, h={h}, quality={quality}");
        if (w <= 0 || h <= 0)
            throw new ArgumentException($"Invalid region dimensions: {w}x{h}");

        var id = Guid.NewGuid().ToString();
        var sess = new StreamSession
        {
            Id = id,
            TargetType = "region2",
            Interval = 1000 / Stream2Fps,
            Quality = quality,
            MaxW = Stream2Width,
            RegionX = x,
            RegionY = y,
            RegionW = w,
            RegionH = h
        };
        _sessions[id] = sess;
        _streamStartTimes[id] = DateTime.UtcNow;
        _ = StreamLoop2(sess);
        return id;
    }

    private async Task StreamLoop2(StreamSession s)
    {
        try
        {
            while (!s.Cts.Token.IsCancellationRequested)
            {
                var start = DateTime.UtcNow;
                try
                {
                    var img = CaptureRegionFixed(s.RegionX, s.RegionY, s.RegionW, s.RegionH, s.Quality, s.Id);
                    await s.Channel.Writer.WriteAsync(img, s.Cts.Token).ConfigureAwait(false);

                    s.LatestFrame = img;
                    s.LastCaptureTime = DateTime.UtcNow;

                    if (OnFrameCaptured != null)
                    {
                        try
                        {
                            await OnFrameCaptured(s.Id, img).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[Stream2 {s.Id}] Failed to send notification: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.Error.WriteLine($"[Stream2 {s.Id}] Capture error: {ex.Message}");
                    await Task.Delay(1000, s.Cts.Token).ConfigureAwait(false);
                    continue;
                }

                var elapsed = (int)(DateTime.UtcNow - start).TotalMilliseconds;
                var delay = s.Interval - elapsed;
                if (delay > 0)
                {
                    try
                    {
                        await Task.Delay(delay, s.Cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Stream2 {s.Id}] Fatal error: {ex.Message}");
        }
        finally
        {
            s.Channel.Writer.Complete();
            Console.Error.WriteLine($"[Stream2 {s.Id}] Completed");
        }
    }

    private string CaptureRegionFixed(int x, int y, int w, int h, int quality, string streamId)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        }

        // Apply AI-friendly overlays if enabled
        if (EnableOverlay)
        {
            var startTime = _streamStartTimes.GetValueOrDefault(streamId, DateTime.UtcNow);
            var elapsed = DateTime.UtcNow - startTime;
            ImageOverlayService.OverlayTimestamp(bmp, elapsed);

            var eventTag = GetEventTag?.Invoke(streamId);
            ImageOverlayService.OverlayEventTag(bmp, eventTag);
        }

        return ToJpegBase64Fixed(bmp, quality);
    }

    private static string ToJpegBase64Fixed(Bitmap src, int q)
    {
        using var ms = new MemoryStream();
        using var target = new Bitmap(src, new Size(Stream2Width, Stream2Height));
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        var p = new EncoderParameters(1);
        p.Param[0] = new EncoderParameter(Encoder.Quality, q);
        target.Save(ms, codec, p);
        return Convert.ToBase64String(ms.ToArray());
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] struct MONITORINFOEX { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
}
