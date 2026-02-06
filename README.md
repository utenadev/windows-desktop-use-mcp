# MCP Windows Screen Capture Server

Windows 11 screen capture MCP server with stdio transport for Claude Desktop.

## Phase 4 完了！✅

Phase 4（Whisper.net 音声認識）の実装が完了しました！

### 実装内容
- ✅ Whisper.net 1.9.0 統合（CPU版）
- ✅ WhisperTranscriptionService サービス
- ✅ `listen` ツール：音声文字起こし
- ✅ `get_whisper_model_info` ツール：モデル情報取得
- ✅ 自動モデルダウンロード（Hugging Face）
- ✅ WAV形式変換（Whisper互換の16kHz/16bit/mono）
- ✅ Smallモデル対応（244MB、高精度）
- ✅ 日本語テスト完了（ラブライブ！15セグメント完璧文字起こし）

## Requirements

- Windows 11 (or Windows 10 1803+)
- .NET 8.0 SDK
- Whisper.ggmlモデル（初回起動時に自動ダウンロード）

## Available MCP Tools

### Screen Capture Tools
| Tool | Description |
|------|-------------|
| `list_monitors` | List all available monitors/displays |
| `list_windows` | List all visible Windows applications (hwnd, title, position, size) |
| `see` | Capture a screenshot of specified monitor |
| `capture_window` | Capture a specific window by its HWND |
| `capture_region` | Capture an arbitrary screen region (x, y, width, height) |
| `start_watching` | Start a continuous screen capture stream |
| `stop_watching` | Stop a running screen capture stream by session ID |
| `get_latest_frame` | Get latest captured frame with hash for change detection |

### Unified Tools (Phase 1)
| Tool | Description |
|------|-------------|
| `list_all` | List all available capture targets (monitors and windows) with unified interface |
| `capture` | Capture screen, window, or region as image or video |
| `watch` | Start watching/streaming a target (monitor, window, or region) |
| `stop_watch` | Stop watching a capture session |

### Audio Capture Tools (Phase 3)
| Tool | Description |
|------|-------------|
| `list_audio_devices` | List available audio devices (microphones and system audio) |
| `start_audio_capture` | Start recording from system or microphone |
| `stop_audio_capture` | Stop recording and return captured audio data |
| `get_active_audio_sessions` | Get list of active audio capture sessions |

### Speech Recognition Tools (Phase 4)
| Tool | Description |
|------|-------------|
| `listen` | Transcribe audio to text using Whisper AI |
| `get_whisper_model_info` | Get available Whisper model information |

## Tool Examples

### Screen Capture
```json
// List monitors
{"method": "list_monitors"}

// Screenshot
{"method": "see", "arguments": {"monitor": 0}}

// Window capture
{"method": "capture_window", "arguments": {"hwnd": 123456, "quality": 80}}

// Region capture
{"method": "capture_region", "arguments": {"x": 100, "y": 100, "w": 800, "h": 600}}

// Start watching
{"method": "start_watching", "arguments": {"targetType": "monitor", "monitor": 0, "intervalMs": 1000}}
```

### Unified Tools (Phase 1)
```json
// List all targets
{"method": "list_all", "arguments": {"filter": "all"}}

// Capture primary monitor
{"method": "capture", "arguments": {"target": "primary", "format": "jpeg"}}

// Watch monitor
{"method": "watch", "arguments": {"target": "monitor", "targetId": "0", "intervalMs": 1000}}
```

### Audio Capture (Phase 3)
```json
// List devices
{"method": "list_audio_devices"}

// Start system audio capture
{"method": "start_audio_capture", "arguments": {"source": "system", "sampleRate": 44100}}

// Stop capture
{"method": "stop_audio_capture", "arguments": {"sessionId": "xxx", "returnFormat": "base64"}}
```

### Speech Recognition (Phase 4)
```json
// Transcribe system audio (auto language, base model)
{"method": "listen", "arguments": {"source": "system", "duration": 30, "language": "auto", "modelSize": "base"}}

// Transcribe with Japanese language
{"method": "listen", "arguments": {"source": "system", "duration": 30, "language": "ja", "modelSize": "small"}}

// Get model info
{"method": "get_whisper_model_info"}
```

## Streaming Features

### Frame Streaming with HTTP Server
The server includes an optional HTTP server that runs in the **same process** as the MCP stdio server. This enables browser-based viewing alongside Claude Desktop interactions.

**Key Points:**
- HTTP server runs in **same process** as MCP stdio server (multi-threaded, not multi-process)
- Runs on localhost only for security
- Configure via command-line arguments in your MCP client settings

**Configuration Examples:**
```json
// Default HTTP port (5000)
{
  "mcpServers": {
    "windows-capture": {
      "command": "C:\\path\\to\\WindowsScreenCaptureServer.exe"
    }
  }
}

// Custom HTTP port
{
  "mcpServers": {
    "windows-capture": {
      "command": "C:\\path\\to\\WindowsScreenCaptureServer.exe",
      "args": ["--httpPort", "8080"]
    }
  }
}

// Disable HTTP server (stdio only)
{
  "mcpServers": {
    "windows-capture": {
      "command": "C:\\path\\to\\WindowsScreenCaptureServer.exe",
      "args": ["--httpPort", "0"]
    }
  }
}
```

**HTTP Endpoints:**
| Endpoint | Description |
|----------|-------------|
| `GET /frame/{sessionId}` | Get latest frame as JPEG image |
| `GET /frame/{sessionId}/info` | Get frame metadata (hash, timestamp) |
| `GET /health` | Health check |
| `GET /` | Server info and usage |

