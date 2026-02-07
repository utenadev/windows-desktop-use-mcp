# windows-desktop-use-mcp Tools

This document provides detailed information about the MCP tools available in this server.

## Summary Table

| Category | Tool | Description |
|----------|------|-------------|
| **Information** | `list_all` | List all monitors and windows |
| | `list_monitors` | List all available monitors |
| | `list_windows` | List all visible applications |
| **Vision (Capture)** | `capture` | Capture any target (monitor, window, or region) |
| | `see` | Capture a screenshot of a monitor or window |
| | `capture_region` | Capture an arbitrary screen region |
| | `capture_window` | Capture a specific window by HWND |
| **Vision (Watch)** | `watch` | Start monitoring/streaming a target |
| | `stop_watch` | Stop a monitoring session |
| | `start_watching` | Start a screen capture stream (legacy) |
| | `stop_watching` | Stop a running stream (legacy) |
| | `get_latest_frame`| Get the latest captured frame |
| **Control (Mouse)** | `mouse_move` | Move cursor to specific coordinates |
| | `mouse_click` | Mouse click (left/right/middle, double-click) |
| | `mouse_drag` | Drag and drop operation |
| **Control (Keyboard)**| `keyboard_type` | Type text (Unicode support) |
| | `keyboard_key` | Press special keys (Enter, Tab, Ctrl+C, etc.) |
| **Hearing** | `listen` | Transcribe system audio or microphone |
| | `list_audio_devices` | List available audio devices |
| | `get_active_audio_sessions` | List running audio sessions |
| **Config/AI** | `get_whisper_model_info` | Get information about available Whisper models |

---

## Detailed Tool Reference

### Screen & Window Capture

#### `capture` (Recommended)
A single tool to capture a monitor, a window, or a specific region.
- **Arguments:**
  - `target` (string): "primary", "monitor", "window", "region" (default: "primary")
  - `targetId` (string): Monitor index or HWND (required if target is "monitor" or "window")
  - `x`, `y`, `w`, `h` (number): Region specification (required if target is "region")
  - `quality` (number): JPEG quality 1-100 (default: 80)
  - `maxWidth` (number): Maximum width. Resizes if larger while maintaining aspect ratio (default: 1920)

---

### Desktop Input (Control)

#### `mouse_move`
Moves the mouse cursor to the specified coordinates.
- **Arguments:**
  - `x` (number): Destination X coordinate (physical pixels)
  - `y` (number): Destination Y coordinate (physical pixels)

#### `mouse_click`
Simulates a mouse click.
- **Arguments:**
  - `button` (string): "left", "right", "middle" (default: "left")
  - `count` (number): Number of clicks. Set to 2 for double-click (default: 1)

#### `mouse_drag`
Drags from the start position to the end position while holding the left button.
- **Arguments:**
  - `startX`, `startY` (number): Start coordinates
  - `endX`, `endY` (number): Drop destination coordinates

#### `keyboard_type`
Sends the specified string as keyboard input. Supports Unicode characters.
- **Arguments:**
  - `text` (string): The text to type

#### `keyboard_key`
Simulates pressing, releasing, or clicking (press and release) a special key.
- **Arguments:**
  - `key` (string): Key name.
    - Available: `enter`, `return`, `tab`, `escape`, `esc`, `space`, `backspace`, `delete`, `del`, `left`, `up`, `right`, `down`, `home`, `end`, `pageup`, `pagedown`, `shift`, `ctrl`, `alt`, `win`
  - `action` (string): "click", "press" (hold down), "release" (let go) (default: "click")

---

### Audio & Transcription

#### `listen`
Records audio and transcribes it using Whisper.
- **Arguments:**
  - `source` (string): "system", "microphone", "file", "audio_session" (default: "system")
  - `sourceId` (string): File path or Audio Session ID
  - `duration` (number): Recording duration in seconds (default: 10)
  - `language` (string): Language code. "auto" or "ja", "en", "zh", etc. (default: "auto")
  - `modelSize` (string): Model size. "tiny", "base", "small", "medium", "large" (default: "base")
  - `translate` (boolean): Whether to translate to English (default: false)