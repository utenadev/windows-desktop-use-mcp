using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Channels;

public class ScreenCaptureService {
    private readonly uint _defaultMon;
    private readonly Dictionary<string, StreamSession> _sessions = new();
    private List<MonitorInfo> _monitors = new();

    public ScreenCaptureService(uint defaultMon) => _defaultMon = defaultMon;

    public void InitializeMonitors() {
        _monitors = EnumMonitors();
        Console.Error.WriteLine($"[Capture] Found {_monitors.Count} monitors");
    }

    public List<MonitorInfo> GetMonitors() => _monitors;

    public string CaptureSingle(uint idx, int maxW, int quality) {
        if (idx >= _monitors.Count)
            throw new ArgumentOutOfRangeException(nameof(idx), $"Monitor index {idx} is out of range. Available: 0-{_monitors.Count - 1}");
        var mon = _monitors[(int)idx];
        using var bmp = new Bitmap(mon.W, mon.H);
        using (var g = Graphics.FromImage(bmp)) {
            g.CopyFromScreen(mon.X, mon.Y, 0, 0, new Size(mon.W, mon.H));
        }
        return ToJpegBase64(bmp, maxW, quality);
    }

    public string StartStream(uint idx, int interval, int quality, int maxW) {
        var id = Guid.NewGuid().ToString();
        var sess = new StreamSession { Id = id, MonIdx = idx, Interval = interval, Quality = quality, MaxW = maxW };
        _sessions[id] = sess;
        _ = StreamLoop(sess);
        return id;
    }

    public void StopStream(string id) {
        if (_sessions.Remove(id, out var s)) s.Cts.Cancel();
    }

    public bool TryGetSession(string id, out StreamSession? s) => _sessions.TryGetValue(id, out s);

    public void StopAllStreams() {
        Console.Error.WriteLine($"[Capture] Stopping all {_sessions.Count} streams...");
        foreach (var session in _sessions.Values) {
            session.Cts.Cancel();
        }
        _sessions.Clear();
    }

    private async Task StreamLoop(StreamSession s) {
        try {
            while (!s.Cts.Token.IsCancellationRequested) {
                var start = DateTime.UtcNow;
                try {
                    var img = CaptureSingle(s.MonIdx, s.MaxW, s.Quality);
                    await s.Channel.Writer.WriteAsync(img, s.Cts.Token);
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    Console.Error.WriteLine($"[Stream {s.Id}] Capture error: {ex.Message}");
                    await Task.Delay(1000, s.Cts.Token); // Backoff on error
                    continue;
                }
                
                var elapsed = (int)(DateTime.UtcNow - start).TotalMilliseconds;
                var delay = s.Interval - elapsed;
                if (delay > 0) {
                    try {
                        await Task.Delay(delay, s.Cts.Token);
                    } catch (OperationCanceledException) {
                        break;
                    }
                }
            }
        } catch (OperationCanceledException) { 
            // Normal cancellation, no action needed
        } catch (Exception ex) {
            Console.Error.WriteLine($"[Stream {s.Id}] Fatal error: {ex.Message}");
        } finally { 
            s.Channel.Writer.Complete();
            Console.Error.WriteLine($"[Stream {s.Id}] Completed");
        }
    }

    private string ToJpegBase64(Bitmap src, int maxW, int q) {
        using var ms = new MemoryStream();
        var target = src.Width > maxW ? new Bitmap(src, new Size(maxW, (int)(src.Height * ((double)maxW / src.Width)))) : src;
        try {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(Encoder.Quality, q);
            target.Save(ms, codec, p);
            return Convert.ToBase64String(ms.ToArray());
        } finally { if (target != src) target.Dispose(); }
    }

    private static List<MonitorInfo> EnumMonitors() {
        var list = new List<MonitorInfo>();
        var handle = GCHandle.Alloc(list);
        try {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumMonCallback, GCHandle.ToIntPtr(handle));
        } finally {
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
    [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);
    const int SRCCOPY = 0x00CC0020;
    
    delegate bool EnumMonDelegate(IntPtr h, IntPtr hdc, ref RECT rc, IntPtr d);
    delegate bool EnumWindowsDelegate(IntPtr hWnd, IntPtr lParam);
    
    static bool EnumMonCallback(IntPtr h, IntPtr hdc, ref RECT rc, IntPtr d) {
        var list = (List<MonitorInfo>)GCHandle.FromIntPtr(d).Target!;
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (GetMonitorInfo(h, ref mi)) {
            var w = mi.rcMonitor.Right - mi.rcMonitor.Left;
            var hgt = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
            list.Add(new MonitorInfo((uint)list.Count, mi.szDevice, w, hgt, mi.rcMonitor.Left, mi.rcMonitor.Top));
        }
        return true;
    }
    
    public List<WindowInfo> GetWindows() {
        var windows = new List<WindowInfo>();
        var handle = GCHandle.Alloc(windows);
        try {
            EnumWindows((hwnd, param) => {
                if (!IsWindowVisible(hwnd)) return true;
                var sb = new System.Text.StringBuilder(256);
                GetWindowText(hwnd, sb, 256);
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
        } finally {
            handle.Free();
        }
        return windows;
    }
    
    public string CaptureWindow(long hwnd, int maxW, int quality) {
        var hWnd = new IntPtr(hwnd);
        if (!IsWindowVisible(hWnd)) 
            throw new ArgumentException($"Window {hwnd} is not visible or does not exist");
            
        GetWindowRect(hWnd, out var rect);
        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0)
            throw new ArgumentException($"Window {hwnd} has invalid dimensions: {w}x{h}");
            
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) {
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            
            var hdcDest = g.GetHdc();
            try {
                // Try PW_RENDERFULLCONTENT flag (0x00000002) for GPU app capture
                // This flag was added in Windows 8.1 to capture full window content including GPU-rendered parts
                const uint PW_RENDERFULLCONTENT = 0x00000002;
                bool success = PrintWindow(hWnd, hdcDest, PW_RENDERFULLCONTENT);
                
                if (!success) {
                    // Fallback to standard PrintWindow if PW_RENDERFULLCONTENT fails
                    success = PrintWindow(hWnd, hdcDest, 0);
                }
                
                if (!success) {
                    throw new InvalidOperationException($"PrintWindow failed for window {hwnd}");
                }
            } finally {
                g.ReleaseHdc(hdcDest);
            }
        }
        return ToJpegBase64(bmp, maxW, quality);
    }
    
    public string CaptureRegion(int x, int y, int w, int h, int maxW, int quality) {
        if (w <= 0 || h <= 0)
            throw new ArgumentException($"Invalid region dimensions: {w}x{h}");
            
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) {
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
        }
        return ToJpegBase64(bmp, maxW, quality);
    }
    
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] struct MONITORINFOEX { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
}

public record WindowInfo(long Hwnd, string Title, int W, int H, int X, int Y);

public record MonitorInfo(uint Idx, string Name, int W, int H, int X, int Y);
public class StreamSession {
    public string Id = "";
    public uint MonIdx;
    public int Interval;
    public int Quality;
    public int MaxW;
    public CancellationTokenSource Cts = new();
    public Channel<string> Channel { get; }
    
    public StreamSession() {
        Channel = System.Threading.Channels.Channel.CreateUnbounded<string>();
    }
}
