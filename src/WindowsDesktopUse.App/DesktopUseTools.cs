using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WindowsDesktopUse.Core;
using WindowsDesktopUse.Screen;
using WindowsDesktopUse.Audio;
using WindowsDesktopUse.Transcription;
using WindowsDesktopUse.Input;

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

    // ============ SCREEN CAPTURE TOOLS ============

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

        var base64Data = imageData.Contains(";base64,") ? imageData.Split(';')[1].Split(',')[1] : imageData;

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
        var base64Data = imageData.Contains(";base64,") ? imageData.Split(';')[1].Split(',')[1] : imageData;

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
        var base64Data = imageData.Contains(";base64,") ? imageData.Split(';')[1].Split(',')[1] : imageData;

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
        [Description("Target type: 'monitor', 'window', 'region', 'primary'")] string target = "primary",
        [Description("Target identifier")] string? targetId = null,
        [Description("X coordinate for region")] int? x = null,
        [Description("Y coordinate for region")] int? y = null,
        [Description("Width for region")] int? w = null,
        [Description("Height for region")] int? h = null,
        [Description("JPEG quality")] int quality = 80,
        [Description("Maximum width")] int maxWidth = 1920)
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
                    throw new ArgumentException("Invalid window handle");
                imageData = _capture.CaptureWindow(hwnd, maxWidth, quality);
                actualTargetType = "window";
                actualTargetId = hwnd.ToString();
                break;

            case "region":
                if (!x.HasValue || !y.HasValue || !w.HasValue || !h.HasValue)
                    throw new ArgumentException("Region capture requires x, y, w, h");
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

    [McpServerTool, Description("Start watching/streaming a target")]
    public static WatchSession Watch(
        McpServer server,
        [Description("Target type: 'monitor', 'window'")] string target = "monitor",
        [Description("Target identifier")] string? targetId = null,
        [Description("Capture interval in milliseconds")] int intervalMs = 1000,
        [Description("JPEG quality")] int quality = 80,
        [Description("Maximum width")] int maxWidth = 1920)
    {
        if (_capture == null) throw new InvalidOperationException("ScreenCaptureService not initialized");
        if (intervalMs < 100)
            throw new ArgumentException("Interval must be at least 100ms");

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
                    throw new ArgumentException("Invalid window handle");
                sessionId = _capture.StartWindowStream(hwnd, intervalMs, quality, maxWidth);
                actualTargetId = hwnd.ToString();
                break;

            default:
                throw new ArgumentException($"Target type '{target}' not supported");
        }

        return new WatchSession(sessionId, target, actualTargetId, intervalMs, "active");
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
    public static List<AudioDeviceInfo> ListAudioDevices()
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

    [McpServerTool, Description("Stop audio capture and return captured audio")]
    public static AudioCaptureResult StopAudioCapture(
        [Description("Session ID")] string sessionId,
        [Description("Return format: 'base64', 'file_path'")] string returnFormat = "base64")
    {
        if (_audioCapture == null)
        {
            throw new InvalidOperationException("AudioCaptureService not initialized");
        }

        return _audioCapture.StopCapture(sessionId, returnFormat == "base64");
    }

    [McpServerTool, Description("Get list of active audio capture sessions")]
    public static List<AudioSession> GetActiveAudioSessions()
    {
        return _audioCapture?.GetActiveSessions() ?? new List<AudioSession>();
    }

    // ============ WHISPER SPEECH RECOGNITION TOOLS ============

    [McpServerTool, Description("Transcribe audio to text using Whisper AI")]
    public static TranscriptionResult Listen(
        [Description("Source: 'microphone', 'system', 'file', 'audio_session'")] string source = "system",
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

        if (!Enum.TryParse<WhisperModelSize>(modelSize, true, out var modelSizeEnum))
        {
            throw new ArgumentException($"Invalid model size: {modelSize}");
        }

        string? audioFilePath = null;
        bool shouldCleanup = false;

        try
        {
            switch (source.ToLower())
            {
                case "file":
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

                case "audio_session":
                    if (_audioCapture == null)
                    {
                        throw new InvalidOperationException("AudioCaptureService not initialized");
                    }
                    if (string.IsNullOrEmpty(sourceId))
                    {
                        throw new ArgumentException("sourceId required when source='audio_session'");
                    }
                    var audioResult = _audioCapture.StopCapture(sourceId, false);
                    audioFilePath = Path.Combine(Path.GetTempPath(), $"whisper_temp_{Guid.NewGuid()}.wav");
                    File.WriteAllBytes(audioFilePath, Convert.FromBase64String(audioResult.AudioDataBase64));
                    shouldCleanup = true;
                    break;

                case "microphone":
                case "system":
                    _audioCapture ??= new AudioCaptureService();
                    var captureSource = source.ToLower() == "microphone" ? AudioCaptureSource.Microphone : AudioCaptureSource.System;
                    var session = _audioCapture.StartCapture(captureSource, 16000);

                    Console.WriteLine($"[Listen] Recording {source} audio for {duration} seconds...");
                    Thread.Sleep(TimeSpan.FromSeconds(duration));

                    var capturedAudio = _audioCapture.StopCapture(session.SessionId, false);
                    audioFilePath = capturedAudio.OutputPath;
                    if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
                    {
                        throw new InvalidOperationException("Audio file not found after capture");
                    }
                    shouldCleanup = false;
                    break;

                default:
                    throw new ArgumentException($"Unknown source: {source}");
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
            result[kvp.Key.ToString().ToLower()] = new
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
        [Description("Button: 'left', 'right', 'middle'")] string button = "left",
        [Description("Click count")] int count = 1)
    {
        if (_inputService == null) throw new InvalidOperationException("InputService not initialized");

        var mouseButton = button.ToLower() switch
        {
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left
        };

        _inputService.ClickMouse(mouseButton, count);
    }

    [McpServerTool, Description("Drag mouse from start to end position")]
    public static void MouseDrag(
        [Description("Start X")] int startX,
        [Description("Start Y")] int startY,
        [Description("End X")] int endX,
        [Description("End Y")] int endY)
    {
        if (_inputService == null) throw new InvalidOperationException("InputService not initialized");
        _inputService.DragMouse(startX, startY, endX, endY);
    }

    [McpServerTool, Description("Type text (direct input)")]
    public static void KeyboardType(
        [Description("Text to type")] string text)
    {
        if (_inputService == null) throw new InvalidOperationException("InputService not initialized");
        _inputService.TypeText(text);
    }

    [McpServerTool, Description("Press a special key")]
    public static void KeyboardKey(
        [Description("Key name (enter, tab, escape, etc.)")] string key,
        [Description("Action: 'press', 'release', 'click'")] string action = "click")
    {
        if (_inputService == null) throw new InvalidOperationException("InputService not initialized");

        var keyAction = action.ToLower() switch
        {
            "press" => KeyAction.Press,
            "release" => KeyAction.Release,
            "click" => KeyAction.Click,
            _ => KeyAction.Click
        };

        var virtualKey = key.ToLower() switch
        {
            "enter" => InputService.VirtualKeys.Enter,
            "return" => InputService.VirtualKeys.Enter,
            "tab" => InputService.VirtualKeys.Tab,
            "escape" => InputService.VirtualKeys.Escape,
            "esc" => InputService.VirtualKeys.Escape,
            "space" => InputService.VirtualKeys.Space,
            "backspace" => InputService.VirtualKeys.Backspace,
            "delete" => InputService.VirtualKeys.Delete,
            "del" => InputService.VirtualKeys.Delete,
            "left" => InputService.VirtualKeys.Left,
            "up" => InputService.VirtualKeys.Up,
            "right" => InputService.VirtualKeys.Right,
            "down" => InputService.VirtualKeys.Down,
            "home" => InputService.VirtualKeys.Home,
            "end" => InputService.VirtualKeys.End,
            "pageup" => InputService.VirtualKeys.PageUp,
            "pagedown" => InputService.VirtualKeys.PageDown,
            "shift" => InputService.VirtualKeys.Shift,
            "ctrl" => InputService.VirtualKeys.Control,
            "control" => InputService.VirtualKeys.Control,
            "alt" => InputService.VirtualKeys.Alt,
            "win" => InputService.VirtualKeys.Win,
            "windows" => InputService.VirtualKeys.Win,
            _ => throw new ArgumentException($"Unknown key: {key}")
        };

        _inputService.PressKey(virtualKey, keyAction);
    }
}
