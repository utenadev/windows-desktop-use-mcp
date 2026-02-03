using System.CommandLine;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

[DllImport("user32.dll")] static extern bool SetProcessDPIAware();

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

var httpOption = new Option<bool>(
    name: "--http",
    description: "Run in HTTP mode (requires ip/port options). Default is stdio mode.",
    getDefaultValue: () => false);

var rootCmd = new RootCommand("MCP Windows Screen Capture Server");
rootCmd.AddOption(ipOption);
rootCmd.AddOption(portOption);
rootCmd.AddOption(desktopOption);
rootCmd.AddOption(httpOption);

rootCmd.SetHandler((ip, port, desktop, useHttp) => {
    SetProcessDPIAware();
    
    var captureService = new ScreenCaptureService(desktop);
    captureService.InitializeMonitors();
    
    if (useHttp) {
        RunHttpMode(ip, port, desktop, captureService);
    } else {
        RunStdioMode(captureService);
    }
}, ipOption, portOption, desktopOption, httpOption);

await rootCmd.InvokeAsync(args);

void RunStdioMode(ScreenCaptureService captureService) {
    Console.Error.WriteLine("[Stdio] MCP Windows Screen Capture Server started in stdio mode");
    Console.Error.WriteLine($"[Stdio] Default monitor: {captureService.GetMonitors().FirstOrDefault()?.Idx ?? 0}");
    
    var server = new StdioMcpServer(captureService);
    server.Run().Wait();
}

void RunHttpMode(string ip, int port, uint desktop, ScreenCaptureService captureService) {
    var builder = WebApplication.CreateBuilder();
    builder.Services.AddSingleton<ScreenCaptureService>(sp => captureService);
    builder.WebHost.ConfigureKestrel(options => {
        options.Listen(IPAddress.Parse(ip), port);
    });
    
    var app = builder.Build();
    var service = app.Services.GetRequiredService<ScreenCaptureService>();
    
    // Legacy SSE transport (for backward compatibility with QwenCode)
    var mcp = new McpServer(service);
    mcp.Configure(app);
    
    // New Streamable HTTP transport (for Claude Code, Gemini CLI, OpenCode)
    var sessionManager = new McpSessionManager();
    var streamableHttp = new StreamableHttpServer(service, sessionManager);
    streamableHttp.Configure(app);
    
    // Register cleanup on application shutdown
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStopping.Register(() => {
        Console.WriteLine("[Server] Shutting down...");
        service.StopAllStreams();
        mcp.StopAllClients();
        Console.WriteLine("[Server] Cleanup completed");
    });
    
    // Handle Ctrl+C gracefully
    Console.CancelKeyPress += (sender, e) => {
        e.Cancel = true;
        Console.WriteLine("[Server] Ctrl+C pressed, initiating shutdown...");
        lifetime.StopApplication();
    };
    
    Console.WriteLine($"[Server] Started on http://{ip}:{port}");
    Console.WriteLine($"[Server] Default monitor: {desktop}");
    Console.WriteLine($"[Server] Legacy SSE endpoint: http://{ip}:{port}/sse (for QwenCode)");
    Console.WriteLine($"[Server] Streamable HTTP endpoint: http://{ip}:{port}/mcp (for Claude Code, Gemini CLI, OpenCode)");
    if (ip == "0.0.0.0") {
        Console.WriteLine($"[Server] WSL2 SSE URL: http://$(ip route | grep default | awk '{{print $3}}'):{port}/sse");
        Console.WriteLine($"[Server] WSL2 HTTP URL: http://$(ip route | grep default | awk '{{print $3}}'):{port}/mcp");
    }
    Console.WriteLine("[Server] Press Ctrl+C to stop");
    
    app.Run();
}

public class StdioMcpServer {
    private readonly ScreenCaptureService _capture;
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private int _requestId = 0;

    public StdioMcpServer(ScreenCaptureService capture) => _capture = capture;