**Browser Usage Example:**
```html
<!-- Simple auto-refresh image -->
<img src="http://localhost:5000/frame/SESSION_ID" style="max-width:100%;" 
     onload="setTimeout(() => this.src = this.src.split('?')[0] + '?' + Date.now(), 1000)">
```

### Testing

Direct testing with MCP stdio protocol:
```bash
# Test initialization
echo '{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2024-11-05"},"id":1}' | src/bin/Release/net8.0-windows/win-x64/WindowsScreenCaptureServer.exe

# Test list_windows tool
echo '{"jsonrpc":"2.0","method":"tools/call","params":{"name":"list_windows","arguments":{}},"id":2}' | src/bin/Release/net8.0-windows/win-x64/WindowsScreenCaptureServer.exe
```

## Whisper Speech Recognition

### Supported Models
| Model | Size | Performance | Best For |
|-------|------|-------------|---------|
| Tiny | 39 MB | Fastest / Lowest accuracy | Real-time streaming |
| Base | 74 MB | Fast / Medium accuracy | Recommended for general use |
| Small | 244 MB | Medium / High accuracy | Quality-focused transcription |
| Medium | 769 MB | Slow / Very high accuracy | File processing |
| Large | 1550 MB | Slowest / Best accuracy | Maximum accuracy needed |

### Supported Languages
Auto-detection supported. Language codes: `en` (English), `ja` (Japanese), and more (see Whisper documentation).

### Direct Whisper Test
```bash
# Test with Japanese language, Small model
./WindowsScreenCaptureServer.exe --test-whisper
```

### Behavior Notes
- **Language Detection**: Whisper determines language in the first few seconds. If you set `language: "auto"`, it may default to English for mixed content. Use `language: "ja"` for Japanese content.
- **Model Performance**: Small model (244MB) provides excellent accuracy with reasonable speed for Japanese transcription.
- **Audio Quality**: Best results with clear, close-mic audio in quiet environment.

## Features

### Implemented
- ✅ Whisper.net 1.9.0 integration (CPU-only)
- ✅ Automatic model download from Hugging Face
- ✅ Multiple model sizes (tiny/base/small/medium/large)
- ✅ Language support (auto-detection, Japanese, English)
- ✅ WAV format conversion (16kHz/16bit/mono for Whisper)
- ✅ System audio and microphone capture
- ✅ Real-time transcription support

### Future Enhancements (TODO)
- [ ] GPU acceleration (Vulkan/CUDA) - separate phase
- [ ] Japanese language model (ggml-base-ja.bin)
- [ ] Video capture with audio synchronization
- [ ] Improved language detection accuracy
- [ ] Whisper.cpp runtime updates

## Architecture

```
src/
├── Services/
│   ├── ScreenCaptureService.cs      # GDI+ Screen capture (GDI+)
│   ├── AudioCaptureService.cs        # NAudio audio capture (Phase 3)
│   ├── WhisperTranscriptionService.cs  # Whisper.net integration (Phase 4)
│   └── CaptureServices/
│       └── ModernCaptureService.cs  # Windows.Graphics.Capture foundation (Phase 2 stub)
├── Tools/
│   └── ScreenCaptureTools.cs       # All MCP tool implementations
└── Program.cs                      # Server entry point
```

## Installation & Usage

```bash
# Build (Release)
dotnet build src/WindowsScreenCaptureServer.csproj -c Release

# Run (stdio mode for MCP)
./WindowsScreenCaptureServer.exe

# Run (with HTTP server for frame streaming)
./WindowsScreenCaptureServer.exe --httpPort 5000

# Test Whisper transcription directly (Japanese)
./WindowsScreenCaptureServer.exe --test-whisper
```

## Troubleshooting

| Issue | Solution |
|--------|----------|
| stdio mode not working | Ensure executable path is correct in your MCP client config |
| Server not found | Verify path to `WindowsScreenCaptureServer.exe` exists |
| Black screen | Run with Administrator privileges |
| Window not found | Verify window is visible (not minimized to tray) |
| Claude Desktop can't connect | Check Claude Desktop logs (Settings > Developer > Open Logs) |
| HTTP server not accessible | Ensure port is not blocked by firewall; default is localhost-only |
| Whisper not transcribing | Ensure audio is playing, check device permissions |

## License

This project uses the following open-source libraries:
- NAudio (LGPL/MS-PL)
- Whisper.net (MIT License)
- ModelContextProtocol (Apache License)

## Version History

### v2.0.0 - Phase 4 Complete (Current)
- ✅ Whisper.net 音声認識統合
- ✅ 日本語対応
- ✅ 自動モデルダウンロード
- ✅ 14種のMCPツール実装
- ✅ 完璧なラブライブ！日本語文字起こしテスト完了

### v1.0.0 - Phase 3 Complete
- ✅ NAudioオーディオキャプチャ
- ✅ システム音声/マイク対応

### v1.0.0-beta - Phase 1 & 2
- ✅ 統合ツール（list_all, capture, watch）
- ✅ Windows Graphics Capture API基盤

## Acknowledgments

Special thanks to the creators of the open-source libraries that make this project possible:
- Whisper.net team for their excellent .NET bindings to OpenAI's Whisper
- NAudio team for the Windows audio framework
- ModelContextProtocol team for the MCP SDK

---

**© 2025 MCP Windows Screen Capture Project**
**Powered by Whisper.net (OpenAI Whisper for .NET)**
