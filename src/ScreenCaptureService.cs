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
        Console.WriteLine($"[Capture] Found {_monitors.Count} monitors");
    }

    public List<MonitorInfo> GetMonitors() => _monitors;

    public string CaptureSingle(uint idx, int maxW, int quality) {
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

    public bool TryGetSession(string id, out StreamSession s) => _sessions.TryGetValue(id, out s!);

    public void StopAllStreams() {
        Console.WriteLine($"[Capture] Stopping all {_sessions.Count} streams...");
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
                    Console.WriteLine($"[Stream {s.Id}] Capture error: {ex.Message}");
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
            Console.WriteLine($"[Stream {s.Id}] Fatal error: {ex.Message}");
        } finally { 
            s.Channel.Writer.Complete();
            Console.WriteLine($"[Stream {s.Id}] Completed");
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
    delegate bool EnumMonDelegate(IntPtr h, IntPtr hdc, ref RECT rc, IntPtr d);
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
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] struct MONITORINFOEX { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
}

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