    public async Task Run() {
        try {
            while (true) {
                var line = await Console.In.ReadLineAsync();
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                try {
                    var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, _json);
                    if (request == null) continue;
                    
                    var response = HandleRequest(request);
                    if (response != null) {
                        var responseJson = JsonSerializer.Serialize(response, _json);
                        await Console.Out.WriteLineAsync(responseJson);
                        await Console.Out.FlushAsync();
                    }
                } catch (JsonException ex) {
                    var errorResponse = new JsonRpcErrorResponse(
                        new JsonRpcId(_requestId++),
                        new JsonRpcError(-32700, $"Parse error: {ex.Message}", null)
                    );
                    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(errorResponse, _json));
                    await Console.Out.FlushAsync();
                }
            }
        } catch (OperationCanceledException) {
            // Normal shutdown
        } catch (Exception ex) {
            Console.Error.WriteLine($"[Stdio] Fatal error: {ex.Message}");
        }
    }

    private JsonRpcResponse? HandleRequest(JsonRpcRequest request) {
        var id = request.Id ?? new JsonRpcId(_requestId++);
        
        return request.Method switch {
            "initialize" => HandleInitialize(request, id),
            "initialized" => null, // Notification, no response
            "tools/list" => HandleToolsList(request, id),
            "tools/call" => HandleToolsCall(request, id),
            _ => new JsonRpcResponse(id, null, new JsonRpcError(-32601, $"Method not found: {request.Method}", null))
        };
    }

    private JsonRpcResponse HandleInitialize(JsonRpcRequest request, JsonRpcId id) {
        var result = new {
            protocolVersion = "2024-11-05",
            capabilities = new {
                tools = new { }
            },
            serverInfo = new {
                name = "windows-screen-capture",
                version = "1.0.0"
            }
        };
        return new JsonRpcResponse(id, result, null);
    }

    private JsonRpcResponse HandleToolsList(JsonRpcRequest request, JsonRpcId id) {
        var tools = new object[] {
            new {
                name = "list_monitors",
                description = "List all available monitors/displays with their index, name, resolution, and position",
                inputSchema = new { type = "object", properties = new { }, required = new string[] { } }
            },
            new {
                name = "list_windows",
                description = "List all visible windows with their handles, titles, and dimensions",
                inputSchema = new { type = "object", properties = new { }, required = new string[] { } }
            },
new {
                name = "see",
                description = "Capture a screenshot of the specified monitor or window (like taking a photo with your eyes). Returns the captured image as base64 JPEG.",
                inputSchema = new {
                    type = "object",
                    properties = new {
                        targetType = new { type = "string", description = "Target type: 'monitor' or 'window'", defaultValue = "monitor", enum_ = new[] { "monitor", "window" } },
                        monitor = new { type = "integer", description = "Monitor index (0=primary, 1=secondary, etc.) - used when targetType='monitor'", defaultValue = 0, minimum = 0 },
                        hwnd = new { type = "integer", description = "Window handle (HWND) - used when targetType='window'" },
                        quality = new { type = "integer", description = "JPEG quality (1-100, higher=better quality but larger size)", defaultValue = 80, minimum = 1, maximum = 100 },
                        maxWidth = new { type = "integer", description = "Maximum width in pixels (image will be resized if larger)", defaultValue = 1920, minimum = 100 }
                    },
                    required = new string[] { }
                }
            },
            new {
                name = "capture_window",
                description = "Capture a screenshot of a specific window by its handle (HWND). Returns the captured image as base64 JPEG.",
                inputSchema = new {
                    type = "object",
                    properties = new {
                        hwnd = new { type = "integer", description = "Window handle (HWND) to capture" },
                        quality = new { type = "integer", description = "JPEG quality (1-100)", defaultValue = 80, minimum = 1, maximum = 100 },
                        maxWidth = new { type = "integer", description = "Maximum width in pixels", defaultValue = 1920, minimum = 100 }
                    },
                    required = new[] { "hwnd" }
                }
            },
            new {
                name = "capture_region",
                description = "Capture a screenshot of a specific screen region. Returns the captured image as base64 JPEG.",
                inputSchema = new {
                    type = "object",
                    properties = new {
                        x = new { type = "integer", description = "X coordinate of the region" },
                        y = new { type = "integer", description = "Y coordinate of the region" },
                        w = new { type = "integer", description = "Width of the region" },
                        h = new { type = "integer", description = "Height of the region" },
                        quality = new { type = "integer", description = "JPEG quality (1-100)", defaultValue = 80, minimum = 1, maximum = 100 },
                        maxWidth = new { type = "integer", description = "Maximum width in pixels", defaultValue = 1920, minimum = 100 }
                    },
                    required = new[] { "x", "y", "w", "h" }
                }
            },
new {
                name = "start_watching",
                description = "Start a continuous screen capture stream for a monitor or window (like watching a live video). Returns a session ID and stream URL.",
                inputSchema = new {
                    type = "object",
                    properties = new {
                        targetType = new { type = "string", description = "Target type: 'monitor' or 'window'", defaultValue = "monitor", enum_ = new[] { "monitor", "window" } },
                        monitor = new { type = "integer", description = "Monitor index to watch - used when targetType='monitor'", defaultValue = 0, minimum = 0 },
                        hwnd = new { type = "integer", description = "Window handle (HWND) to watch - used when targetType='window'" },
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
        };
        
        var result = new { tools };
        return new JsonRpcResponse(id, result, null);
    }

    private JsonRpcResponse HandleToolsCall(JsonRpcRequest request, JsonRpcId id) {
        if (request.Params == null) {
            return new JsonRpcResponse(id, null, new JsonRpcError(-32602, "Invalid params: missing params", null));
        }

        var paramsEl = request.Params.Value;
        
        if (!paramsEl.TryGetProperty("name", out var nameEl)) {
            return new JsonRpcResponse(id, null, new JsonRpcError(-32602, "Invalid params: missing tool name", null));
        }

        var toolName = nameEl.GetString();
        var args = paramsEl.TryGetProperty("arguments", out var argsEl) ? argsEl : default;

        object? result;
        try {
            result = toolName switch {
                "list_monitors" => HandleListMonitors(),
                "list_windows" => HandleListWindows(),
                "see" => HandleSee(args),
                "capture_window" => HandleCaptureWindow(args),
                "capture_region" => HandleCaptureRegion(args),
                "start_watching" => HandleStartWatching(args),
                "stop_watching" => HandleStopWatching(args),
                _ => throw new ArgumentException($"Unknown tool: {toolName}")
            };
        } catch (Exception ex) {
            return new JsonRpcResponse(id, null, new JsonRpcError(-32603, $"Tool execution error: {ex.Message}", null));
        }

        return new JsonRpcResponse(id, result, null);
    }

    private object HandleListMonitors() {
        var monitors = _capture.GetMonitors();
        var content = new object[] {
            new { type = "text", text = JsonSerializer.Serialize(monitors, _json) }
        };
        return new { content, isError = false };
    }

    private object HandleListWindows() {
        var windows = _capture.GetWindows();
        var content = new object[] {
            new { type = "text", text = JsonSerializer.Serialize(windows, _json) }
        };
        return new { content, isError = false };
    }

    private object HandleSee(JsonElement args) {
        var targetType = args.TryGetProperty("targetType", out var tt) ? tt.GetString() : "monitor";
        var qual = args.TryGetProperty("quality", out var q) ? q.GetInt32() : 80;
        var maxW = args.TryGetProperty("maxWidth", out var w) ? w.GetInt32() : 1920;
        
        string data;
        string message;
        
        if (targetType == "window") {
            var hwnd = args.TryGetProperty("hwnd", out var h) ? h.GetInt64() : 0L;
            data = _capture.CaptureWindow(hwnd, maxW, qual);
            message = $"Captured window {hwnd} at {DateTime.Now:HH:mm:ss}";
        } else {
            var mon = args.TryGetProperty("monitor", out var m) ? m.GetUInt32() : 0u;
            data = _capture.CaptureSingle(mon, maxW, qual);
            message = $"Captured monitor {mon} at {DateTime.Now:HH:mm:ss}";
        }
        
        var content = new object[] {
            new { type = "image", data, mimeType = "image/jpeg" },
            new { type = "text", text = message }
        };
        return new { content, isError = false };
    }

    private object HandleCaptureWindow(JsonElement args) {
        var hwnd = args.TryGetProperty("hwnd", out var h) ? h.GetInt64() : 0L;
        var qual = args.TryGetProperty("quality", out var q) ? q.GetInt32() : 80;
        var maxW = args.TryGetProperty("maxWidth", out var w) ? w.GetInt32() : 1920;
        var data = _capture.CaptureWindow(hwnd, maxW, qual);
        var content = new object[] {
            new { type = "image", data, mimeType = "image/jpeg" },
            new { type = "text", text = $"Captured window {hwnd} at {DateTime.Now:HH:mm:ss}" }
        };
        return new { content, isError = false };
    }

    private object HandleCaptureRegion(JsonElement args) {
        var x = args.TryGetProperty("x", out var xVal) ? xVal.GetInt32() : 0;
        var y = args.TryGetProperty("y", out var yVal) ? yVal.GetInt32() : 0;
        var w = args.TryGetProperty("w", out var wVal) ? wVal.GetInt32() : 1920;
        var h = args.TryGetProperty("h", out var hVal) ? hVal.GetInt32() : 1080;
        var qual = args.TryGetProperty("quality", out var q) ? q.GetInt32() : 80;
        var maxW = args.TryGetProperty("maxWidth", out var max) ? max.GetInt32() : 1920;
        var data = _capture.CaptureRegion(x, y, w, h, maxW, qual);
        var content = new object[] {
            new { type = "image", data, mimeType = "image/jpeg" },
            new { type = "text", text = $"Captured region at ({x}, {y}) size {w}x{h} at {DateTime.Now:HH:mm:ss}" }
        };
        return new { content, isError = false };
    }

    private object HandleStartWatching(JsonElement args) {
        var targetType = args.TryGetProperty("targetType", out var tt) ? tt.GetString() : "monitor";
        var interval = args.TryGetProperty("intervalMs", out var i) ? i.GetInt32() : 1000;
        var qual = args.TryGetProperty("quality", out var q) ? q.GetInt32() : 80;
        var maxW = args.TryGetProperty("maxWidth", out var w) ? w.GetInt32() : 1920;
        
        string id;
        if (targetType == "window") {
            var hwnd = args.TryGetProperty("hwnd", out var h) ? h.GetInt64() : 0L;
            id = _capture.StartWindowStream(hwnd, interval, qual, maxW);
        } else {
            var mon = args.TryGetProperty("monitor", out var m) ? m.GetUInt32() : 0u;
            id = _capture.StartStream(mon, interval, qual, maxW);
        }
        
        var text = JsonSerializer.Serialize(new { sessionId = id });
        var content = new object[] {
            new { type = "text", text }
        };
        return new { content, isError = false };
    }

    private object HandleStopWatching(JsonElement args) {
        var id = args.GetProperty("sessionId").GetString()!;
        _capture.StopStream(id);
        var content = new object[] {
            new { type = "text", text = "Stopped watching" }
        };
        return new { content, isError = false };
    }
}

// JSON-RPC 2.0 types for stdio mode
public record JsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params,
    [property: JsonPropertyName("id")] JsonRpcId? Id
);

public record JsonRpcResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonRpcId Id,
    [property: JsonPropertyName("result")] object? Result,
    [property: JsonPropertyName("error")] JsonRpcError? Error
) {
    public JsonRpcResponse(JsonRpcId id, object? result, JsonRpcError? error) 
        : this("2.0", id, result, error) { }
}

