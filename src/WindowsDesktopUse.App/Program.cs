using System.CommandLine;
using System.Runtime.InteropServices;
using WindowsDesktopUse.Screen;
using WindowsDesktopUse.Audio;
using WindowsDesktopUse.Transcription;
using WindowsDesktopUse.Input;
using WindowsDesktopUse.App;

[DllImport("user32.dll")] static extern bool SetProcessDPIAware();

// Create subcommands
var doctorCmd = new Command("doctor", "Diagnose system compatibility and configuration");
var setupCmd = new Command("setup", "Configure Claude Desktop integration");
var serverCmd = new Command("server", "Run MCP server (default)") { IsHidden = true };

// Doctor command
doctorCmd.SetHandler(() =>
{
    Console.WriteLine("🔍 Windows Desktop Use - System Diagnostics");
    Console.WriteLine("==========================================");
    Console.WriteLine();

    var hasError = false;

    // Check OS
    Console.WriteLine($"✓ Operating System: {Environment.OSVersion}");
    if (Environment.OSVersion.Version.Major >= 10)
    {
        Console.WriteLine("  ✓ Windows 10/11 detected");
    }
    else
    {
        Console.WriteLine("  ✗ Windows 10 or later required");
        hasError = true;
    }

    // Check .NET
    Console.WriteLine($"✓ .NET Runtime: {Environment.Version}");
    
    // Check monitors
    try
    {
        SetProcessDPIAware();
        var captureService = new ScreenCaptureService(0);
        captureService.InitializeMonitors();
        var monitors = captureService.GetMonitors();
        Console.WriteLine($"✓ Displays detected: {monitors.Count}");
        foreach (var mon in monitors)
        {
            Console.WriteLine($"  - {mon.Name}: {mon.W}x{mon.H} at ({mon.X},{mon.Y})");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Screen capture test failed: {ex.Message}");
        hasError = true;
    }

    // Check audio devices
    try
    {
        var devices = AudioCaptureService.GetAudioDevices();
        Console.WriteLine($"✓ Audio devices: {devices.Count}");
    }
    catch
    {
        Console.WriteLine("⚠ Audio device detection skipped (may require admin)");
    }

    Console.WriteLine();
    if (hasError)
    {
        Console.WriteLine("❌ Diagnostics completed with errors");
        Environment.Exit(1);
    }
    else
    {
        Console.WriteLine("✅ All diagnostics passed!");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("  1. Run 'WindowsDesktopUse setup' to configure Claude Desktop");
        Console.WriteLine("  2. Start Claude Desktop and begin using WindowsDesktopUse");
    }
});

// Setup command
setupCmd.SetHandler(() =>
{
    Console.WriteLine("🔧 Windows Desktop Use - Setup");
    Console.WriteLine("==============================");
    Console.WriteLine();

    var exePath = System.AppContext.BaseDirectory;
    if (exePath.EndsWith("\\") || exePath.EndsWith("/"))
        exePath = exePath.TrimEnd('\\', '/');
    var exeName = "WindowsDesktopUse.exe";
    var fullExePath = Path.Combine(exePath, exeName);
    
    var configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Claude", "claude_desktop_config.json");

    Console.WriteLine($"Executable: {fullExePath}");
    Console.WriteLine($"Config file: {configPath}");
    Console.WriteLine();

    var config = new
    {
        mcpServers = new
        {
            windowsDesktopUse = new
            {
                command = fullExePath,
                args = new[] { "--httpPort", "5000" }
            }
        }
    };

    var jsonOptions = new System.Text.Json.JsonSerializerOptions 
    { 
        WriteIndented = true 
    };
    var json = System.Text.Json.JsonSerializer.Serialize(config, jsonOptions);

    Console.WriteLine("Generated configuration:");
    Console.WriteLine("------------------------");
    Console.WriteLine(json);
    Console.WriteLine("------------------------");
    Console.WriteLine();

    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, json);
        Console.WriteLine("✅ Configuration saved to Claude Desktop!");
        Console.WriteLine();
        Console.WriteLine("Please restart Claude Desktop to apply changes.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Failed to save configuration: {ex.Message}");
        Console.WriteLine();
        Console.WriteLine("Please manually add the above configuration to:");
        Console.WriteLine(configPath);
        Environment.Exit(1);
    }
});

// Main server command options
var desktopOption = new Option<uint>(
    name: "--desktopNum",
    description: "Default monitor index (0=primary)",
    getDefaultValue: () => 0u);

var httpPortOption = new Option<int>(
    name: "--httpPort",
    description: "HTTP server port for frame streaming (0=disable)",
    getDefaultValue: () => 5000);

var testOption = new Option<bool>(
    name: "--test-whisper",
    description: "Test Whisper transcription directly",
    getDefaultValue: () => false);

// Root command with subcommands
var rootCmd = new RootCommand("Windows Desktop Use MCP Server");
rootCmd.AddCommand(doctorCmd);
rootCmd.AddCommand(setupCmd);

// Add server options to root command (default behavior)
rootCmd.AddOption(desktopOption);
rootCmd.AddOption(httpPortOption);
rootCmd.AddOption(testOption);

