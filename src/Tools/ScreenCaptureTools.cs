using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// Data models for unified tools
public record CaptureTarget(
    string Type,
    string Id,
    string Name,
    int Width,
    int Height,
    int X,
    int Y
);

public record CaptureTargets(
    List<CaptureTarget> Monitors,
    List<CaptureTarget> Windows,
    int TotalCount
);

public record CaptureResult(
    string ImageData,
    string MimeType,
    int Width,
    int Height,
    string TargetType,
    string TargetId
);

public record WatchSession(
    string SessionId,
    string TargetType,
    string TargetId,
    int IntervalMs,
    string Status
);

[McpServerToolType]
public static class ScreenCaptureTools
{
    private static ScreenCaptureService? _capture;

    public static void SetCaptureService(ScreenCaptureService capture) => _capture = capture;

    [McpServerTool, Description("List all available monitors/displays with their index, name, resolution, and position")]
    public static List<MonitorInfo> ListMonitors()
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        return _capture.GetMonitors();
    }

    [McpServerTool, Description("List all visible windows with their handles, titles, and dimensions")]
    public static List<WindowInfo> ListWindows()
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        return _capture.GetWindows();
    }

    [McpServerTool, Description("Capture a screenshot of specified monitor or window (like taking a photo with your eyes). Returns the captured image as base64 JPEG.")]
    public static ImageContentBlock See(
        [Description("Target type: 'monitor' or 'window'")] string targetType = "monitor",
        [Description("Monitor index (0=primary, 1=secondary, etc.) - used when targetType='monitor'")] uint monitor = 0,
        [Description("Window handle (HWND) - used when targetType='window'")] long? hwnd = null,
        [Description("JPEG quality (1-100, higher=better quality but larger size)")] int quality = 80,
        [Description("Maximum width in pixels (image will be resized if larger)")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        
        var imageData = targetType == "window" && hwnd.HasValue
            ? _capture.CaptureWindow(hwnd.Value, maxWidth, quality)
            : _capture.CaptureSingle(monitor, maxWidth, quality);

        var base64Data = imageData.Contains(";base64,") ? imageData.Split(';')[1].Split(',')[1] : imageData;
        
        return new ImageContentBlock
        {
            MimeType = "image/jpeg",
            Data = base64Data
        };
    }

    [McpServerTool, Description("Capture a screenshot of a specific window by its handle (HWND). Returns the captured image as base64 JPEG.")]
    public static ImageContentBlock CaptureWindow(
        [Description("Window handle (HWND) to capture")] long hwnd,
        [Description("JPEG quality (1-100)")] int quality = 80,
        [Description("Maximum width in pixels")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        
        var imageData = _capture.CaptureWindow(hwnd, maxWidth, quality);
        var base64Data = imageData.Contains(";base64,") ? imageData.Split(';')[1].Split(',')[1] : imageData;
        
        return new ImageContentBlock
        {
            MimeType = "image/jpeg",
            Data = base64Data
        };
    }

    [McpServerTool, Description("Capture a screenshot of a specific screen region. Returns the captured image as base64 JPEG.")]
    public static ImageContentBlock CaptureRegion(
        [Description("X coordinate of the region")] int x,
        [Description("Y coordinate of the region")] int y,
        [Description("Width of the region")] int w,
        [Description("Height of the region")] int h,
        [Description("JPEG quality (1-100)")] int quality = 80,
        [Description("Maximum width in pixels")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        
        var imageData = _capture.CaptureRegion(x, y, w, h, maxWidth, quality);
        var base64Data = imageData.Contains(";base64,") ? imageData.Split(';')[1].Split(',')[1] : imageData;
        
        return new ImageContentBlock
        {
            MimeType = "image/jpeg",
            Data = base64Data
        };
    }

    [McpServerTool, Description("Start a continuous screen capture stream for a monitor or window (like watching a live video). Returns a session ID.")]
    public static string StartWatching(
        McpServer server,
        [Description("Target type: 'monitor' or 'window'")] string targetType = "monitor",
        [Description("Monitor index to watch - used when targetType='monitor'")] uint monitor = 0,
        [Description("Window handle (HWND) to watch - used when targetType='window'")] long? hwnd = null,
        [Description("Capture interval in milliseconds (1000=1 second)")] int intervalMs = 1000,
        [Description("JPEG quality (1-100)")] int quality = 80,
        [Description("Maximum width in pixels")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        
        // Setup notification callback for frame streaming
        _capture.OnFrameCaptured = async (sessionId, imageData) => {
            var notificationData = new Dictionary<string, object?> {
                ["level"] = "info",
                ["data"] = new Dictionary<string, string> {
                    ["sessionId"] = sessionId,
                    ["image"] = imageData,
                    ["type"] = "frame"
                }
            };
            await server.SendNotificationAsync("notifications/message", notificationData);
        };
        
        if (targetType == "window" && hwnd.HasValue)
        {
            return _capture.StartWindowStream(hwnd.Value, intervalMs, quality, maxWidth);
        }
        return _capture.StartStream(monitor, intervalMs, quality, maxWidth);
    }

    [McpServerTool, Description("Stop a running screen capture stream by session ID")]
    public static string StopWatching(
        [Description("The session ID returned by start_watching")] string sessionId)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        _capture.StopStream(sessionId);
        return "Stopped watching";
    }

    [McpServerTool, Description("Get the latest captured frame from a stream session. Returns image data with hash for change detection.")]
    public static object GetLatestFrame(
        [Description("The session ID returned by start_watching")] string sessionId)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        
        if (!_capture.TryGetSession(sessionId, out var session) || session == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }
        
        var latestFrame = session.LatestFrame;
        var hash = session.LastFrameHash;
        var captureTime = session.LastCaptureTime;
        
        if (string.IsNullOrEmpty(latestFrame))
        {
            return new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId,
                ["hasFrame"] = false,
                ["message"] = "No frame captured yet"
            };
        }
        
        return new Dictionary<string, object?>
        {
            ["sessionId"] = sessionId,
            ["hasFrame"] = true,
            ["image"] = latestFrame,
            ["hash"] = hash,
            ["captureTime"] = captureTime.ToString("O"),
            ["targetType"] = session.TargetType,
            ["monIdx"] = session.MonIdx,
            ["hwnd"] = session.Hwnd
        };
    }

    // ============ NEW UNIFIED TOOLS ============

    [McpServerTool, Description("List all available capture targets (monitors and windows) with unified interface")]
    public static CaptureTargets ListAll(
        [Description("Filter: 'all', 'monitors', 'windows'")] string filter = "all")
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        
        var monitors = new List<CaptureTarget>();
        var windows = new List<CaptureTarget>();
        
        if (filter == "all" || filter == "monitors")
        {
            var monitorList = _capture.GetMonitors();
            monitors = monitorList.Select(m => new CaptureTarget(
                "monitor",
                m.Idx.ToString(),
                m.Name,
                m.W,
                m.H,
                m.X,
                m.Y
            )).ToList();
        }
        
        if (filter == "all" || filter == "windows")
        {
            var windowList = _capture.GetWindows();
            windows = windowList.Select(w => new CaptureTarget(
                "window",
                w.Hwnd.ToString(),
                w.Title,
                w.W,
                w.H,
                w.X,
                w.Y
            )).ToList();
        }
        
        return new CaptureTargets(monitors, windows, monitors.Count + windows.Count);
    }

    [McpServerTool, Description("Capture screen, window, or region as image")]
    public static CaptureResult Capture(
        [Description("Target type: 'monitor', 'window', 'region', 'primary' (default monitor)")] string target = "primary",
        [Description("Target identifier: monitor index, hwnd, or 'primary' (default)")] string? targetId = null,
        [Description("X coordinate for region capture")] int? x = null,
        [Description("Y coordinate for region capture")] int? y = null,
        [Description("Width for region capture")] int? w = null,
        [Description("Height for region capture")] int? h = null,
        [Description("JPEG quality (1-100)")] int quality = 80,
        [Description("Maximum width in pixels")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        
        string imageData;
        string actualTargetType = target;
        string actualTargetId = targetId ?? "0";
        int capturedWidth = 0;
        int capturedHeight = 0;
        
        switch (target.ToLower())
        {
            case "primary":
                imageData = _capture.CaptureSingle(0, maxWidth, quality);
                actualTargetType = "monitor";
                actualTargetId = "0";
                break;
                
            case "monitor":
                if (!uint.TryParse(targetId ?? "0", out var monitorIdx))
                    throw new ArgumentException("Invalid monitor index");
                imageData = _capture.CaptureSingle(monitorIdx, maxWidth, quality);
                capturedWidth = maxWidth;
                break;
                
            case "window":
                if (!long.TryParse(targetId, out var hwnd))
                    throw new ArgumentException("Invalid window handle (hwnd)");
                imageData = _capture.CaptureWindow(hwnd, maxWidth, quality);
                actualTargetType = "window";
                actualTargetId = hwnd.ToString();
                break;
                
            case "region":
                if (!x.HasValue || !y.HasValue || !w.HasValue || !h.HasValue)
                    throw new ArgumentException("Region capture requires x, y, w, h parameters");
                imageData = _capture.CaptureRegion(x.Value, y.Value, w.Value, h.Value, maxWidth, quality);
                actualTargetType = "region";
                actualTargetId = $"{x.Value},{y.Value},{w.Value},{h.Value}";
                capturedWidth = w.Value;
                capturedHeight = h.Value;
                break;
                
            default:
                throw new ArgumentException($"Unknown target type: {target}");
        }
        
        var base64Data = imageData.Contains(";base64,") ? imageData.Split(';')[1].Split(',')[1] : imageData;
        
        return new CaptureResult(
            base64Data,
            "image/jpeg",
            capturedWidth,
            capturedHeight,
            actualTargetType,
            actualTargetId
        );
    }

    [McpServerTool, Description("Start watching/streaming a target (monitor, window, or region)")]
    public static WatchSession Watch(
        McpServer server,
        [Description("Target type: 'monitor', 'window', 'region'")] string target = "monitor",
        [Description("Target identifier: monitor index, hwnd, or region coordinates (x,y,w,h)")] string? targetId = null,
        [Description("Capture interval in milliseconds (minimum 100ms)")] int intervalMs = 1000,
        [Description("JPEG quality (1-100)")] int quality = 80,
        [Description("Maximum width in pixels")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        if (intervalMs < 100)
            throw new ArgumentException("Interval must be at least 100ms");
        
        // Setup notification callback for frame streaming
        _capture.OnFrameCaptured = async (sessionId, imageData) => {
            var notificationData = new Dictionary<string, object?> {
                ["level"] = "info",
                ["data"] = new Dictionary<string, string> {
                    ["sessionId"] = sessionId,
                    ["image"] = imageData,
                    ["type"] = "frame"
                }
            };
            await server.SendNotificationAsync("notifications/message", notificationData);
        };
        
        string sessionId;
        string actualTargetId = targetId ?? "0";
        
        switch (target.ToLower())
        {
            case "monitor":
                if (!uint.TryParse(targetId ?? "0", out var monitorIdx))
                    throw new ArgumentException("Invalid monitor index");
                sessionId = _capture.StartStream(monitorIdx, intervalMs, quality, maxWidth);
                actualTargetId = monitorIdx.ToString();
                break;
                
            case "window":
                if (!long.TryParse(targetId, out var hwnd))
                    throw new ArgumentException("Invalid window handle (hwnd)");
                sessionId = _capture.StartWindowStream(hwnd, intervalMs, quality, maxWidth);
                actualTargetId = hwnd.ToString();
                break;
                
            default:
                throw new ArgumentException($"Target type '{target}' not yet supported for watching");
        }
        
        return new WatchSession(sessionId, target, actualTargetId, intervalMs, "active");
    }

    [McpServerTool, Description("Stop watching a capture session")]
    public static string StopWatch(
        [Description("The session ID returned by watch")] string sessionId)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        _capture.StopStream(sessionId);
        return $"Stopped watching session {sessionId}";
    }
}
