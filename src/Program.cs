using System.CommandLine;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;

var ipOption = new Option<string>(
    name: "--ip_addr",
    description: "IP address to bind (0.0.0.0 for WSL2, 127.0.0.1 for local only)",
    getDefaultValue: () => "127.0.0.1");

var portOption = new Option<int>(
    name: "--port",
    description: "Port number",
    getDefaultValue: () => 5000);

var desktopOption = new Option<uint>(
    name: "--desktopNum",
    description: "Default monitor index (0=primary)",
    getDefaultValue: () => 0u);

var rootCmd = new RootCommand("MCP Windows Screen Capture Server");
rootCmd.AddOption(ipOption);
rootCmd.AddOption(portOption);
rootCmd.AddOption(desktopOption);

rootCmd.SetHandler((ip, port, desktop) => {
    var builder = WebApplication.CreateBuilder();
    builder.Services.AddSingleton<ScreenCaptureService>(sp => new ScreenCaptureService(desktop));
    builder.WebHost.ConfigureKestrel(options => {
        options.Listen(IPAddress.Parse(ip), port);
    });
    
    var app = builder.Build();
    var captureService = app.Services.GetRequiredService<ScreenCaptureService>();
    captureService.InitializeMonitors();
    var mcp = new McpServer(captureService);
    mcp.Configure(app);
    
    Console.WriteLine($"[Server] Started on http://{ip}:{port}");
    Console.WriteLine($"[Server] Default monitor: {desktop}");
    if (ip == "0.0.0.0") {
        Console.WriteLine($"[Server] WSL2 URL: http://$(ip route | grep default | awk '{{print $3}}'):{port}/sse");
    }
    
    app.Run();
}, ipOption, portOption, desktopOption);

await rootCmd.InvokeAsync(args);

public class McpServer {
    private readonly ScreenCaptureService _capture;
    private readonly Dictionary<string, HttpResponse> _clients = new();
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public McpServer(ScreenCaptureService capture) => _capture = capture;

    public void Configure(WebApplication app) {
        app.MapGet("/sse", async (HttpContext ctx) => {
            ctx.Response.ContentType = "text/event-stream";
            var clientId = Guid.NewGuid().ToString();
            _clients[clientId] = ctx.Response;
            
            await SendEvent(ctx.Response, "endpoint", new { uri = $"/message?clientId={clientId}" });
            
            try {
                while (!ctx.RequestAborted.IsCancellationRequested) {
                    await Task.Delay(30000, ctx.RequestAborted);
                    await SendEvent(ctx.Response, "ping", new { time = DateTime.UtcNow });
                }
            } finally { _clients.Remove(clientId); }
        });

        app.MapPost("/message", async (HttpContext ctx) => {
            var req = await JsonSerializer.DeserializeAsync<McpRequest>(ctx.Request.Body, _json);
            if (req == null) return Results.BadRequest();
            
            var clientId = ctx.Request.Query["clientId"].ToString();
            var result = HandleToolCall(req.Params);
            
            if (req.Id.HasValue && _clients.TryGetValue(clientId, out var resp)) {
                await SendEvent(resp, "message", new McpResponse(req.Id.Value, result));
            }
            return Results.Accepted();
        });

        app.MapGet("/stream/{id}", async (string id, HttpContext ctx) => {
            if (!_capture.TryGetSession(id, out var session)) return Results.NotFound();
            
            ctx.Response.ContentType = "text/event-stream";
            await foreach (var img in session.Channel.Reader.ReadAllAsync(ctx.RequestAborted)) {
                await SendEvent(ctx.Response, "image", new { timestamp = DateTime.UtcNow.ToString("o"), data = $"data:image/jpeg;base64,{img}" });
            }
            return Results.Empty;
        });
    }

    private object? HandleToolCall(JsonElement? paramsEl) {
        if (paramsEl == null) return null;
        var method = paramsEl.Value.GetProperty("name").GetString();
        var args = paramsEl.Value.TryGetProperty("arguments", out var a) ? a : default;

        return method switch {
            "list_monitors" => ListMonitors(),
            "see" => See(args),
            "start_watching" => StartWatching(args),
            "stop_watching" => StopWatching(args),
            "tools/list" => ListTools(),
            _ => new { error = $"Unknown: {method}" }
        };
    }

