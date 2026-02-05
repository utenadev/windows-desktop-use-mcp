using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

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
}
