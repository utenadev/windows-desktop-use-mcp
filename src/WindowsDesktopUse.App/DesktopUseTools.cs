using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WindowsDesktopUse.Audio;
using WindowsDesktopUse.Core;
using WindowsDesktopUse.Input;
using WindowsDesktopUse.Screen;
using WindowsDesktopUse.Transcription;

namespace WindowsDesktopUse.App;

[McpServerToolType]
public class DesktopUseTools
{
    private static ScreenCaptureService? _capture;
    private static AudioCaptureService? _audioCapture;
    private static WhisperTranscriptionService? _whisperService;
    private static VideoCaptureService? _videoCapture;
    private static WindowsDesktopUse.Screen.HybridCaptureService? _hybridCapture;
    private static AccessibilityService? _accessibilityService;

    public static void SetCaptureService(ScreenCaptureService capture) => _capture = capture;
    public static void SetAudioCaptureService(AudioCaptureService audioCapture) => _audioCapture = audioCapture;
    public static void SetWhisperService(WhisperTranscriptionService whisperService) => _whisperService = whisperService;
    public static void SetVideoCaptureService(VideoCaptureService videoCapture) => _videoCapture = videoCapture;
    public static void SetHybridCaptureService(WindowsDesktopUse.Screen.HybridCaptureService hybridCapture) => _hybridCapture = hybridCapture;
    public static void SetAccessibilityService(AccessibilityService accessibilityService) => _accessibilityService = accessibilityService;

    // Enum for capture target types
    public enum CaptureTargetType
    {
        Monitor,
        Window,
        Region,
        Primary
    }

    // Enum for audio source types
    public enum AudioSourceType
    {
        Microphone,
        System,
        File,
        AudioSession
    }

    // Enum for mouse button types
    public enum MouseButtonName
    {
        Left,
        Right,
        Middle
    }

    // Enum for key action types
    public enum KeyActionType
    {
        Press,
        Release,
        Click
    }

    // ============ SCREEN CAPTURE TOOLS ============

