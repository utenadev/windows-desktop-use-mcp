# AGENTS.md - Coding Guidelines for MCP Windows Screen Capture

## Project Overview
.NET 8.0 Web SDK project implementing an MCP (Model Context Protocol) server for Windows screen capture using GDI+. Windows-only (requires Windows 11 or Windows 10 1809+).

## Build Commands

```bash
# Build (Release)
dotnet build src/WindowsScreenCaptureServer.csproj -c Release

# Build (Debug)
dotnet build src/WindowsScreenCaptureServer.csproj -c Debug

# Run with CLI options
dotnet run --project src/WindowsScreenCaptureServer.csproj -- --ip_addr 0.0.0.0 --port 5000 --desktopNum 0

# Restore dependencies
dotnet restore src/WindowsScreenCaptureServer.csproj

# Publish single-file executable
dotnet publish src/WindowsScreenCaptureServer.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

## Test Commands

```bash
# Run all tests (if test project exists)
dotnet test

# Run tests with verbosity
dotnet test --verbosity normal

# Run tests without building (if already built)
dotnet test --no-build
```

**Note:** Test project exists at `tests/WindowsScreenCapture.Tests/`. To run tests: `dotnet test`

## Code Style Guidelines

### Project Configuration
- **Target Framework:** `net8.0-windows`
- **Implicit Usings:** Enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
- **Nullable:** Enabled (`<Nullable>enable</Nullable>`)
- **Output Type:** Exe (console application)
- **Runtime Identifier:** `win-x64`

### Naming Conventions
- **Public members:** PascalCase (e.g., `GetMonitors`, `CaptureSingle`)
- **Private fields:** `_` prefix + camelCase (e.g., `_defaultMon`, `_sessions`)
- **Local variables:** camelCase (e.g., `mon`, `qual`, `maxW`)
- **Parameters:** camelCase (e.g., `idx`, `interval`, `quality`)
- **Type parameters:** PascalCase (e.g., `T`)
- **Constants:** PascalCase

### Code Patterns

#### Expression-bodied members for simple methods
```csharp
public List<MonitorInfo> GetMonitors() => _monitors;
public McpServer(ScreenCaptureService capture) => _capture = capture;
```

#### Target-typed new expressions
```csharp
var list = new List<MonitorInfo>();
var p = new EncoderParameters(1);
```

#### Switch expressions
```csharp
return method switch {
    "list_monitors" => new { ... },
    "capture_screen" => Capture(args),
    _ => new { error = $"Unknown: {method}" }
};
```

#### Records for data classes
```csharp
public record MonitorInfo(uint Idx, string Name, int W, int H, int X, int Y);
public record McpRequest(string Method, JsonElement? Params, long? Id);
```

#### File-scoped classes (no namespace braces)
```csharp
public class ScreenCaptureService {
    // class members directly at file scope
}
```

### Error Handling
- Use `try/finally` for cleanup (e.g., removing clients from dictionary)
- Use empty catch blocks only for `OperationCanceledException` (expected cancellation)
- Return null for optional parameters: `JsonElement? Params`
- Use nullable reference types: `string?`, `object?`

### Formatting
- **Indentation:** 4 spaces
- **Braces:** K&R style, omit for single-line blocks
- **Line length:** No strict limit, but keep readable
- **Spacing:** Space after keywords (`if (condition)`), no space before semicolons

### Imports
- Use implicit usings (configured in .csproj)
- Add explicit usings for external packages:
  ```csharp
  using System.CommandLine;
  using System.Drawing;
  using System.Drawing.Imaging;
  ```
- Group: System.* first, then external packages, then project-specific

### P/Invoke Patterns
```csharp
[DllImport("user32.dll")] 
static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr rc, EnumMonDelegate del, IntPtr dw);

[StructLayout(LayoutKind.Sequential)] 
struct RECT { public int Left, Top, Right, Bottom; }
```

### JSON Handling
- Use `JsonSerializerOptions` with camelCase naming policy
- Use `JsonElement` for dynamic JSON parsing
- Use `System.Text.Json` (built-in, no Newtonsoft.Json)

## Dependencies
- `System.Drawing.Common` (8.0.0) - GDI+ screen capture
- `System.CommandLine` (2.0.0-beta4) - CLI argument parsing
- ASP.NET Core (via Web SDK) - HTTP server and SSE

## Windows-Specific Considerations
- Must run on Windows (uses user32.dll GDI APIs)
- May require Administrator privileges for capturing certain windows
- Firewall rule needed for WSL2 access: `netsh advfirewall firewall add rule name="MCP Screen Capture" dir=in action=allow protocol=TCP localport=5000 remoteip=172.16.0.0/12`

## Language Policy
- **Source code comments**: English only
- **Commit messages**: English only (e.g., "Fix build errors in GitHub Actions workflow")
- **User conversation**: Japanese ( user's preferred language)
- **Documentation**: English preferred, Japanese allowed for some documents (e.g., `docs/NextOrder.md`)

## Git Operations

**IMPORTANT:** Always obtain explicit user approval before performing any git operations that modify the repository state:
- `git commit` - Must ask for approval before committing
- `git push` - Must ask for approval before pushing
- `git merge` - Must ask for approval before merging
- `git rebase` - Must ask for approval before rebasing
- `git reset --hard` - Must ask for approval (destructive operation)

**Allowed without approval:**
- `git status` - Check repository status
- `git log` - View commit history
- `git diff` - View changes
- `git branch` - List branches
- `git add` - Stage changes (but do not commit without approval)

## CI/CD
GitHub Actions workflow builds and publishes artifacts on push to main/master.
