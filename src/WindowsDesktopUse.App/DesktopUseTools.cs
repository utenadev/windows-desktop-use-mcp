using System.ComponentModel;
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
public static class DesktopUseTools
{
    private static ScreenCaptureService? _capture;
    private static AudioCaptureService? _audioCapture;
    private static WhisperTranscriptionService? _whisperService;
    private static InputService? _inputService;

    public static void SetCaptureService(ScreenCaptureService capture) => _capture = capture;
    public static void SetAudioCaptureService(AudioCaptureService audioCapture) => _audioCapture = audioCapture;
    public static void SetWhisperService(WhisperTranscriptionService whisperService) => _whisperService = whisperService;
    public static void SetInputService(InputService inputService) => _inputService = inputService;

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

    [McpServerTool, Description("List all available monitors/displays with their index, name, resolution, and position")]
    public static IReadOnlyList<MonitorInfo> ListMonitors()
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        return _capture.GetMonitors().AsReadOnly();
    }

    [McpServerTool, Description("List all visible windows with their handles, titles, and dimensions")]
    public static IReadOnlyList<WindowInfo> ListWindows()
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        return _capture.GetWindows().AsReadOnly();
    }

    [McpServerTool, Description("Capture a screenshot of specified monitor or window. Returns the captured image as base64 JPEG.")]
    public static ImageContentBlock See(
        [Description("Target type: 'monitor' or 'window'")] string targetType = "monitor",
        [Description("Monitor index (0=primary) - used when targetType='monitor'")] uint monitor = 0,
        [Description("Window handle (HWND) - used when targetType='window'")] long? hwnd = null,
        [Description("JPEG quality (1-100)")] int quality = 80,
        [Description("Maximum width in pixels")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");

        var imageData = targetType == "window" && hwnd.HasValue
            ? _capture.CaptureWindow(hwnd.Value, maxWidth, quality)
            : _capture.CaptureSingle(monitor, maxWidth, quality);

        var base64Data = imageData.Contains(";base64,", StringComparison.Ordinal) ? imageData.Split(';')[1].Split(',')[1] : imageData;

        return new ImageContentBlock
        {
            MimeType = "image/jpeg",
            Data = base64Data
        };
    }

    [McpServerTool, Description("Capture a screenshot of a specific window by its handle (HWND)")]
    public static ImageContentBlock CaptureWindow(
        [Description("Window handle (HWND) to capture")] long hwnd,
        [Description("JPEG quality (1-100)")] int quality = 80,
        [Description("Maximum width in pixels")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");

        var imageData = _capture.CaptureWindow(hwnd, maxWidth, quality);
        var base64Data = imageData.Contains(";base64,", StringComparison.Ordinal) ? imageData.Split(';')[1].Split(',')[1] : imageData;

        return new ImageContentBlock
        {
            MimeType = "image/jpeg",
            Data = base64Data
        };
    }

    [McpServerTool, Description("Capture a screenshot of a specific screen region")]
    public static ImageContentBlock CaptureRegion(
        [Description("X coordinate")] int x,
        [Description("Y coordinate")] int y,
        [Description("Width")] int w,
        [Description("Height")] int h,
        [Description("JPEG quality")] int quality = 80,
        [Description("Maximum width")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");

        var imageData = _capture.CaptureRegion(x, y, w, h, maxWidth, quality);
        var base64Data = imageData.Contains(";base64,", StringComparison.Ordinal) ? imageData.Split(';')[1].Split(',')[1] : imageData;

        return new ImageContentBlock
        {
            MimeType = "image/jpeg",
            Data = base64Data
        };
    }

    [McpServerTool, Description("Start a continuous screen capture stream")]
    public static string StartWatching(
        McpServer server,
        [Description("Target type: 'monitor' or 'window'")] string targetType = "monitor",
        [Description("Monitor index")] uint monitor = 0,
        [Description("Window handle")] long? hwnd = null,
        [Description("Capture interval in milliseconds")] int intervalMs = 1000,
        [Description("JPEG quality")] int quality = 80,
        [Description("Maximum width")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");

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

    [McpServerTool, Description("Stop a running screen capture stream")]
    public static string StopWatching(
        [Description("The session ID returned by start_watching")] string sessionId)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        _capture.StopStream(sessionId);
        return "Stopped watching";
    }

    [McpServerTool, Description("Get the latest captured frame from a stream session")]
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

    [McpServerTool, Description("List all available capture targets (monitors and windows)")]
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
            var windowList = _capture.GetWindows();
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

    [McpServerTool, Description("Capture screen, window, or region as image")]
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
                imageData = _capture.CaptureWindow(hwnd, maxWidth, quality);
                actualTargetType = "window";
                actualTargetId = hwnd.ToString(CultureInfo.InvariantCulture);
                break;

            case CaptureTargetType.Region:
                if (!x.HasValue || !y.HasValue || !w.HasValue || !h.HasValue)
                    throw new ArgumentException("Region capture requires x, y, w, h");
                imageData = _capture.CaptureRegion(x.Value, y.Value, w.Value, h.Value, maxWidth, quality);
                actualTargetType = "region";
                actualTargetId = $"{x.Value},{y.Value},{w.Value},{h.Value}";
                capturedWidth = w.Value;
                capturedHeight = h.Value;
                break;

            default:
                throw new ArgumentException($"Unknown target type: {target}. Valid values are 'primary', 'monitor', 'window', 'region'");
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

    [McpServerTool, Description("Start watching/streaming a target")]
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

    [McpServerTool, Description("Stop watching a capture session")]
    public static string StopWatch(
        [Description("The session ID")] string sessionId)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        _capture.StopStream(sessionId);
        return $"Stopped watching session {sessionId}";
    }

    // ============ AUDIO CAPTURE TOOLS ============

    [McpServerTool, Description("List available audio capture devices")]
    public static IReadOnlyList<AudioDeviceInfo> ListAudioDevices()
    {
        return AudioCaptureService.GetAudioDevices().AsReadOnly();
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

    [McpServerTool, Description("Stop audio capture and return captured audio")]
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

    [McpServerTool, Description("Get list of active audio capture sessions")]
    public static IReadOnlyList<AudioSession> GetActiveAudioSessions()
    {
        return (_audioCapture?.GetActiveSessions() ?? new List<AudioSession>()).AsReadOnly();
    }

    // ============ WHISPER SPEECH RECOGNITION TOOLS ============

    [McpServerTool, Description("Transcribe audio to text using Whisper AI")]
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

    [McpServerTool, Description("Get available Whisper model information")]
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

    [McpServerTool, Description("Move mouse cursor to absolute position")]
    public static void MouseMove(
        [Description("X coordinate")] int x,
        [Description("Y coordinate")] int y)
    {
        if (_inputService == null) throw new InvalidOperationException("InputService not initialized");
        _inputService.MoveMouse(x, y);
    }

    [McpServerTool, Description("Click mouse button")]
    public static void MouseClick(
        [Description("Button: 'left', 'right', 'middle'")] MouseButtonName button = MouseButtonName.Left,
        [Description("Click count")] int count = 1)
    {
        if (_inputService == null) throw new InvalidOperationException("InputService not initialized");
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), "Click count must be at least 1");

        var mouseButton = button switch
        {
            MouseButtonName.Left => MouseButton.Left,
            MouseButtonName.Right => MouseButton.Right,
            MouseButtonName.Middle => MouseButton.Middle,
            _ => throw new ArgumentException($"Invalid button: {button}")
        };

        _inputService.ClickMouseAsync(mouseButton, count).GetAwaiter().GetResult();
    }

    [McpServerTool, Description("Drag mouse from start to end position")]
    public static void MouseDrag(
        [Description("Start X")] int startX,
        [Description("Start Y")] int startY,
        [Description("End X")] int endX,
        [Description("End Y")] int endY)
    {
        if (_inputService == null) throw new InvalidOperationException("InputService not initialized");
        _inputService.DragMouseAsync(startX, startY, endX, endY).GetAwaiter().GetResult();
    }

    [McpServerTool, Description("Press a navigation key (security restricted)")]
    public static void KeyboardKey(
        [Description("Key name: enter, tab, escape, space, backspace, delete, left, up, right, down, home, end, pageup, pagedown")] string key,
        [Description("Action: 'press', 'release', 'click'")] KeyActionType action = KeyActionType.Click)
    {
        if (_inputService == null) throw new InvalidOperationException("InputService not initialized");
        if (key == null) throw new ArgumentNullException(nameof(key), "Key cannot be null");

        var keyAction = action switch
        {
            KeyActionType.Press => KeyAction.Press,
            KeyActionType.Release => KeyAction.Release,
            KeyActionType.Click => KeyAction.Click,
            _ => KeyAction.Click
        };

        var virtualKey = key.ToLowerInvariant() switch
        {
            "enter" or "return" => InputService.VirtualKeys.Enter,
            "tab" => InputService.VirtualKeys.Tab,
            "escape" or "esc" => InputService.VirtualKeys.Escape,
            "space" => InputService.VirtualKeys.Space,
            "backspace" => InputService.VirtualKeys.Backspace,
            "delete" or "del" => InputService.VirtualKeys.Delete,
            "left" => InputService.VirtualKeys.Left,
            "up" => InputService.VirtualKeys.Up,
            "right" => InputService.VirtualKeys.Right,
            "down" => InputService.VirtualKeys.Down,
            "home" => InputService.VirtualKeys.Home,
            "end" => InputService.VirtualKeys.End,
            "pageup" => InputService.VirtualKeys.PageUp,
            "pagedown" => InputService.VirtualKeys.PageDown,
            _ => throw new ArgumentException($"Key '{key}' is not allowed or unknown. Allowed keys: enter, tab, escape, space, backspace, delete, arrow keys, home, end, pageup, pagedown")
        };

        _inputService.PressKey(virtualKey, keyAction);
    }

    [McpServerTool, Description("Close a window by its handle (HWND)")]
    public static void CloseWindow(
        [Description("Window handle (HWND) to close")] long hwnd)
    {
        if (_inputService == null) throw new InvalidOperationException("InputService not initialized");
        _inputService.TerminateWindowProcess(new IntPtr(hwnd));
    }
}
