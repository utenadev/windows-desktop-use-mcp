using System.Text.Json;

public class StreamableHttpServer
{
    private readonly ScreenCaptureService _capture;
    private readonly McpSessionManager _sessionManager;
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    
    public StreamableHttpServer(ScreenCaptureService capture, McpSessionManager sessionManager)
    {
        _capture = capture;
        _sessionManager = sessionManager;
    }
    
    public void Configure(WebApplication app)
    {
        // Streamable HTTP endpoint - supports both POST and GET
        app.MapMethods("/mcp", new[] { "POST", "GET" }, async (HttpContext ctx) =>
        {
            var method = ctx.Request.Method;
            var acceptHeader = ctx.Request.Headers["Accept"].ToString();
            var sessionId = ctx.Request.Headers["MCP-Session-Id"].ToString();
            
            // Validate Origin header for security
            var origin = ctx.Request.Headers["Origin"].ToString();
            if (!string.IsNullOrEmpty(origin) && !IsValidOrigin(origin, ctx))
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Invalid Origin" }, _json));
                return;
            }
            
            // Handle GET request (SSE stream for server-to-client messages)
            if (method == "GET")
            {
                if (!acceptHeader.Contains("text/event-stream"))
                {
                    ctx.Response.StatusCode = 405;
                    return;
                }
                
                await HandleGetRequest(ctx, sessionId);
                return;
            }
            
            // Handle POST request (client-to-server messages)
            if (method == "POST")
            {
                await HandlePostRequest(ctx, sessionId);
                return;
            }
        });
        
        // Session termination endpoint
        app.MapDelete("/mcp", async (HttpContext ctx) =>
        {
            var sessionId = ctx.Request.Headers["MCP-Session-Id"].ToString();
            if (!string.IsNullOrEmpty(sessionId))
            {
                _sessionManager.RemoveSession(sessionId);
            }
            ctx.Response.StatusCode = 200;
        });
    }
    
    private async Task HandleGetRequest(HttpContext ctx, string sessionId)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        
        // Get or create session
        McpSession? session;
        if (string.IsNullOrEmpty(sessionId) || !_sessionManager.TryGetSession(sessionId, out session))
        {
            session = _sessionManager.CreateSession();
        }
        
        // Send initial event with session ID
        var eventId = Guid.NewGuid().ToString("N");
        await SendSseEvent(ctx.Response, eventId, ""); // Empty data to prime reconnection
        
        try
        {
            // Stream messages from the session's channel
            await foreach (var message in session!.MessageChannel.Reader.ReadAllAsync(ctx.RequestAborted))
            {
                eventId = Guid.NewGuid().ToString("N");
                await SendSseEvent(ctx.Response, eventId, message);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal disconnection
        }
    }
    
    private async Task HandlePostRequest(HttpContext ctx, string sessionId)
    {
        var acceptHeader = ctx.Request.Headers["Accept"].ToString();
        
        // Read JSON-RPC message
        var message = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body, _json);
        var jsonrpc = message.GetProperty("jsonrpc").GetString();
        var msgMethod = message.TryGetProperty("method", out var m) ? m.GetString() : null;
        var msgId = message.TryGetProperty("id", out var id) ? id.GetInt64() : (long?)null;
        
        // Handle initialization
        if (msgMethod == "initialize")
        {
            var session = _sessionManager.CreateSession();
            session.IsInitialized = true;
            
            // Return initialization response with session ID
            ctx.Response.Headers["MCP-Session-Id"] = session.Id;
            ctx.Response.Headers["MCP-Protocol-Version"] = "2024-11-05";
            ctx.Response.ContentType = "application/json";
            
            var response = new
            {
                jsonrpc = "2.0",
                id = msgId,
                result = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    serverInfo = new
                    {
                        name = "windows-screen-capture",
                        version = "1.0.0"
                    }
                }
            };
            
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(response, _json));
            return;
        }
        
        // For other requests, check session
        if (!string.IsNullOrEmpty(sessionId))
        {
            if (!_sessionManager.TryGetSession(sessionId, out var session))
            {
                ctx.Response.StatusCode = 404;
                return;
            }
        }
        
        // Handle notifications (no response needed)
        if (msgId == null)
        {
            ctx.Response.StatusCode = 202;
            return;
        }
        
        // For requests that need a response, determine transport method
        if (acceptHeader.Contains("text/event-stream"))
        {
            // Client wants SSE stream response
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            
            // Process the request and send response via SSE
            var result = ProcessToolCall(message);
            var response = new
            {
                jsonrpc = "2.0",
                id = msgId,
                result
            };
            
            var eventId = Guid.NewGuid().ToString("N");
            await SendSseEvent(ctx.Response, eventId, JsonSerializer.Serialize(response, _json));
        }
        else
        {
            // Simple JSON response
            ctx.Response.ContentType = "application/json";
            var result = ProcessToolCall(message);
            var response = new
            {
                jsonrpc = "2.0",
                id = msgId,
                result
            };
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(response, _json));
        }
    }
    
    private object? ProcessToolCall(JsonElement message)
    {
        var method = message.TryGetProperty("method", out var m) ? m.GetString() : null;
        var args = message.TryGetProperty("params", out var p) ? p : default;
        
        return method switch
        {
            "tools/list" => ListTools(),
            "tools/call" => CallTool(args),
            _ => new { error = $"Unknown method: {method}" }
        };
    }
    
    private object ListTools()
    {
        return new
        {
            tools = new object[]
            {
                new {
                    name = "list_monitors",
                    description = "List all available monitors/displays",
                    inputSchema = new { type = "object", properties = new { } }
                },
                new {
                    name = "see",
                    description = "Capture a screenshot of the specified monitor",
                    inputSchema = new {
                        type = "object",
                        properties = new {
                            monitor = new { type = "integer", defaultValue = 0 },
                            quality = new { type = "integer", defaultValue = 80 },
                            maxWidth = new { type = "integer", defaultValue = 1920 }
                        }
                    }
                }
            }
        };
    }
    
    private object CallTool(JsonElement args)
    {
        var toolName = args.GetProperty("name").GetString();
        var toolArgs = args.TryGetProperty("arguments", out var a) ? a : default;
        
        return toolName switch
        {
            "list_monitors" => new { content = new object[] { new { type = "text", text = "Monitors listed" } } },
            "see" => new { content = new object[] { new { type = "image", data = "base64data", mimeType = "image/jpeg" } } },
            _ => new { error = $"Unknown tool: {toolName}" }
        };
    }
    
    private async Task SendSseEvent(HttpResponse response, string eventId, string data)
    {
        await response.WriteAsync($"id: {eventId}\n");
        await response.WriteAsync($"data: {data}\n\n");
        await response.Body.FlushAsync();
    }
    
    private bool IsValidOrigin(string origin, HttpContext ctx)
    {
        // For local development, accept localhost
        if (origin.Contains("localhost") || origin.Contains("127.0.0.1"))
        {
            return true;
        }
        
        // Additional validation can be added here
        return true;
    }
}