public record JsonRpcErrorResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] JsonRpcId Id,
    [property: JsonPropertyName("error")] JsonRpcError Error
) {
    public JsonRpcErrorResponse(JsonRpcId id, JsonRpcError error) 
        : this("2.0", id, error) { }
}

public record JsonRpcError(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data")] object? Data
);

[JsonConverter(typeof(JsonRpcIdConverter))]
public struct JsonRpcId {
    public int? IntValue { get; }
    public string? StringValue { get; }
    
    public JsonRpcId(int value) { IntValue = value; StringValue = null; }
    public JsonRpcId(string value) { IntValue = null; StringValue = value; }
    
    public override string ToString() => IntValue?.ToString() ?? StringValue ?? "null";
}

public class JsonRpcIdConverter : System.Text.Json.Serialization.JsonConverter<JsonRpcId> {
    public override JsonRpcId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var intVal)) {
            return new JsonRpcId(intVal);
        }
        if (reader.TokenType == JsonTokenType.String) {
            return new JsonRpcId(reader.GetString()!);
        }
        if (reader.TokenType == JsonTokenType.Null) {
            return new JsonRpcId(0);
        }
        throw new JsonException($"Unexpected token type for id: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, JsonRpcId value, JsonSerializerOptions options) {
        if (value.IntValue.HasValue) {
            writer.WriteNumberValue(value.IntValue.Value);
        } else if (value.StringValue != null) {
            writer.WriteStringValue(value.StringValue);
        } else {
            writer.WriteNullValue();
        }
    }
}

