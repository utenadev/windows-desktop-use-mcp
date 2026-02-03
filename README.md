# MCP Windows Screen Capture Server

Windows 11 screen capture MCP server with stdio transport as default. Supports optional HTTP mode for legacy clients.

Last updated: 2026-02-04

Build: Trigger CI

> **⚠️ Implementation Note:** This is the **GDI+ version** which works reliably without Direct3D dependencies. If you need high-performance GPU capture, you must complete the Direct3D/Windows Graphics Capture implementation yourself. This GDI+ version is sufficient for most AI assistant use cases.

## Requirements
- Windows 11 (or Windows 10 1809+)
- .NET 8.0 SDK

## Build & Run

```bash
# Build
dotnet build src/WindowsScreenCaptureServer.csproj -c Release

# Run in stdio mode (default - recommended)
dotnet run --project src/WindowsScreenCaptureServer.csproj

# Run in HTTP mode (for legacy clients)
dotnet run --project src/WindowsScreenCaptureServer.csproj -- --http --ip_addr 127.0.0.1 --port 5000

# Or single-file publish
dotnet publish src/WindowsScreenCaptureServer.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

## CLI Options

### Default Mode (stdio)
No flags required - runs as stdio-based MCP server:
```bash
dotnet run --project src/WindowsScreenCaptureServer.csproj
```

### HTTP Mode (Optional)
Enable with `--http` flag for legacy clients or special use cases:
- `--http`: Enable HTTP mode (stdio is default)
- `--ip_addr`: IP to bind (default: `127.0.0.1`, use `0.0.0.0` only for external access)
- `--port`: Port number (default: 5000)
- `--desktopNum`: Default monitor index (0=primary, 1=secondary, etc.)

## Transport Modes

> **Note:** HTTP mode is for backward compatibility and advanced use cases only. The stdio transport is the default and recommended mode for all clients.

This server supports two transport modes:

| Mode | Default | Use Case |
|------|---------|----------|
| **stdio** | ✅ Yes | Recommended for all clients - local, secure, no network exposure |
| **HTTP** | ❌ No | Legacy support, requires `--http` flag |

### HTTP Endpoints (when --http is enabled)

| Endpoint | Transport | Status |
|----------|-----------|--------|
| `/mcp` | Streamable HTTP | Active |
| `/sse` | Legacy SSE | Deprecated |

## Available MCP Tools

### Screen Capture Tools

| Tool | Description |
|------|-------------|
| `list_monitors` | List all available monitors/displays |
| `see` | Capture a screenshot of the specified monitor (like taking a photo with your eyes) |
| `start_watching` | Start a continuous screen capture stream (like watching a live video) |
| `stop_watching` | Stop a running screen capture stream by session ID |

### Window Capture Tools

| Tool | Description |
|------|-------------|
| `list_windows` | List all visible Windows applications (hwnd, title, position, size) |
| `capture_window` | Capture a specific window by its HWND (Window handle) |
| `capture_region` | Capture an arbitrary screen region (x, y, width, height) |

### Tool Examples

Ask Claude:
- "See what's on my screen"
- "Look at monitor 1"
- "List all open windows"
- "Capture the Visual Studio window"
- "Capture a region from (100,100) to (500,500)"
- "Start watching my screen and tell me when something changes"

### Tool Parameter Examples

```json
// List windows
{"method": "list_windows"}

// Capture specific window
{"method": "capture_window", "arguments": {"hwnd": 123456, "quality": 80}}

// Capture region
{"method": "capture_region", "arguments": {"x": 100, "y": 100, "w": 800, "h": 600}}
```

## Limitations & Considerations

### Window Capture Limitations
- **Minimized Windows**: Windows that are minimized may not be captured correctly or may show stale content. Ensure the target window is visible before capturing.
- **GPU-Accelerated Apps**: Uses PW_RENDERFULLCONTENT flag (Windows 8.1+) to capture Chrome, Electron, WPF apps. This works well for static screenshots but may have limitations with some applications.

### Performance Considerations
- **Static Screenshots**: ✅ Fully supported - capture single screenshots or periodic captures (every few seconds)
- **High-Frequency Video Capture**: ⚠️ Not recommended - CPU load is high. For video/streaming use cases, consider Desktop Duplication API (DirectX-based) instead.
- **Optimal Use Case**: Periodic monitoring, documentation screenshots, automated testing

## Architecture & Implementation

### Refactoring History

| Version | Changes | Status |
|---------|---------|--------|
| v1.0 | Initial SSE-only implementation | ✅ Merged |
| v1.1 | Tool naming (verbs), inputSchema, error handling | ✅ Merged |
| v1.2 | Unit tests, CI improvements | ✅ Merged |
| v1.3 | Graceful shutdown (IHostApplicationLifetime) | ✅ Merged |
| v1.4 | **Dual Transport** (Streamable HTTP + SSE) | ✅ Merged |
| v1.5 | **Window Capture** (list_windows, capture_window, capture_region) | ✅ Merged |

### Key Features

- **stdio Transport**: Default local-only mode - secure, no network exposure
- **Optional HTTP Mode**: Streamable HTTP and legacy SSE for backward compatibility
- **Session Management**: MCP-Session-Id header with automatic cleanup (HTTP mode)
- **Window Enumeration**: EnumWindows API for listing visible applications
- **Region Capture**: Arbitrary screen region capture using CopyFromScreen
- **Graceful Shutdown**: Proper cleanup on Ctrl+C or process termination
- **Error Handling**: Comprehensive try-catch blocks with meaningful error messages
- **CI/CD**: GitHub Actions with automated testing

## Client Configuration Examples

### Stdio Mode (Default / Recommended)

All modern MCP clients support stdio transport. This is the **secure, local-only** mode with no network exposure.

```json
{
  "mcpServers": {
    "windows-capture": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\WindowsScreenCaptureServer.csproj"]
    }
  }
}
```

Or with the published executable:

```json
{
  "mcpServers": {
    "windows-capture": {
      "command": "C:\\path\\to\\WindowsScreenCaptureServer.exe"
    }
  }
}
```

### HTTP Mode (Optional / Legacy)

Only needed for clients that don't support stdio. Requires running the server with `--http` flag first.

```json
{
  "mcpServers": {
    "windows-capture": {
      "url": "http://127.0.0.1:5000/mcp",
      "transport": "http"
    }
  }
}
```

## Security Considerations

- **stdio Mode**: No network exposure - completely local and secure
- **HTTP Mode**: Only enable when necessary; binds to localhost by default
- **Origin Validation**: HTTP endpoint validates Origin header to prevent DNS rebinding attacks
- **Session Isolation**: Each client gets a unique session ID with automatic expiration (1 hour)

## First Run (HTTP Mode Only)

If using HTTP mode with external access, run as Administrator in PowerShell:

```powershell
# For localhost only (default - no firewall rule needed)
# No action required

# For external access (not recommended)
netsh advfirewall firewall add rule name="MCP Screen Capture" dir=in action=allow protocol=TCP localport=5000
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| stdio mode not working | Ensure the executable path is correct in your MCP client config |
| Connection refused (HTTP mode) | Check firewall rules and ensure server is running with `--http` flag |
| 404 on /mcp | Verify you're using the latest build and server is running with `--http` flag |
| Black screen | Run with Administrator privileges |
| Window not found | Verify window is visible (not minimized to tray) |

## License

MIT License - See LICENSE file for details.
