using System.CommandLine;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

[DllImport("user32.dll")] static extern bool SetProcessDPIAware();

var desktopOption = new Option<uint>(
    name: "--desktopNum",
    description: "Default monitor index (0=primary)",
    getDefaultValue: () => 0u);

var httpPortOption = new Option<int>(
    name: "--httpPort",
    description: "HTTP server port for frame streaming (0=disable)",
    getDefaultValue: () => 5000);

var rootCmd = new RootCommand("MCP Windows Screen Capture Server");
rootCmd.AddOption(desktopOption);
rootCmd.AddOption(httpPortOption);

rootCmd.SetHandler((desktop, httpPort) => {
    SetProcessDPIAware();
    
    var captureService = new ScreenCaptureService(desktop);
    captureService.InitializeMonitors();
    ScreenCaptureTools.SetCaptureService(captureService);
    
    // Initialize audio capture service
    var audioCaptureService = new AudioCaptureService();
    ScreenCaptureTools.SetAudioCaptureService(audioCaptureService);
    
    Console.Error.WriteLine("[Stdio] MCP Windows Screen Capture Server started in stdio mode");
    
    // Start HTTP server for frame streaming if port is specified
    if (httpPort > 0) {
        _ = StartHttpServer(captureService, httpPort);
        Console.Error.WriteLine($"[HTTP] Frame streaming server started on http://localhost:{httpPort}");
        Console.Error.WriteLine($"[HTTP] Endpoint: http://localhost:{httpPort}/frame/{{sessionId}}");
    }
    
    var builder = Host.CreateApplicationBuilder();
    // Disable logging to stdout for MCP stdio protocol compliance
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(new StderrLoggerProvider());
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(typeof(ScreenCaptureTools).Assembly);
    
    var host = builder.Build();
    host.Run();
}, desktopOption, httpPortOption);

await rootCmd.InvokeAsync(args);

static async Task StartHttpServer(ScreenCaptureService captureService, int port) {
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.Services.AddSingleton(captureService);
    
    var app = builder.Build();
    
    // Endpoint to get latest frame as JPEG image
    app.MapGet("/frame/{sessionId}", (string sessionId, ScreenCaptureService svc) => {
        if (!svc.TryGetSession(sessionId, out var session) || session == null) {
            return Results.NotFound(new { error = "Session not found" });
        }
        
        var frameData = session.LatestFrame;
        if (string.IsNullOrEmpty(frameData)) {
            return Results.NotFound(new { error = "No frame captured yet" });
        }
        
        try {
            // Convert base64 to binary
            var imageBytes = Convert.FromBase64String(frameData);
            return Results.Bytes(imageBytes, "image/jpeg");
        } catch (Exception ex) {
            return Results.Problem($"Failed to decode image: {ex.Message}");
        }
    });
    
    // Endpoint to get frame info (hash, timestamp) without image data
    app.MapGet("/frame/{sessionId}/info", (string sessionId, ScreenCaptureService svc) => {
        if (!svc.TryGetSession(sessionId, out var session) || session == null) {
            return Results.NotFound(new { error = "Session not found" });
        }
        
        return Results.Ok(new {
            sessionId = sessionId,
            hasFrame = !string.IsNullOrEmpty(session.LatestFrame),
            hash = session.LastFrameHash,
            captureTime = session.LastCaptureTime.ToString("O"),
            targetType = session.TargetType,
            interval = session.Interval
        });
    });
    
    // Health check
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    
    // Root endpoint with usage info
    app.MapGet("/", () => Results.Ok(new {
        message = "MCP Screen Capture HTTP Server",
        endpoints = new {
            frame = "/frame/{sessionId} - Get latest frame as JPEG image",
            frameInfo = "/frame/{sessionId}/info - Get frame metadata (hash, timestamp)",
            health = "/health - Health check"
        },
        usage = "Use start_watching tool to create a session, then access /frame/{sessionId}"
    }));
    
    await app.RunAsync($"http://localhost:{port}");
}

// Custom logger that writes to stderr to avoid polluting stdout (MCP stdio protocol)
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