rootCmd.SetHandler((desktop, httpPort, testWhisper) =>
{
    SetProcessDPIAware();

    var captureService = new ScreenCaptureService(desktop);
    captureService.InitializeMonitors();
    DesktopUseTools.SetCaptureService(captureService);

    var audioCaptureService = new AudioCaptureService();
    DesktopUseTools.SetAudioCaptureService(audioCaptureService);

    var whisperService = new WhisperTranscriptionService();
    DesktopUseTools.SetWhisperService(whisperService);

    var inputService = new InputService();
    DesktopUseTools.SetInputService(inputService);

    if (testWhisper)
    {
        Console.Error.WriteLine("[TEST] Testing Whisper transcription...");
        Console.Error.WriteLine("[TEST] Please play audio on YouTube! Starting in 3 seconds...");
        Thread.Sleep(3000);

        try
        {
            var result = DesktopUseTools.Listen(
                source: "system",
                duration: 30,
                language: "ja",
                modelSize: "small",
                translate: false);

            Console.Error.WriteLine($"[TEST] ========================================");
            Console.Error.WriteLine($"[TEST] 検出言語: {result.Language}");
            Console.Error.WriteLine($"[TEST] セグメント数: {result.Segments.Count}");
            Console.Error.WriteLine($"[TEST] 合計時間: {result.Duration.TotalSeconds:F2}秒");
            Console.Error.WriteLine($"[TEST] ========================================");
            Console.Error.WriteLine($"[TEST] 【文字起こし結果】");

            int i = 1;
            foreach (var seg in result.Segments)
            {
                var timeStr = seg.Start.ToString(@"mm\:ss");
                Console.Error.WriteLine($"[TEST] [{i:D2} {timeStr}] {seg.Text}");
                i++;
            }
            Console.Error.WriteLine($"[TEST] ========================================");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TEST] ERROR: {ex.GetType().Name}");
            Console.Error.WriteLine($"[TEST] Message: {ex.Message}");
            Console.Error.WriteLine($"[TEST] Stack: {ex.StackTrace}");
        }

        return;
    }

    Console.Error.WriteLine("[Stdio] Windows Desktop Use MCP Server started in stdio mode");

    if (httpPort > 0)
    {
        _ = StartHttpServer(captureService, httpPort);
        Console.Error.WriteLine($"[HTTP] Frame streaming server started on http://localhost:{httpPort}");
        Console.Error.WriteLine($"[HTTP] Endpoint: http://localhost:{httpPort}/frame/{{sessionId}}");
    }

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(new StderrLoggerProvider());
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(typeof(DesktopUseTools).Assembly);

    var host = builder.Build();
    host.Run();
}, desktopOption, httpPortOption, testOption);

await rootCmd.InvokeAsync(args).ConfigureAwait(false);

static async Task StartHttpServer(ScreenCaptureService captureService, int port)
{
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.Services.AddSingleton(captureService);

    var app = builder.Build();

    app.MapGet("/frame/{sessionId}", (string sessionId, ScreenCaptureService svc) =>
    {
        if (!svc.TryGetSession(sessionId, out var session) || session == null)
        {
            return Results.NotFound(new { error = "Session not found" });
        }

        var frameData = session.LatestFrame;
        if (string.IsNullOrEmpty(frameData))
        {
            return Results.NotFound(new { error = "No frame captured yet" });
        }

        try
        {
            var imageBytes = Convert.FromBase64String(frameData);
            return Results.Bytes(imageBytes, "image/jpeg");
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to decode image: {ex.Message}");
        }
    });

    app.MapGet("/frame/{sessionId}/info", (string sessionId, ScreenCaptureService svc) =>
    {
        if (!svc.TryGetSession(sessionId, out var session) || session == null)
        {
            return Results.NotFound(new { error = "Session not found" });
        }

        return Results.Ok(new
        {
            sessionId = sessionId,
            hasFrame = !string.IsNullOrEmpty(session.LatestFrame),
            hash = session.LastFrameHash,
            captureTime = session.LastCaptureTime.ToString("O"),
            targetType = session.TargetType,
            interval = session.Interval
        });
    });

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    app.MapGet("/", () => Results.Ok(new
    {
        message = "Windows Desktop Use MCP HTTP Server",
        endpoints = new
        {
            frame = "/frame/{sessionId} - Get latest frame as JPEG image",
            frameInfo = "/frame/{sessionId}/info - Get frame metadata (hash, timestamp)",
            health = "/health - Health check"
        },
        usage = "Use start_watching tool to create a session, then access /frame/{sessionId}"
    }));

    await app.RunAsync($"http://localhost:{port}").ConfigureAwait(false);
}

public class StderrLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new StderrLogger(categoryName);
    public void Dispose() { }
}

public class StderrLogger : ILogger
{
    private readonly string _category;
    public StderrLogger(string category) => _category = category;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var message = formatter(state, exception);
        Console.Error.WriteLine($"[{logLevel}] {_category}: {message}");
    }
}

public class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new NullScope();
    public void Dispose() { }
}
