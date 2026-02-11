# windows-desktop-use-mcp

An MCP server for controlling and perceiving Windows 11 from AI assistants.
It provides AI with "eyes" (vision), "ears" (hearing), and "limbs" (input control), making the desktop environment accessible from MCP clients like Claude.

[English](README.md) | [日本語](README.ja.md)

## Main Features

- **Vision (Screen Capture)**: Capture monitors, specific windows, or arbitrary regions.
- **Hearing (Audio & Transcription)**: Record system audio or microphone, with high-quality local transcription using Whisper AI.
- **Limbs (Desktop Input)**: Mouse movement, clicking, dragging, and safe navigation key operations (security restricted).
- **Live Monitoring (Streaming)**: Monitor screen changes in real-time, viewable via HTTP streaming in a browser.

## For Non-Developers (Pre-built .exe)

If you don't have a development environment, you can use the pre-built executable from [Releases](../../releases).

### 1. Download
1. Go to the [Releases page](../../releases)
2. Download the latest `WindowsDesktopUse.zip`
3. Extract to your preferred location (e.g., `C:\Tools\WindowsDesktopUse`)

### 2. Configure Claude Desktop
**Option A: Automatic setup**
```powershell
C:\Tools\WindowsDesktopUse\WindowsDesktopUse.App.exe setup
```

**Option B: Manual setup**
Add this to your `%AppData%\Roaming\Claude\claude_desktop_config.json`:
```json
{
  "mcpServers": {
    "windows-desktop-use": {
      "command": "C:\\Tools\\WindowsDesktopUse\\WindowsDesktopUse.App.exe",
      "args": ["--httpPort", "5000"]
    }
  }
}
```

### 3. Restart Claude Desktop
Close and reopen Claude Desktop to load the new MCP server.

---

## For Developers (Build from Source)

### 1. Build
```powershell
dotnet build src/WindowsDesktopUse.App/WindowsDesktopUse.App.csproj -c Release
```

### 2. Configure Claude Desktop
**Option A: Automatic setup**
```powershell
WindowsDesktopUse.App.exe setup
```

**Option B: Manual setup**
Add this to your `%AppData%\Roaming\Claude\claude_desktop_config.json`:
```json
{
  "mcpServers": {
    "windows-desktop-use": {
      "command": "C:\\path\\to\\WindowsDesktopUse.App.exe",
      "args": ["--httpPort", "5000"]
    }
  }
}
```

### 3. Verify Installation
```powershell
WindowsDesktopUse.App.exe doctor
```

## CLI Commands

### `doctor` - System Diagnostics
Check system compatibility and configuration.
```powershell
WindowsDesktopUse.App.exe doctor
WindowsDesktopUse.App.exe doctor --verbose    # Show detailed information
WindowsDesktopUse.App.exe doctor --json       # Output in JSON format
```

### `setup` - Claude Desktop Configuration
Automatically configure Claude Desktop integration.
```powershell
WindowsDesktopUse.App.exe setup                              # Use default config path
WindowsDesktopUse.App.exe setup --config-path "C:\custom\path.json"  # Custom config path
WindowsDesktopUse.App.exe setup --no-merge                    # Overwrite existing config
WindowsDesktopUse.App.exe setup --dry-run                    # Show config without writing
```

### `whisper` - Whisper AI Models
Manage Whisper AI models for audio transcription.
```powershell
WindowsDesktopUse.App.exe whisper          # List available models and check installation
WindowsDesktopUse.App.exe whisper --list   # Show model list only
```

## Available MCP Tools (Summary)

### Vision & Information
- `list_all`: List all monitors and windows.
- `capture`: Capture any target as an image.
- `watch`: Start monitoring/streaming a target.

### Hearing
- `listen`: Record system audio or microphone and transcribe to text.
- `list_audio_devices`: List available audio devices.

### Input Control
- `mouse_move`: Move cursor to specific coordinates.
- `mouse_click`: Left/Right/Middle click, and double-clicks.
- `mouse_drag`: Drag and drop operations.
- `keyboard_key`: Press safe navigation keys (Enter, Tab, arrow keys, etc.). Text typing and modifier keys (Ctrl, Alt, Win) are blocked for security.

For detailed arguments and examples, see the [**Tools Guide**](docs/TOOLS.md).

## Documentation Index

- [**Tools Reference**](docs/TOOLS.md) - Detailed command list and usage examples.
- [**Development Guide**](docs/DEVELOPMENT.md) - Details on build, test, and architecture (DLL structure).
- [**Whisper AI**](docs/WHISPER.md) - Information about speech recognition features and models.

## Requirements

- Windows 11 (or Windows 10 1803+)
- .NET 8.0 Runtime/SDK
- High DPI aware (Uses physical pixel coordinates)

## License
MIT License. See [LICENSE](LICENSE) file.