    // [McpServerTool removed for v2.0 - use unified tools]
    public static IReadOnlyList<MonitorInfo> ListMonitors()
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        return _capture.GetMonitors();
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static IReadOnlyList<WindowInfo> ListWindows()
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        return ScreenCaptureService.GetWindows();
    }

    /// <summary>
    /// Captures a screenshot of specified monitor or window.
    /// Accepts hwnd as string (e.g. "655936") for robustness against JSON number/string ambiguity.
    /// </summary>
    // [McpServerTool removed for v2.0 - use unified tools]
    public static ImageContentBlock See(
        [Description("Target type: 'monitor' or 'window'")] string targetType = "monitor",
        [Description("Monitor index (0=primary) - used when targetType='monitor'")] uint monitor = 0,
        [Description("Window handle (HWND) as string or number - used when targetType='window'")] string hwndStr = null,
        [Description("JPEG quality (1-100)")] int quality = 80,
        [Description("Maximum width in pixels")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");

        long? hwnd = null;
        if (!string.IsNullOrWhiteSpace(hwndStr))
        {
            if (!long.TryParse(hwndStr, out var h))
                throw new ArgumentException($"Invalid hwnd value: '{hwndStr}'. Expected integer string.");
            hwnd = h;
        }

        var imageData = targetType == "window" && hwnd.HasValue
            ? ScreenCaptureService.CaptureWindow(hwnd.Value, maxWidth, quality)
            : _capture.CaptureSingle(monitor, maxWidth, quality);

        var base64Data = imageData.Contains(";base64,", StringComparison.Ordinal) ? imageData.Split(';')[1].Split(',')[1] : imageData;

        return new ImageContentBlock
        {
            MimeType = "image/jpeg",
            Data = base64Data
        };
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static ImageContentBlock CaptureWindow(
        [Description("Window handle (HWND) to capture")] long hwnd,
        [Description("JPEG quality (1-100)")] int quality = 80,
        [Description("Maximum width in pixels")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");

        var imageData = ScreenCaptureService.CaptureWindow(hwnd, maxWidth, quality);
        var base64Data = imageData.Contains(";base64,", StringComparison.Ordinal) ? imageData.Split(';')[1].Split(',')[1] : imageData;

        return new ImageContentBlock
        {
            MimeType = "image/jpeg",
            Data = base64Data
        };
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static ImageContentBlock CaptureRegion(
        [Description("X coordinate")] int x,
        [Description("Y coordinate")] int y,
        [Description("Width")] int w,
        [Description("Height")] int h,
        [Description("JPEG quality")] int quality = 80,
        [Description("Maximum width")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");

        var imageData = ScreenCaptureService.CaptureRegion(x, y, w, h, maxWidth, quality);
        var base64Data = imageData.Contains(";base64,", StringComparison.Ordinal) ? imageData.Split(';')[1].Split(',')[1] : imageData;

        return new ImageContentBlock
        {
            MimeType = "image/jpeg",
            Data = base64Data
        };
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static WatchSession CameraCaptureStream(
        McpServer server,
        [Description("X coordinate of region")] int x,
        [Description("Y coordinate of region")] int y,
        [Description("Width of region")] int w,
        [Description("Height of region")] int h,
        [Description("JPEG quality (1-100)")] int quality = 80)
    {
        try
        {
            Console.Error.WriteLine($"[CameraCaptureStream] Called with x={x}, y={y}, w={w}, h={h}, quality={quality}");
            Console.Error.WriteLine($"[CameraCaptureStream] _capture is null: {_capture == null}");
            
            if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
            if (server == null) throw new ArgumentNullException(nameof(server), "McpServer cannot be null");
            if (quality < 1 || quality > 100) throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 1 and 100");

            Console.Error.WriteLine($"[CameraCaptureStream] Starting OnFrameCaptured callback setup");
            
            _capture.OnFrameCaptured = async (sessionId, imageData) =>
            {
                var notificationData = new Dictionary<string, object?>
                {
                    ["level"] = "info",
                    ["data"] = new Dictionary<string, string>
                    {
                        ["sessionId"] = sessionId,
                        ["image"] = imageData,
                        ["type"] = "frame"
                    }
                };
                await server.SendNotificationAsync("notifications/message", notificationData).ConfigureAwait(false);
            };

            Console.Error.WriteLine($"[CameraCaptureStream] Calling StartRegionStream2");
            
            var sessionId = _capture.StartRegionStream2(x, y, w, h, quality);
            var targetId = $"{x},{y},{w},{h}";

            Console.Error.WriteLine($"[CameraCaptureStream] Created session {sessionId}");

            return new WatchSession(sessionId, "region2", targetId, 1000 / 15, "active");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CameraCaptureStream] ERROR: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine($"[CameraCaptureStream] StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// Starts a continuous screen capture stream.
    /// Accepts hwnd as string for compatibility with JSON clients that serialize numbers as strings.
    /// </summary>
    // [McpServerTool removed for v2.0 - use unified tools]
    public static string StartWatching(
        McpServer server,
        [Description("Target type: 'monitor' or 'window'")] string targetType = "monitor",
        [Description("Monitor index")] uint monitor = 0,
        [Description("Window handle as string or number")] string hwndStr = null,
        [Description("Capture interval in milliseconds")] int intervalMs = 1000,
        [Description("JPEG quality")] int quality = 80,
        [Description("Maximum width")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");

        long? hwnd = null;
        if (!string.IsNullOrWhiteSpace(hwndStr))
        {
            if (!long.TryParse(hwndStr, out var h))
                throw new ArgumentException($"Invalid hwnd value: '{hwndStr}'. Expected integer string.");
            hwnd = h;
        }

        _capture.OnFrameCaptured = async (sessionId, imageData) =>
        {
            var notificationData = new Dictionary<string, object?>
            {
                ["level"] = "info",
                ["data"] = new Dictionary<string, string>
                {
                    ["sessionId"] = sessionId,
                    ["image"] = imageData,
                    ["type"] = "frame"
                }
            };
            await server.SendNotificationAsync("notifications/message", notificationData).ConfigureAwait(false);
        };

        if (targetType == "window" && hwnd.HasValue)
        {
            return _capture.StartWindowStream(hwnd.Value, intervalMs, quality, maxWidth);
        }
        return _capture.StartStream(monitor, intervalMs, quality, maxWidth);
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static string StopWatching(
        [Description("The session ID returned by start_watching")] string sessionId)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        _capture.StopStream(sessionId);
        return "Stopped watching";
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static object GetLatestFrame(
        [Description("The session ID")] string sessionId)
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

    // ============ UNIFIED TOOLS ============

    // [McpServerTool removed for v2.0 - use unified tools]
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
                m.Idx.ToString(CultureInfo.InvariantCulture),
                m.Name,
                m.W,
                m.H,
                m.X,
                m.Y
            )).ToList();
        }

        if (filter == "all" || filter == "windows")
        {
            var windowList = ScreenCaptureService.GetWindows();
            windows = windowList.Select(w => new CaptureTarget(
                "window",
                w.Hwnd.ToString(CultureInfo.InvariantCulture),
                w.Title,
                w.W,
                w.H,
                w.X,
                w.Y
            )).ToList();
        }

        return new CaptureTargets(monitors, windows, monitors.Count + windows.Count);
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static CaptureResult Capture(
        [Description("Target type: 'monitor', 'window', 'region', 'primary'")] CaptureTargetType target = CaptureTargetType.Primary,
        [Description("Target identifier")] string? targetId = null,
        [Description("X coordinate for region")] int? x = null,
        [Description("Y coordinate for region")] int? y = null,
        [Description("Width for region")] int? w = null,
        [Description("Height for region")] int? h = null,
        [Description("JPEG quality")] int quality = 80,
        [Description("Maximum width")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        if (quality < 1 || quality > 100) throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 1 and 100");
        if (maxWidth < 1) throw new ArgumentOutOfRangeException(nameof(maxWidth), "MaxWidth must be greater than 0");

        string imageData;
        string actualTargetType = target.ToString();
        string actualTargetId = targetId ?? "0";
        int capturedWidth = 0;
        int capturedHeight = 0;

        switch (target)
        {
            case CaptureTargetType.Primary:
                imageData = _capture.CaptureSingle(0, maxWidth, quality);
                actualTargetType = "monitor";
                actualTargetId = "0";
                break;

            case CaptureTargetType.Monitor:
                if (!uint.TryParse(targetId ?? "0", out var monitorIdx))
                    throw new ArgumentException("Invalid monitor index");
                imageData = _capture.CaptureSingle(monitorIdx, maxWidth, quality);
                capturedWidth = maxWidth;
                break;

            case CaptureTargetType.Window:
                if (targetId == null) throw new ArgumentNullException(nameof(targetId), "Target ID is required for window capture");
                if (!long.TryParse(targetId, out var hwnd))
                    throw new ArgumentException("Invalid window handle");
                imageData = ScreenCaptureService.CaptureWindow(hwnd, maxWidth, quality);
                actualTargetType = "window";
                actualTargetId = hwnd.ToString(CultureInfo.InvariantCulture);
                break;

            case CaptureTargetType.Region:
                if (!x.HasValue || !y.HasValue || !w.HasValue || !h.HasValue)
                    throw new ArgumentException("Region capture requires x, y, w, h");
                imageData = ScreenCaptureService.CaptureRegion(x.Value, y.Value, w.Value, h.Value, maxWidth, quality);
                actualTargetType = "region";
                actualTargetId = $"{x.Value},{y.Value},{w.Value},{h.Value}";
                capturedWidth = w.Value;
                capturedHeight = h.Value;
                break;

            default:
                throw new ArgumentException($"Unknown target type: {target}. Valid values are 'primary', 'monitor', 'window', 'region'");
        }

        var base64Data = imageData.Contains(";base64,", StringComparison.Ordinal) ? imageData.Split(';')[1].Split(',')[1] : imageData;

        return new CaptureResult(
            base64Data,
            "image/jpeg",
            capturedWidth,
            capturedHeight,
            actualTargetType,
            actualTargetId
        );
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static WatchSession Watch(
        McpServer server,
        [Description("Target type: 'monitor', 'window'")] CaptureTargetType target = CaptureTargetType.Monitor,
        [Description("Target identifier")] string? targetId = null,
        [Description("Capture interval in milliseconds")] int intervalMs = 1000,
        [Description("JPEG quality")] int quality = 80,
        [Description("Maximum width")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        if (server == null) throw new ArgumentNullException(nameof(server), "McpServer cannot be null");
        if (intervalMs < 100)
            throw new ArgumentOutOfRangeException(nameof(intervalMs), "Interval must be at least 100ms");
        if (quality < 1 || quality > 100) throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 1 and 100");
        if (maxWidth < 1) throw new ArgumentOutOfRangeException(nameof(maxWidth), "MaxWidth must be greater than 0");

        _capture.OnFrameCaptured = async (sessionId, imageData) =>
        {
            var notificationData = new Dictionary<string, object?>
            {
                ["level"] = "info",
                ["data"] = new Dictionary<string, string>
                {
                    ["sessionId"] = sessionId,
                    ["image"] = imageData,
                    ["type"] = "frame"
                }
            };
            await server.SendNotificationAsync("notifications/message", notificationData).ConfigureAwait(false);
        };

        string sessionId;
        string actualTargetId = targetId ?? "0";

        switch (target)
        {
            case CaptureTargetType.Monitor:
                if (targetId == null) throw new ArgumentNullException(nameof(targetId), "Target ID is required for monitor target");
                if (!uint.TryParse(targetId, out var monitorIdx))
                    throw new ArgumentException("Invalid monitor index");
                sessionId = _capture.StartStream(monitorIdx, intervalMs, quality, maxWidth);
                actualTargetId = monitorIdx.ToString(CultureInfo.InvariantCulture);
                break;

            case CaptureTargetType.Window:
                if (targetId == null) throw new ArgumentNullException(nameof(targetId), "Target ID is required for window target");
                if (!long.TryParse(targetId, out var hwnd))
                    throw new ArgumentException("Invalid window handle");
                sessionId = _capture.StartWindowStream(hwnd, intervalMs, quality, maxWidth);
                actualTargetId = hwnd.ToString(CultureInfo.InvariantCulture);
                break;

            default:
                throw new ArgumentException($"Target type '{target}' not supported. Valid values are 'monitor', 'window'");
        }

        return new WatchSession(sessionId, target.ToString(), actualTargetId, intervalMs, "active");
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static string StopWatch(
        [Description("The session ID")] string sessionId)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        _capture.StopStream(sessionId);
        return $"Stopped watching session {sessionId}";
    }

    // ============ AUDIO CAPTURE TOOLS ============

    // [McpServerTool removed for v2.0 - use unified tools]
    public static IReadOnlyList<AudioDeviceInfo> ListAudioDevices()
    {
        return AudioCaptureService.GetAudioDevices();
    }

    [McpServerTool, Description("Start audio capture from system or microphone")]
    public static AudioSession StartAudioCapture(
        [Description("Source: 'system', 'microphone', 'both'")] string source = "system",
        [Description("Sample rate")] int sampleRate = 44100,
        [Description("Microphone device index")] int deviceIndex = 0)
    {
        _audioCapture ??= new AudioCaptureService();

        if (!Enum.TryParse<AudioCaptureSource>(source, true, out var sourceEnum))
        {
            throw new ArgumentException($"Invalid source: {source}");
        }

        return _audioCapture.StartCapture(sourceEnum, sampleRate, deviceIndex);
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static async Task<AudioCaptureResult> StopAudioCapture(
        [Description("Session ID")] string sessionId,
        [Description("Return format: 'base64', 'file_path'")] string returnFormat = "base64")
    {
        if (_audioCapture == null)
        {
            throw new InvalidOperationException("AudioCaptureService not initialized");
        }

        return await _audioCapture.StopCaptureAsync(sessionId, returnFormat == "base64").ConfigureAwait(false);
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static IReadOnlyList<AudioSession> GetActiveAudioSessions()
    {
        return _audioCapture?.GetActiveSessions() ?? new List<AudioSession>();
    }

    // ============ WHISPER SPEECH RECOGNITION TOOLS ============

    // [McpServerTool removed for v2.0 - use unified tools]
    public static async Task<TranscriptionResult> Listen(
        [Description("Source: 'microphone', 'system', 'file', 'audio_session'")] AudioSourceType source = AudioSourceType.System,
        [Description("Source ID")] string? sourceId = null,
        [Description("Language code")] string language = "auto",
        [Description("Recording duration in seconds")] int duration = 10,
        [Description("Model size: 'tiny', 'base', 'small', 'medium', 'large'")] string modelSize = "base",
        [Description("Translate to English")] bool translate = false)
    {
        if (_whisperService == null)
        {
            throw new InvalidOperationException("WhisperTranscriptionService not initialized");
        }
        if (duration < 1) throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be at least 1 second");

        if (!Enum.TryParse<WhisperModelSize>(modelSize, true, out var modelSizeEnum))
        {
            throw new ArgumentException($"Invalid model size: {modelSize}. Valid values are 'tiny', 'base', 'small', 'medium', 'large'");
        }

        string? audioFilePath = null;
        bool shouldCleanup = false;

        try
        {
            switch (source)
            {
                case AudioSourceType.File:
                    if (string.IsNullOrEmpty(sourceId))
                    {
                        throw new ArgumentException("sourceId required when source='file'");
                    }
                    audioFilePath = sourceId;
                    if (!File.Exists(audioFilePath))
                    {
                        throw new FileNotFoundException($"Audio file not found: {audioFilePath}");
                    }
                    break;

                case AudioSourceType.AudioSession:
                    if (_audioCapture == null)
                    {
                        throw new InvalidOperationException("AudioCaptureService not initialized");
                    }
                    if (string.IsNullOrEmpty(sourceId))
                    {
                        throw new ArgumentException("sourceId required when source='audio_session'");
                    }
                    var audioResult = await _audioCapture.StopCaptureAsync(sourceId, false).ConfigureAwait(false);
                    audioFilePath = Path.Combine(Path.GetTempPath(), $"whisper_temp_{Guid.NewGuid()}.wav");
                    File.WriteAllBytes(audioFilePath, Convert.FromBase64String(audioResult.AudioDataBase64));
                    shouldCleanup = true;
                    break;

                case AudioSourceType.Microphone:
                case AudioSourceType.System:
                    _audioCapture ??= new AudioCaptureService();
                    var captureSource = source == AudioSourceType.Microphone ? AudioCaptureSource.Microphone : AudioCaptureSource.System;
                    var session = _audioCapture.StartCapture(captureSource, 16000);

                    Console.WriteLine($"[Listen] Recording {(source == AudioSourceType.Microphone ? "microphone" : "system")} audio for {duration} seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(duration)).ConfigureAwait(false);

                    var capturedAudio = await _audioCapture.StopCaptureAsync(session.SessionId, false).ConfigureAwait(false);
                    audioFilePath = capturedAudio.OutputPath;
                    if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
                    {
                        throw new InvalidOperationException("Audio file not found after capture");
                    }
                    shouldCleanup = false;
                    break;

                default:
                    throw new ArgumentException($"Unknown source: {source}. Valid values are 'microphone', 'system', 'file', 'audio_session'");
            }

            Console.WriteLine($"[Listen] Transcribing with {modelSize} model...");
            var langCode = language == "auto" ? null : language;

            var result = _whisperService.TranscribeFileAsync(
                audioFilePath,
                langCode,
                modelSizeEnum,
                translate).GetAwaiter().GetResult();

            Console.WriteLine($"[Listen] Transcription complete: {result.Segments.Count} segments");

            return result;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Listen] ERROR: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            if (shouldCleanup && audioFilePath != null && File.Exists(audioFilePath))
            {
                try
                {
                    File.Delete(audioFilePath);
                }
                catch { }
            }
        }
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static Dictionary<string, object> GetWhisperModelInfo()
    {
        var models = WhisperTranscriptionService.GetModelInfo();
        var result = new Dictionary<string, object>();

        foreach (var kvp in models)
        {
            result[kvp.Key.ToString()] = new
            {
                size = kvp.Value.Size,
                performance = kvp.Value.Performance,
                bestFor = kvp.Value.BestFor
            };
        }

        return result;
    }

    // ============ INPUT TOOLS ============

    // [McpServerTool removed for v2.0 - use unified tools]
    public static void MouseMove(
        [Description("X coordinate")] int x,
        [Description("Y coordinate")] int y)
    {
        
        InputService.MoveMouse(x, y);
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static void MouseClick(
        [Description("Button: 'left', 'right', 'middle'")] MouseButtonName button = MouseButtonName.Left,
        [Description("Click count")] int count = 1)
    {
        
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), "Click count must be at least 1");

        var mouseButton = button switch
        {
            MouseButtonName.Left => MouseButton.Left,
            MouseButtonName.Right => MouseButton.Right,
            MouseButtonName.Middle => MouseButton.Middle,
            _ => throw new ArgumentException($"Invalid button: {button}")
        };

        InputService.ClickMouseAsync(mouseButton, count).GetAwaiter().GetResult();
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static void MouseDrag(
        [Description("Start X")] int startX,
        [Description("Start Y")] int startY,
        [Description("End X")] int endX,
        [Description("End Y")] int endY)
    {
        
        InputService.DragMouseAsync(startX, startY, endX, endY).GetAwaiter().GetResult();
    }

    [McpServerTool, Description("Press or release keyboard keys. Replaces: keyboard_type, press_key.")]
    public static void KeyboardKey(
        [Description("Key name: enter, tab, escape, space, backspace, delete, left, up, right, down, home, end, pageup, pagedown")] string key,
        [Description("Action: 'press', 'release', 'click'")] KeyActionType action = KeyActionType.Click)
    {
        
        if (key == null) throw new ArgumentNullException(nameof(key), "Key cannot be null");

        var keyAction = action switch
        {
            KeyActionType.Press => KeyAction.Press,
            KeyActionType.Release => KeyAction.Release,
            KeyActionType.Click => KeyAction.Click,
            _ => KeyAction.Click
        };

        var virtualKey = key.ToUpperInvariant() switch
        {
            "ENTER" or "RETURN" => VirtualKeys.Enter,
            "TAB" => VirtualKeys.Tab,
            "ESCAPE" or "ESC" => VirtualKeys.Escape,
            "SPACE" => VirtualKeys.Space,
            "BACKSPACE" => VirtualKeys.Backspace,
            "DELETE" or "DEL" => VirtualKeys.Delete,
            "LEFT" => VirtualKeys.Left,
            "UP" => VirtualKeys.Up,
            "RIGHT" => VirtualKeys.Right,
            "DOWN" => VirtualKeys.Down,
            "HOME" => VirtualKeys.Home,
            "END" => VirtualKeys.End,
            "PAGEUP" => VirtualKeys.PageUp,
            "PAGEDOWN" => VirtualKeys.PageDown,
            _ => throw new ArgumentException($"Key '{key}' is not allowed or unknown. Allowed keys: enter, tab, escape, space, backspace, delete, arrow keys, home, end, pageup, pagedown")
        };

        InputService.PressKey(virtualKey, keyAction);
    }

    /// <summary>
    /// Closes a window by its handle (HWND).
    /// Accepts hwnd as string for compatibility with JSON clients that serialize numbers as strings.
    /// </summary>
    // [McpServerTool removed for v2.0 - use unified tools]
    public static void CloseWindow(
        [Description("Window handle (HWND) as string or number to close")] string hwndStr)
    {
        if (!long.TryParse(hwndStr, out var hwnd))
            throw new ArgumentException($"Invalid hwnd: '{hwndStr}'. Must be integer.");

        InputService.TerminateWindowProcess(new IntPtr(hwnd));
    }

    // ============ VIDEO CAPTURE TOOLS ============

    /// <summary>
    /// Starts a high-efficiency video capture stream for LLM consumption.
    /// Optimized for low-latency, low-token visual information delivery.
    /// </summary>
    // [McpServerTool removed for v2.0 - use unified tools]
    public static async Task<string> WatchVideo(
        McpServer server,
        [Description("Target name: 'YouTube', 'Netflix', 'ActiveWindow', or window title substring")] string targetName = "ActiveWindow",
        [Description("Frame rate (fps), default 10")] int fps = 10,
        [Description("JPEG quality (1-100), default 65")] int quality = 65,
        [Description("Enable change detection to skip duplicate frames")] bool enableChangeDetection = true,
        [Description("Change threshold (0.05-0.20), default 0.08 (8%)")] double changeThreshold = 0.08,
        [Description("Key frame interval in seconds, default 10")] int keyFrameInterval = 10)
    {
        if (_videoCapture == null)
            throw new InvalidOperationException("VideoCaptureService not initialized");

        if (fps < 1 || fps > 30)
            throw new ArgumentOutOfRangeException(nameof(fps), "FPS must be between 1 and 30");

        if (quality < 1 || quality > 100)
            throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 1 and 100");

        Console.Error.WriteLine($"[WatchVideo] Starting video stream for: {targetName}, FPS: {fps}, Quality: {quality}");

        try
        {
            var sessionId = await _videoCapture.StartVideoStreamAsync(
                targetName: targetName,
                fps: fps,
                quality: quality,
                maxWidth: 640,
                enableChangeDetection: enableChangeDetection,
                changeThreshold: changeThreshold,
                keyFrameInterval: keyFrameInterval,
                onFrameCaptured: async (payload) =>
                {
                    var notificationData = new Dictionary<string, object?>
                    {
                        ["level"] = "info",
                        ["data"] = new Dictionary<string, object>
                        {
                            ["sessionId"] = "video_" + Guid.NewGuid().ToString()[..8],
                            ["timestamp"] = payload.Timestamp,
                            ["systemTime"] = payload.SystemTime,
                            ["windowTitle"] = payload.WindowInfo.Title,
                            ["hasChange"] = payload.VisualMetadata.HasChange,
                            ["eventTag"] = payload.VisualMetadata.EventTag,
                            ["image"] = payload.ImageData,
                            ["type"] = "video_frame"
                        }
                    };
                    await server.SendNotificationAsync("notifications/message", notificationData).ConfigureAwait(false);
                }
            );

            Console.Error.WriteLine($"[WatchVideo] Video stream started: {sessionId}");
            return sessionId;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WatchVideo] Error starting video stream: {ex.Message}");
            throw;
        }
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static string StopWatchVideo(
        [Description("The session ID returned by watch_video")] string sessionId)
    {
        if (_videoCapture == null)
            throw new InvalidOperationException("VideoCaptureService not initialized");

        _videoCapture.StopVideoStream(sessionId);
        return $"Stopped video stream {sessionId}";
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static object GetLatestVideoFrame(
        [Description("The session ID")] string sessionId)
    {
        if (_videoCapture == null)
            throw new InvalidOperationException("VideoCaptureService not initialized");

        var payload = _videoCapture.GetLatestPayload(sessionId);
        
        if (payload == null)
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
            ["_llm_instruction"] = new Dictionary<string, object>
            {
                ["action"] = LlmInstructions.ProcessAndDiscardImage.Action,
                ["steps"] = LlmInstructions.ProcessAndDiscardImage.Steps,
                ["token_warning"] = LlmInstructions.ProcessAndDiscardImage.TokenWarning
            },
            ["sessionId"] = sessionId,
            ["hasFrame"] = true,
            ["timestamp"] = payload.Timestamp,
            ["systemTime"] = payload.SystemTime,
            ["windowTitle"] = payload.WindowInfo.Title,
            ["isActive"] = payload.WindowInfo.IsActive,
            ["hasChange"] = payload.VisualMetadata.HasChange,
            ["eventTag"] = payload.VisualMetadata.EventTag,
            ["image"] = payload.ImageData,
            ["ocrText"] = payload.OcrText
        };
    }

    // ============ SPIRAL 1: UNIFIED VIDEO/AUDIO STREAM ============

    private static readonly Dictionary<string, DateTime> _unifiedSessionStartTimes = new();

    /// <summary>
    /// Prototype tool for unified video and audio capture with synchronized timeline.
    /// Combines camera_capture_stream and Listen functionality with RelativeTime.
    /// </summary>
    // [McpServerTool removed for v2.0 - use unified tools]
    public static async Task<string> WatchVideoV1(
        McpServer server,
        [Description("X coordinate of capture region")] int x,
        [Description("Y coordinate of capture region")] int y,
        [Description("Width of capture region")] int w,
        [Description("Height of capture region")] int h,
        [Description("JPEG quality (1-100), default 60")] int quality = 60,
        [Description("Frame rate, default 10")] int fps = 10,
        [Description("Transcription model size (tiny/base/small/medium/large), default 'base'")] string modelSize = "base",
        [Description("Enable audio transcription, default true")] bool enableAudio = true,
        [Description("Audio duration in seconds, default 30")] int audioDuration = 30)
    {
        if (_capture == null)
            throw new InvalidOperationException("ScreenCaptureService not initialized");

        if (w <= 0 || h <= 0)
            throw new ArgumentException("Width and height must be positive");
        if (quality < 1 || quality > 100)
            throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be between 1 and 100");

        var sessionId = Guid.NewGuid().ToString();
        var sessionStartTime = DateTime.UtcNow;
        
        Console.Error.WriteLine($"[WatchVideoV1] Starting unified stream: {sessionId}");
        Console.Error.WriteLine($"[WatchVideoV1] Region: ({x}, {y}) {w}x{h}, FPS: {fps}, Quality: {quality}");

        try
        {
            // Start video capture
            var videoSessionId = _capture.StartRegionStream2(x, y, w, h, quality);
            
            // Store session start time for unified timeline
            lock (_unifiedSessionStartTimes)
            {
                _unifiedSessionStartTimes[sessionId] = sessionStartTime;
            }

            // Start audio capture if enabled
            if (enableAudio && _audioCapture != null && _whisperService != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Start audio capture
                        var audioSession = _audioCapture.StartCapture(AudioCaptureSource.System);
                        Console.Error.WriteLine($"[WatchVideoV1] Audio capture started: {audioSession.SessionId}");

                        // Process audio in segments
                        while (_unifiedSessionStartTimes.ContainsKey(sessionId))
                        {
                            await Task.Delay(audioDuration * 1000).ConfigureAwait(false);
                            
                            if (!_unifiedSessionStartTimes.ContainsKey(sessionId))
                                break;

                            var segmentStartTime = DateTime.UtcNow;
                            var result = await _audioCapture.StopCaptureAsync(audioSession.SessionId, false).ConfigureAwait(false);
                            
                            if (!string.IsNullOrEmpty(result.OutputPath) && File.Exists(result.OutputPath))
                            {
                                // Transcribe
                                var model = Enum.TryParse<WhisperModelSize>(modelSize, true, out var parsed) ? parsed : WhisperModelSize.Base;
                                var transcript = await _whisperService.TranscribeFileAsync(
                                    result.OutputPath,
                                    language: null, // Use OS default
                                    modelSize: model,
                                    ct: default
                                ).ConfigureAwait(false);

                                // Send each segment with relative time
                                foreach (var segment in transcript.Segments)
                                {
                                    var relativeTime = (segmentStartTime - sessionStartTime).TotalSeconds + segment.Start.TotalSeconds;
                                    
                                    var notificationData = new Dictionary<string, object?>
                                    {
                                        ["level"] = "info",
                                        ["data"] = new Dictionary<string, object>
                                        {
                                            ["sessionId"] = sessionId,
                                            ["type"] = "audio",
                                            ["relativeTime"] = relativeTime,
                                            ["systemTime"] = DateTime.UtcNow.ToString("O"),
                                            ["language"] = transcript.Language,
                                            ["text"] = segment.Text
                                        }
                                    };

                                    await server.SendNotificationAsync("notifications/message", notificationData).ConfigureAwait(false);
                                    Console.Error.WriteLine($"[WatchVideoV1] Audio: relativeTime={relativeTime:F2}s, text={segment.Text}");
                                }
                            }

                            // Restart capture for next segment
                            audioSession = _audioCapture.StartCapture(AudioCaptureSource.System);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WatchVideoV1] Audio error: {ex.Message}");
                    }
                });
            }

            // Process video frames with relative time
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_capture.TryGetSession(videoSessionId, out var videoSession) && videoSession != null)
                    {
                        await foreach (var frame in videoSession.Channel.Reader.ReadAllAsync(videoSession.Cts.Token))
                        {
                            var relativeTime = (DateTime.UtcNow - sessionStartTime).TotalSeconds;
                            
                            var notificationData = new Dictionary<string, object?>
                            {
                                ["level"] = "info",
                                ["data"] = new Dictionary<string, object>
                                {
                                    ["sessionId"] = sessionId,
                                    ["type"] = "video",
                                    ["relativeTime"] = relativeTime,
                                    ["systemTime"] = DateTime.UtcNow.ToString("O"),
                                    ["hash"] = videoSession.LastFrameHash,
                                    ["image"] = frame
                                }
                            };

                            await server.SendNotificationAsync("notifications/message", notificationData).ConfigureAwait(false);
                            Console.Error.WriteLine($"[WatchVideoV1] Video: relativeTime={relativeTime:F2}s");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WatchVideoV1] Video error: {ex.Message}");
                }
            });

            Console.Error.WriteLine($"[WatchVideoV1] Unified stream started: {sessionId}");
            return sessionId;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WatchVideoV1] Error: {ex.Message}");
            throw;
        }
    }

    // [McpServerTool removed for v2.0 - use unified tools]
    public static string StopWatchVideoV1(
        [Description("The session ID returned by watch_video_v1")] string sessionId)
    {
        lock (_unifiedSessionStartTimes)
        {
            if (_unifiedSessionStartTimes.ContainsKey(sessionId))
            {
                _unifiedSessionStartTimes.Remove(sessionId);
                Console.Error.WriteLine($"[WatchVideoV1] Unified stream stopped: {sessionId}");
                return $"Stopped unified stream {sessionId}";
            }
        }

        return $"Session not found: {sessionId}";
    }

    // ============ ACCESSIBILITY & MONITOR TOOLS ============

    private static readonly Dictionary<string, MonitorSession> _monitorSessions = new();
    private static readonly Dictionary<string, VisualChangeDetector> _monitorDetectors = new();

    /// <summary>
    /// Read text from a window using UI Automation and return as Markdown
    /// </summary>
    [McpServerTool, Description("Read text content from a window using UI Automation. Replaces: read_window_text_v2.")]
    public static string ReadWindowText(
        [Description("Window handle (HWND) as string")] string hwndStr,
        [Description("Include buttons in output")] bool includeButtons = false)
    {
        if (_accessibilityService == null)
            throw new InvalidOperationException("AccessibilityService not initialized");

        if (!long.TryParse(hwndStr, out var hwnd))
        {
            throw new ArgumentException($"Invalid HWND format: '{hwndStr}'. Expected numeric string.");
        }

        Console.Error.WriteLine($"[ReadWindowText] Extracting text from window: {hwnd}");

        var text = _accessibilityService.ExtractWindowText(new IntPtr(hwnd), includeButtons);
        
        if (string.IsNullOrWhiteSpace(text))
        {
            return "No text content found in the window.";
        }

        return text;
    }

    /// <summary>
    /// Monitor a window for visual changes and send notifications
    /// </summary>
    // [McpServerTool removed for v2.0 - use unified tools]
    public static async Task<string> Monitor(
        McpServer server,
        [Description("Window handle (HWND)")] long hwnd,
        [Description("Sensitivity level: High (1%), Medium (5%), Low (15%)")] string sensitivity = "Medium",
        [Description("Check interval in milliseconds")] int intervalMs = 500)
    {
        if (_capture == null)
            throw new InvalidOperationException("ScreenCaptureService not initialized");

        var sensitivityLevel = Enum.TryParse<MonitorSensitivity>(sensitivity, true, out var s) 
            ? s 
            : MonitorSensitivity.Medium;

        var threshold = (double)sensitivityLevel / 100.0;

        Console.Error.WriteLine($"[Monitor] Starting monitor for window: {hwnd}, sensitivity: {sensitivityLevel}, threshold: {threshold}");

        var sessionId = Guid.NewGuid().ToString();
        var session = new MonitorSession
        {
            Id = sessionId,
            Hwnd = hwnd,
            Sensitivity = sensitivityLevel,
            IntervalMs = intervalMs
        };

        var detector = new VisualChangeDetector
        {
            GridSize = 16,
            ChangeThreshold = threshold,
            PixelThreshold = 30,
            KeyFrameInterval = 30
        };

        lock (_monitorSessions)
        {
            _monitorSessions[sessionId] = session;
            _monitorDetectors[sessionId] = detector;
        }

        // Start monitoring loop
        _ = Task.Run(async () =>
        {
            try
            {
                while (!session.Cts.IsCancellationRequested)
                {
                    var startTime = DateTime.UtcNow;

                    try
                    {
                        // Capture window
                        var frameBase64 = ScreenCaptureService.CaptureWindow(hwnd, 640, 60);
                        if (string.IsNullOrEmpty(frameBase64))
                        {
                            Console.Error.WriteLine($"[Monitor] Failed to capture window: {hwnd}");
                            await Task.Delay(intervalMs, session.Cts.Token).ConfigureAwait(false);
                            continue;
                        }

                        // Decode base64 to Bitmap for analysis
                        var imageBytes = Convert.FromBase64String(frameBase64);
                        using var frame = new Bitmap(new MemoryStream(imageBytes));

                        // Analyze for changes
                        var result = detector.AnalyzeFrame(frame);

                        if (result.ShouldSend)
                        {
                            var notificationData = new Dictionary<string, object?>
                            {
                                ["level"] = "info",
                                ["data"] = new Dictionary<string, object>
                                {
                                    ["type"] = "window_monitor",
                                    ["sessionId"] = sessionId,
                                    ["hwnd"] = hwnd,
                                    ["hasChange"] = result.HasChange,
                                    ["eventTag"] = result.EventTag,
                                    ["changeRatio"] = result.ChangeRatio,
                                    ["changedGridIndices"] = result.ChangedGridIndices.Count > 0 
                                        ? result.ChangedGridIndices.ToArray() 
                                        : Array.Empty<int>()
                                }
                            };

                            await server.SendNotificationAsync("notifications/message", notificationData).ConfigureAwait(false);
                            Console.Error.WriteLine($"[Monitor] Change detected: {result.EventTag}, ratio: {result.ChangeRatio:F3}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Monitor] Error: {ex.Message}");
                    }

                    // Maintain interval
                    var elapsed = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
                    var delay = Math.Max(0, intervalMs - elapsed);
                    if (delay > 0)
                    {
                        try
                        {
                            await Task.Delay(delay, session.Cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                Console.Error.WriteLine($"[Monitor] Session ended: {sessionId}");
            }
        }, session.Cts.Token);

        Console.Error.WriteLine($"[Monitor] Started: {sessionId}");
        return sessionId;
    }

    /// <summary>
    /// Stop monitoring a window
    /// </summary>
    // [McpServerTool removed for v2.0 - use unified tools]
    public static string StopMonitor(
        [Description("The session ID returned by monitor")] string sessionId)
    {
        lock (_monitorSessions)
        {
            if (_monitorSessions.TryGetValue(sessionId, out var session))
            {
                session.Cts.Cancel();
                session.Dispose();
                _monitorSessions.Remove(sessionId);

                if (_monitorDetectors.TryGetValue(sessionId, out var detector))
                {
                    detector.Dispose();
                    _monitorDetectors.Remove(sessionId);
                }

                Console.Error.WriteLine($"[Monitor] Stopped: {sessionId}");
                return $"Stopped monitor {sessionId}";
            }
        }

        return $"Session not found: {sessionId}";
    }

    // ============ VIDEO CO-VIEW SYNC (v2) ============

    private static readonly Dictionary<string, VideoCoViewSession> _coviewSessions = new();

    /// <summary>
    /// Start synchronized video/audio capture for co-viewing experience
    /// </summary>
    // [McpServerTool removed for v2.0 - use unified tools]
    public static async Task<string> WatchVideoV2(
        McpServer server,
        [Description("X coordinate of capture region")] int x,
        [Description("Y coordinate of capture region")] int y,
        [Description("Width of capture region")] int w,
        [Description("Height of capture region")] int h,
        [Description("Capture interval in milliseconds (default 2000)")] int intervalMs = 2000,
        [Description("JPEG quality (1-100), default 60")] int quality = 60,
        [Description("Maximum width for resizing, default 640")] int maxWidth = 640,
        [Description("Whisper model size (tiny/base/small), default base")] string modelSize = "base",
        [Description("Language code for transcription (empty for auto-detect)")] string? language = null)
    {
        if (_capture == null)
            throw new InvalidOperationException("ScreenCaptureService not initialized");
        if (_audioCapture == null)
            throw new InvalidOperationException("AudioCaptureService not initialized");
        if (_whisperService == null)
            throw new InvalidOperationException("WhisperTranscriptionService not initialized");

        if (w <= 0 || h <= 0)
            throw new ArgumentException("Width and height must be positive");
        if (intervalMs < 500)
            throw new ArgumentException("Interval must be at least 500ms");

        var sessionId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        Console.Error.WriteLine($"[WatchVideoV2] Starting session: {sessionId}");
        Console.Error.WriteLine($"[WatchVideoV2] Region: ({x}, {y}) {w}x{h}, Interval: {intervalMs}ms");

        var session = new VideoCoViewSession
        {
            Id = sessionId,
            Hwnd = 0,
            IntervalMs = intervalMs,
            Quality = quality,
            MaxWidth = maxWidth,
            ModelSize = modelSize,
            Language = language ?? WhisperTranscriptionService.GetDefaultLanguage(),
            StartTime = startTime
        };

        lock (_coviewSessions)
        {
            _coviewSessions[sessionId] = session;
        }

        // Start the capture loop
        _ = Task.Run(async () =>
        {
            // 絶対時刻スケジュール: 次のキャプチャ予定時刻を管理
            var nextCaptureTime = startTime;

            try
            {
                while (!session.Cts.IsCancellationRequested)
                {
                    // 次のキャプチャ時刻まで待機（絶対時刻ベース）
                    var waitMs = (int)(nextCaptureTime - DateTime.UtcNow).TotalMilliseconds;
                    if (waitMs > 0)
                    {
                        try
                        {
                            await Task.Delay(waitMs, session.Cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                    else if (waitMs < -500)
                    {
                        // 大幅な遅延を警告
                        Console.Error.WriteLine($"[WatchVideoV2] Warning: Capture delayed by {-waitMs}ms");
                    }

                    try
                    {
                        // Start audio capture for this interval
                        var audioSession = _audioCapture.StartCapture(AudioCaptureSource.System);

                        // Capture video frame
                        var frameBase64 = ScreenCaptureService.CaptureRegion(x, y, w, h, maxWidth, quality);
                        
                        // Normalize base64 (remove newlines and spaces)
                        frameBase64 = frameBase64.Replace("\n", "").Replace("\r", "").Replace(" ", "");

                        // 実測タイムスタンプ: キャプチャ処理完了直後の時刻を使用
                        var captureCompletedTime = DateTime.UtcNow;
                        var ts = (captureCompletedTime - startTime).TotalSeconds;

                        Console.Error.WriteLine($"[WatchVideoV2] ts={ts:F1}s - Frame captured ({frameBase64.Length} chars)");

                        // Stop audio capture
                        var audioResult = await _audioCapture.StopCaptureAsync(audioSession.SessionId, false).ConfigureAwait(false);
                        Console.Error.WriteLine($"[WatchVideoV2] ts={ts:F1}s - Audio capture stopped");

                        // Transcribe audio in background (don't block next capture)
                        var transcriptionTask = Task.Run(async () =>
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(audioResult.OutputPath) && File.Exists(audioResult.OutputPath))
                                {
                                    var parsedModel = Enum.TryParse<WhisperModelSize>(session.ModelSize, true, out var m) ? m : WhisperModelSize.Base;
                                    var transcript = await _whisperService.TranscribeFileAsync(
                                        audioResult.OutputPath,
                                        language: session.Language,
                                        modelSize: parsedModel,
                                        ct: session.Cts.Token
                                    ).ConfigureAwait(false);

                                    var text = string.Join(" ", transcript.Segments.Select(s => s.Text.Trim()));
                                    Console.Error.WriteLine($"[WatchVideoV2] ts={ts:F1}s - Transcription: {text}");

                                    // Clean up temp audio file
                                    try { File.Delete(audioResult.OutputPath); } catch { }

                                    return text;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[WatchVideoV2] ts={ts:F1}s - Transcription error: {ex.Message}");
                            }
                            return null;
                        }, session.Cts.Token);

                        // Wait for transcription (with timeout)
                        var transcriptionResult = await transcriptionTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                        // 次のキャプチャ時刻を更新（厳密な間隔維持）
                        nextCaptureTime = nextCaptureTime.AddMilliseconds(intervalMs);

                        // Send notification with actual capture timestamp
                        var notificationData = new Dictionary<string, object?>
                        {
                            ["level"] = "info",
                            ["data"] = new Dictionary<string, object>
                            {
                                ["_llm_instruction"] = new Dictionary<string, object>
                                {
                                    ["action"] = LlmInstructions.ProcessAndDiscardImage.Action,
                                    ["steps"] = LlmInstructions.ProcessAndDiscardImage.Steps,
                                    ["token_warning"] = LlmInstructions.ProcessAndDiscardImage.TokenWarning
                                },
                                ["type"] = "video_coview",
                                ["sessionId"] = sessionId,
                                ["ts"] = Math.Round(ts, 1),
                                ["frame"] = frameBase64,
                                ["transcript"] = transcriptionResult ?? "",
                                ["windowTitle"] = $"Region ({x}, {y}) {w}x{h}"
                            }
                        };

                        await server.SendNotificationAsync("notifications/message", notificationData).ConfigureAwait(false);
                        Console.Error.WriteLine($"[WatchVideoV2] ts={ts:F1}s - Notification sent");
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WatchVideoV2] Error: {ex.Message}");
                        // エラー時も次のキャプチャ時刻を更新
                        nextCaptureTime = nextCaptureTime.AddMilliseconds(intervalMs);
                    }
                }
            }
            finally
            {
                Console.Error.WriteLine($"[WatchVideoV2] Session ended: {sessionId}");
                lock (_coviewSessions)
                {
                    _coviewSessions.Remove(sessionId);
                }
            }
        }, session.Cts.Token);

        return sessionId;
    }

    /// <summary>
    /// Stop video co-view session
    /// </summary>
    // [McpServerTool removed for v2.0 - use unified tools]
    public static string StopWatchVideoV2(
        [Description("The session ID returned by watch_video_v2")] string sessionId)
    {
        lock (_coviewSessions)
        {
            if (_coviewSessions.TryGetValue(sessionId, out var session))
            {
                session.Cts.Cancel();
                session.Dispose();
                _coviewSessions.Remove(sessionId);
                Console.Error.WriteLine($"[WatchVideoV2] Stopped: {sessionId}");
                return $"Stopped video co-view session {sessionId}";
            }
        }

        return $"Session not found: {sessionId}";
    }

    // ============ NEW UNIFIED TOOLS (v2.0) ============

    private static SessionManager? _sessionManager;

    public static void SetSessionManager(SessionManager sessionManager) => _sessionManager = sessionManager;

    // Enum for visual target types
    public enum VisualTargetType
    {
        All,
        Monitor,
        Window
    }

    // Enum for visual capture modes
    public enum VisualCaptureMode
    {
        Normal,
        Detailed
    }

    // Enum for watch modes
    public enum VisualWatchMode
    {
        Video,
        Monitor,
        Unified
    }

    // Enum for mouse actions
    public enum MouseActionType
    {
        Move,
        Click,
        Drag
    }

    // Enum for window actions
    public enum WindowActionType
    {
        Close,
        Minimize,
        Maximize,
        Restore
    }

    /// <summary>
    /// Unified tool to list visual targets (monitors, windows, or all)
    /// </summary>
    [McpServerTool, Description("List monitors or windows. Replaces: list_monitors, list_windows.")]
    public static object VisualList(
        [Description("Target type: 'monitor', 'window', or 'all' (default)")] string type = "all",
        [Description("Filter by title (for windows)")] string? filter = null)
    {
        if (_capture == null)
            throw new InvalidOperationException("ScreenCaptureService not initialized");

        var targetType = Enum.TryParse<VisualTargetType>(type, true, out var parsed) ? parsed : VisualTargetType.All;

        switch (targetType)
        {
            case VisualTargetType.Monitor:
                var monitors = _capture.GetMonitors();
                return new { type = "monitors", count = monitors.Count, items = monitors };

            case VisualTargetType.Window:
                var windows = ScreenCaptureService.GetWindows();
                if (!string.IsNullOrEmpty(filter))
                {
                    windows = windows.Where(w => w.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                return new { type = "windows", count = windows.Count, items = windows };

            case VisualTargetType.All:
            default:
                var allMonitors = _capture.GetMonitors();
                var allWindows = ScreenCaptureService.GetWindows();
                if (!string.IsNullOrEmpty(filter))
                {
                    allWindows = allWindows.Where(w => w.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                return new 
                { 
                    type = "all", 
                    monitors = new { count = allMonitors.Count, items = allMonitors },
                    windows = new { count = allWindows.Count, items = allWindows }
                };
        }
    }

    /// <summary>
    /// Unified tool to capture visual content with dynamic quality control
    /// </summary>
    [McpServerTool, Description("Capture visual content (monitor, window, or region) with dynamic quality. Replaces: capture, see, capture_window, capture_region. CRITICAL: Returns large base64 data. Process immediately and discard.")]
    public static async Task<object> VisualCapture(
        [Description("Target type: 'monitor', 'window', 'region', or 'primary' (default)")] string target = "primary",
        [Description("Monitor index (for 'monitor' type)")] uint monitorIndex = 0,
        [Description("Window handle (for 'window' type) as string")] string hwnd = null,
        [Description("Region X (for 'region' type)")] int x = 0,
        [Description("Region Y (for 'region' type)")] int y = 0,
        [Description("Region width (for 'region' type)")] int w = 640,
        [Description("Region height (for 'region' type)")] int h = 360,
        [Description("Capture mode: 'normal' (quality=30, default) or 'detailed' (quality=70)")] string mode = "normal",
        [Description("Maximum width for resizing")] int maxWidth = 640)
    {
        if (_capture == null)
            throw new InvalidOperationException("ScreenCaptureService not initialized");

        var captureMode = Enum.TryParse<VisualCaptureMode>(mode, true, out var parsed) ? parsed : VisualCaptureMode.Normal;
        var quality = captureMode == VisualCaptureMode.Detailed ? 70 : 30;

        string imageData;

        switch (target.ToLowerInvariant())
        {
            case "monitor":
                imageData = _capture.CaptureSingle(monitorIndex, maxWidth, quality);
                break;

            case "window":
                if (string.IsNullOrWhiteSpace(hwnd))
                    throw new ArgumentException("hwnd is required for window capture");
                if (!long.TryParse(hwnd, out var hwndValue))
                    throw new ArgumentException($"Invalid HWND format: '{hwnd}'. Expected numeric string.");
                imageData = ScreenCaptureService.CaptureWindow(hwndValue, maxWidth, quality);
                break;

            case "region":
                imageData = ScreenCaptureService.CaptureRegion(x, y, w, h, maxWidth, quality);
                break;

            case "primary":
            default:
                imageData = _capture.CaptureSingle(0, maxWidth, quality);
                break;
        }

        return new
        {
            _llm_instruction = new
            {
                action = LlmInstructions.ProcessAndDiscardImage.Action,
                steps = LlmInstructions.ProcessAndDiscardImage.Steps,
                token_warning = $"This image consumes approx {imageData.Length / 3}+ tokens. Discarding saves 95% memory."
            },
            target,
            mode = captureMode.ToString().ToLowerInvariant(),
            quality,
            image = imageData
        };
    }

    /// <summary>
    /// Unified tool to start watching visual content with different modes
    /// </summary>
    [McpServerTool, Description("Start watching visual content with different modes (video, monitor, unified). Replaces: watch, watch_video_v2, monitor. CRITICAL: Returns large base64 data. Process immediately and discard.")]
    public static async Task<string> VisualWatch(
        McpServer server,
        [Description("Watch mode: 'video', 'monitor', or 'unified' (default: video)")] string mode = "video",
        [Description("Target type: 'monitor', 'window', or 'region'")] string target = "monitor",
        [Description("Monitor index (for 'monitor' type)")] uint monitorIndex = 0,
        [Description("Window handle (for 'window' type) as string")] string hwnd = null,
        [Description("Region X (for 'region' type)")] int x = 0,
        [Description("Region Y (for 'region' type)")] int y = 0,
        [Description("Region width (for 'region' type)")] int w = 640,
        [Description("Region height (for 'region' type)")] int h = 360,
        [Description("Frame rate (fps), default 5")] int fps = 5,
        [Description("Enable change detection, default true")] bool detectChanges = true,
        [Description("Change threshold (0.05-0.20), default 0.08")] double threshold = 0.08,
        [Description("Enable timestamp overlay on captured frames, default false")] bool overlay = false,
        [Description("Enable contextual prompt generation for LLM, default false")] bool context = false)
    {
        if (_sessionManager == null)
            throw new InvalidOperationException("SessionManager not initialized");
        if (_capture == null)
            throw new InvalidOperationException("ScreenCaptureService not initialized");

        var watchMode = Enum.TryParse<VisualWatchMode>(mode, true, out var parsed) ? parsed : VisualWatchMode.Video;
        var session = new UnifiedSession
        {
            Type = SessionType.Watch,
            Target = target,
            Metadata = new Dictionary<string, object>
            {
                ["mode"] = mode,
                ["target"] = target,
                ["fps"] = fps,
                ["detectChanges"] = detectChanges,
                ["threshold"] = threshold,
                ["overlay"] = overlay,
                ["context"] = context
            }
        };

        var sessionId = _sessionManager.RegisterSession(session);
        var intervalMs = 1000 / fps;

        _ = Task.Run(async () =>
        {
            var nextCaptureTime = DateTime.UtcNow;
            
            while (!session.Cts.IsCancellationRequested)
            {
                var waitMs = (int)(nextCaptureTime - DateTime.UtcNow).TotalMilliseconds;
                if (waitMs > 0)
                {
                    try { await Task.Delay(waitMs, session.Cts.Token); }
                    catch (OperationCanceledException) { break; }
                }

                try
                {
                    string imageData;
                    string? eventTag = null;
                    var quality = 30; // Normal mode quality
                    
                    switch (target.ToLowerInvariant())
                    {
                        case "window":
                            if (string.IsNullOrWhiteSpace(hwnd)) continue;
                            if (!long.TryParse(hwnd, out var hwndValue))
                            {
                                Console.Error.WriteLine($"[VisualWatch] Invalid HWND format: '{hwnd}'");
                                continue;
                            }
                            // Use HybridCaptureService for GPU-accelerated capture
                            if (_hybridCapture != null)
                            {
                                var bitmap = await _hybridCapture.CaptureWindowAsync(new IntPtr(hwndValue));
                                if (bitmap == null)
                                {
                                    Console.Error.WriteLine($"[VisualWatch] Failed to capture window: {hwnd}");
                                    continue;
                                }
                                // Apply overlay if enabled
                                if (overlay)
                                {
                                    var elapsed = DateTime.UtcNow - session.StartTime;
                                    ImageOverlayService.OverlayTimestamp(bitmap, elapsed);
                                    if (eventTag != null)
                                        ImageOverlayService.OverlayEventTag(bitmap, eventTag);
                                }
                                using (bitmap)
                                using (var ms = new MemoryStream())
                                {
                                    bitmap.Save(ms, ImageFormat.Jpeg);
                                    imageData = Convert.ToBase64String(ms.ToArray());
                                }
                            }
                            else
                            {
                                imageData = ScreenCaptureService.CaptureWindow(hwndValue, 640, quality);
                            }
                            break;
                        case "region":
                            imageData = ScreenCaptureService.CaptureRegion(x, y, w, h, 640, quality);
                            break;
                        case "monitor":
                        default:
                            imageData = _capture.CaptureSingle(monitorIndex, 640, quality);
                            break;
                    }

                    var captureTime = DateTime.UtcNow;
                    var ts = (captureTime - session.StartTime).TotalSeconds;
                    var timestamp = captureTime.ToLocalTime().ToString("HH:mm:ss.f");
                    nextCaptureTime = nextCaptureTime.AddMilliseconds(intervalMs);

                    // Build notification data
                    var dataDict = new Dictionary<string, object>
                    {
                        ["_llm_instruction"] = new Dictionary<string, object>
                        {
                            ["action"] = LlmInstructions.ProcessAndDiscardImage.Action,
                            ["logging_policy"] = LlmInstructions.ProcessAndDiscardImage.LoggingPolicy,
                            ["steps"] = LlmInstructions.ProcessAndDiscardImage.Steps,
                            ["token_warning"] = $"This image consumes approx {imageData.Length / 3}+ tokens. Discarding saves 95% memory."
                        },
                        ["type"] = "visual_watch",
                        ["sessionId"] = sessionId,
                        ["mode"] = mode,
                        ["ts"] = Math.Round(ts, 1),
                        ["timestamp"] = timestamp,
                        ["image"] = imageData
                    };

                    // Add contextual prompt if enabled
                    if (context)
                    {
                        var frameContext = new FrameContextBuilder()
                            .WithFrame(imageData)
                            .WithTimestamps(captureTime, ts)
                            .WithEventTag(eventTag ?? "Frame")
                            .Build();
                        dataDict["prompt"] = frameContext.GenerateContextualPrompt();
                    }

                    var notificationData = new Dictionary<string, object?>
                    {
                        ["level"] = "info",
                        ["data"] = dataDict
                    };

                    await server.SendNotificationAsync("notifications/message", notificationData);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[VisualWatch] Error: {ex.Message}");
                    nextCaptureTime = nextCaptureTime.AddMilliseconds(intervalMs);
                }
            }
        }, session.Cts.Token);

        return sessionId;
    }

    /// <summary>
    /// Unified tool to stop any visual or input session
    /// </summary>
    [McpServerTool, Description("Stop any active session by ID or type. Replaces: stop_capture, stop_watch, stop_monitor.")]
    public static string VisualStop(
        [Description("Session ID to stop")] string sessionId,
        [Description("Stop all sessions of this type: 'watch', 'capture', 'audio', 'monitor', or 'all' (default)")] string type = "all")
    {
        if (_sessionManager == null)
            throw new InvalidOperationException("SessionManager not initialized");

        if (type.ToLowerInvariant() == "all" && string.IsNullOrEmpty(sessionId))
        {
            _sessionManager.StopAllSessions();
            return "Stopped all sessions";
        }

        if (!string.IsNullOrEmpty(sessionId))
        {
            if (_sessionManager.StopSession(sessionId))
                return $"Stopped session {sessionId}";
            return $"Session not found: {sessionId}";
        }

        if (Enum.TryParse<SessionType>(type, true, out var sessionType))
        {
            var count = _sessionManager.StopSessionsByType(sessionType);
            return $"Stopped {count} sessions of type {type}";
        }

        return "Invalid session type or ID";
    }

    /// <summary>
    /// Unified tool for mouse operations
    /// </summary>
    [McpServerTool, Description("Perform mouse operations (move, click, drag). Replaces: mouse_move, mouse_click.")]
    public static string InputMouse(
        [Description("Mouse action: 'move', 'click', or 'drag'")] string action,
        [Description("X coordinate")] int x,
        [Description("Y coordinate")] int y,
        [Description("End X coordinate (for drag)")] int? endX = null,
        [Description("End Y coordinate (for drag)")] int? endY = null,
        [Description("Mouse button: 'left' (default), 'right', or 'middle'")] string button = "left",
        [Description("Number of clicks (for click action), default 1")] int clicks = 1)
    {
        var actionType = Enum.TryParse<MouseActionType>(action, true, out var parsed) ? parsed : MouseActionType.Move;
        var buttonName = Enum.TryParse<MouseButtonName>(button, true, out var btn) ? btn : MouseButtonName.Left;

        switch (actionType)
        {
            case MouseActionType.Move:
                InputService.MoveMouse(x, y);
                return $"Mouse moved to ({x}, {y})";

            case MouseActionType.Click:
                InputService.MoveMouse(x, y);
                var mouseButton = buttonName switch
                {
                    MouseButtonName.Left => MouseButton.Left,
                    MouseButtonName.Right => MouseButton.Right,
                    MouseButtonName.Middle => MouseButton.Middle,
                    _ => MouseButton.Left
                };
                InputService.ClickMouseAsync(mouseButton, clicks).Wait();
                return $"Mouse clicked {clicks} time(s) at ({x}, {y}) with {buttonName} button";

            case MouseActionType.Drag:
                if (!endX.HasValue || !endY.HasValue)
                    throw new ArgumentException("endX and endY are required for drag action");
                InputService.DragMouseAsync(x, y, endX.Value, endY.Value).Wait();
                return $"Mouse dragged from ({x}, {y}) to ({endX.Value}, {endY.Value})";

            default:
                throw new ArgumentException($"Unknown action: {action}");
        }
    }

    /// <summary>
    /// Unified tool for window operations
    /// </summary>
    [McpServerTool, Description("Perform window operations (close, minimize, maximize, restore). Replaces: close_window.")]
    public static string InputWindow(
        [Description("Window handle (HWND) as string")] string hwnd,
        [Description("Window action: 'close' (default), 'minimize', 'maximize', or 'restore'")] string action = "close")
    {
        if (string.IsNullOrWhiteSpace(hwnd))
            throw new ArgumentException("hwnd is required");
        if (!long.TryParse(hwnd, out var hwndValue))
            throw new ArgumentException($"Invalid HWND format: '{hwnd}'. Expected numeric string.");
        
        var actionType = Enum.TryParse<WindowActionType>(action, true, out var parsed) ? parsed : WindowActionType.Close;
        var windowHandle = new IntPtr(hwndValue);

        switch (actionType)
        {
            case WindowActionType.Close:
                InputService.TerminateWindowProcess(windowHandle);
                return $"Window {hwndValue} closed";

            case WindowActionType.Minimize:
                InputService.MinimizeWindow(windowHandle);
                return $"Window {hwndValue} minimized";

            case WindowActionType.Maximize:
                InputService.MaximizeWindow(windowHandle);
                return $"Window {hwndValue} maximized";

            case WindowActionType.Restore:
                InputService.RestoreWindow(windowHandle);
                return $"Window {hwndValue} restored";

            default:
                throw new ArgumentException($"Unknown action: {action}");
        }
    }
}