    private object ListTools() {
        return new { 
            content = new[] { 
                new { 
                    type = "text", 
                    text = JsonSerializer.Serialize(new[] {
                        new {
                            name = "list_monitors",
                            description = "List all available monitors/displays with their index, name, resolution, and position",
                            inputSchema = new { type = "object", properties = new { }, required = new string[] { } }
                        },
                        new {
                            name = "see",
                            description = "Capture a screenshot of the specified monitor (like taking a photo with your eyes). Returns the captured image as base64 JPEG.",
                            inputSchema = new { 
                                type = "object", 
                                properties = new { 
                                    monitor = new { type = "integer", description = "Monitor index (0=primary, 1=secondary, etc.)", defaultValue = 0, minimum = 0 },
                                    quality = new { type = "integer", description = "JPEG quality (1-100, higher=better quality but larger size)", defaultValue = 80, minimum = 1, maximum = 100 },
                                    maxWidth = new { type = "integer", description = "Maximum width in pixels (image will be resized if larger)", defaultValue = 1920, minimum = 100 }
                                }, 
                                required = new string[] { } 
                            }
                        },
                        new {
                            name = "start_watching",
                            description = "Start a continuous screen capture stream (like watching a live video). Returns a session ID and stream URL.",
                            inputSchema = new { 
                                type = "object", 
                                properties = new { 
                                    monitor = new { type = "integer", description = "Monitor index to watch", defaultValue = 0, minimum = 0 },
                                    intervalMs = new { type = "integer", description = "Capture interval in milliseconds (1000=1 second)", defaultValue = 1000, minimum = 100 },
                                    quality = new { type = "integer", description = "JPEG quality (1-100)", defaultValue = 80, minimum = 1, maximum = 100 },
                                    maxWidth = new { type = "integer", description = "Maximum width in pixels", defaultValue = 1920, minimum = 100 }
                                }, 
                                required = new string[] { } 
                            }
                        },
                        new {
                            name = "stop_watching",
                            description = "Stop a running screen capture stream by session ID",
                            inputSchema = new { 
                                type = "object", 
                                properties = new { 
                                    sessionId = new { type = "string", description = "The session ID returned by start_watching" }
                                }, 
                                required = new[] { "sessionId" } 
                            }
                        }
                    })
                } 
            } 
        };
    }

    private object ListMonitors() {
        try {
            var monitors = _capture.GetMonitors();
            return new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(monitors) } } };
        } catch (Exception ex) {
            return new { error = $"Failed to list monitors: {ex.Message}" };
        }
    }

    private object See(JsonElement args) {
        try {
            var mon = args.TryGetProperty("monitor", out var m) ? m.GetUInt32() : 0u;
            var qual = args.TryGetProperty("quality", out var q) ? q.GetInt32() : 80;
            var maxW = args.TryGetProperty("maxWidth", out var w) ? w.GetInt32() : 1920;
            var data = _capture.CaptureSingle(mon, maxW, qual);
            return new { content = new object[] { new { type = "image", data, mimeType = "image/jpeg" }, new { type = "text", text = $"Captured monitor {mon} at {DateTime.Now:HH:mm:ss}" } } };
        } catch (Exception ex) {
            return new { error = $"Failed to capture screen: {ex.Message}" };
        }
    }

    private object StartWatching(JsonElement args) {
        try {
            var mon = args.TryGetProperty("monitor", out var m) ? m.GetUInt32() : 0u;
            var interval = args.TryGetProperty("intervalMs", out var i) ? i.GetInt32() : 1000;
            var qual = args.TryGetProperty("quality", out var q) ? q.GetInt32() : 80;
            var maxW = args.TryGetProperty("maxWidth", out var w) ? w.GetInt32() : 1920;
            var id = _capture.StartStream(mon, interval, qual, maxW);
            return new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { sessionId = id, streamUrl = $"/stream/{id}" }) } } };
        } catch (Exception ex) {
            return new { error = $"Failed to start watching: {ex.Message}" };
        }
    }

    private object StopWatching(JsonElement args) {
        try {
            var id = args.GetProperty("sessionId").GetString()!;
            _capture.StopStream(id);
            return new { content = new[] { new { type = "text", text = "Stopped watching" } } };
        } catch (Exception ex) {
            return new { error = $"Failed to stop watching: {ex.Message}" };
        }
    }

    private async Task SendEvent(HttpResponse r, string evt, object data) {
        await r.WriteAsync($"event: {evt}\ndata: {JsonSerializer.Serialize(data, _json)}\n\n");
        await r.Body.FlushAsync();
    }
}

public record McpRequest(string Method, JsonElement? Params, long? Id);
public record McpResponse(long Id, object? Result);