public class McpServer {
    private readonly ScreenCaptureService _capture;
    private readonly Dictionary<string, HttpResponse> _clients = new();
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public McpServer(ScreenCaptureService capture) => _capture = capture;

    public void StopAllClients() {
        Console.WriteLine($"[MCP] Disconnecting {_clients.Count} clients...");
        _clients.Clear();
    }

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
            if (!_capture.TryGetSession(id, out var session) || session == null) return Results.NotFound();
            
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
            "list_windows" => ListWindows(),
            "see" => See(args),
            "capture_window" => CaptureWindow(args),
            "capture_region" => CaptureRegion(args),
            "start_watching" => StartWatching(args),
            "stop_watching" => StopWatching(args),
            "tools/list" => ListTools(),
            _ => new { error = $"Unknown: {method}" }
        };
    }

    private object ListTools() {
        return new { 
            content = new object[] { 
                new { 
                    type = "text", 
                    text = JsonSerializer.Serialize(new object[] {
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

    private object ListWindows() {
        try {
            var windows = _capture.GetWindows();
            return new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(windows) } } };
        } catch (Exception ex) {
            return new { error = $"Failed to list windows: {ex.Message}" };
        }
    }

    private object CaptureWindow(JsonElement args) {
        try {
            var hwnd = args.TryGetProperty("hwnd", out var h) ? h.GetInt64() : 0L;
            var qual = args.TryGetProperty("quality", out var q) ? q.GetInt32() : 80;
            var maxW = args.TryGetProperty("maxWidth", out var w) ? w.GetInt32() : 1920;
            var data = _capture.CaptureWindow(hwnd, maxW, qual);
            return new { content = new object[] { new { type = "image", data, mimeType = "image/jpeg" }, new { type = "text", text = $"Captured window {hwnd} at {DateTime.Now:HH:mm:ss}" } } };
        } catch (Exception ex) {
            return new { error = $"Failed to capture window: {ex.Message}" };
        }
    }

    private object CaptureRegion(JsonElement args) {
        try {
            var x = args.TryGetProperty("x", out var xVal) ? xVal.GetInt32() : 0;
            var y = args.TryGetProperty("y", out var yVal) ? yVal.GetInt32() : 0;
            var w = args.TryGetProperty("w", out var wVal) ? wVal.GetInt32() : 1920;
            var h = args.TryGetProperty("h", out var hVal) ? hVal.GetInt32() : 1080;
            var qual = args.TryGetProperty("quality", out var q) ? q.GetInt32() : 80;
            var maxW = args.TryGetProperty("maxWidth", out var max) ? max.GetInt32() : 1920;
            var data = _capture.CaptureRegion(x, y, w, h, maxW, qual);
            return new { content = new object[] { new { type = "image", data, mimeType = "image/jpeg" }, new { type = "text", text = $"Captured region at ({x}, {y}) size {w}x{h} at {DateTime.Now:HH:mm:ss}" } } };
        } catch (Exception ex) {
            return new { error = $"Failed to capture region: {ex.Message}" };
        }
    }

    private async Task SendEvent(HttpResponse r, string evt, object data) {
        await r.WriteAsync($"event: {evt}\ndata: {JsonSerializer.Serialize(data, _json)}\n\n");
        await r.Body.FlushAsync();
    }
}

public record McpRequest(string Method, JsonElement? Params, long? Id);
public record McpResponse(long Id, object? Result);
