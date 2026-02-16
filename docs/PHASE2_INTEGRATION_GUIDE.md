# Phase 2 Integration Guide for opencode

## Overview
Phase 1 で Vision 最適化のコアロジックが完成しました。Phase 2 では、これを MCP ツールの外部インターフェースに公開します。

## Core Changes Required

### 1. Add `enableOverlay` parameter to MCP tools

#### Tool: `visual_watch`
**File**: `src/WindowsDesktopUse.App/DesktopUseTools.cs` or `Program.cs`

Add optional parameter:
```csharp
[JsonPropertyName("enable_overlay")]
public bool? EnableOverlay { get; set; } = false;
```

**Default**: `false` (opt-in for performance)

---

### 2. Pass flag to VideoCaptureService

**Location**: Where `StartVideoStreamAsync` is called

**Example**:
```csharp
var sessionId = await videoCaptureService.StartVideoStreamAsync(
    targetName: windowTitle,
    fps: fps,
    quality: quality,
    maxWidth: maxWidth,
    enableChangeDetection: true,
    changeThreshold: changeThreshold,
    keyFrameInterval: keyFrameInterval,
    enableOverlay: request.EnableOverlay ?? false, // <-- Pass the flag
    onFrameCaptured: async (payload) =>
    {
        // Existing callback logic
    }
);
```

---

### 3. SessionManager Integration

**File**: `src/WindowsDesktopUse.App/SessionManager.cs`

Add `EnableOverlay` property to `UnifiedSession` or equivalent session tracking class:
```csharp
public bool EnableOverlay { get; set; } = false;
```

This allows querying active sessions with overlay enabled.

---

### 4. CLI Options (Optional)

If you want to support CLI flags:
```bash
--overlay true
--enable-overlay
```

Parse these in `Program.cs` argument parsing logic.

---

## Testing Checklist

- [ ] `visual_watch` with `enable_overlay: false` (default) - no overlay
- [ ] `visual_watch` with `enable_overlay: true` - timestamp + event tag visible
- [ ] Performance: Verify <10ms overhead per frame
- [ ] OCR: Confirm timestamp is readable in captured frames
- [ ] Model compatibility: Test with Claude Desktop (if available)

---

## Files Modified in Phase 1 (Reference)

### Core
- `src/WindowsDesktopUse.Core/FrameContext.cs` (NEW)
  - `FrameContext` record with temporal context
  - `GenerateContextualPrompt()` method
  - `FrameContextBuilder` class

### Screen
- `src/WindowsDesktopUse.Screen/ImageOverlayService.cs` (NEW)
  - `OverlayTimestamp(Bitmap, TimeSpan)`
  - `OverlayEventTag(Bitmap, string?)`
  - `GenerateDiffImage(Bitmap, Bitmap)`

- `src/WindowsDesktopUse.Screen/ScreenCaptureService.cs`
  - Added `EnableOverlay` property
  - Added `GetEventTag` callback
  - Integrated overlay in capture methods

- `src/WindowsDesktopUse.Screen/VideoCaptureService.cs`
  - Added `enableOverlay` parameter to `StartVideoStreamAsync`
  - Added `EnableOverlay` property to `VideoSession`
  - Integrated overlay in `CaptureLoopAsync` (before JPEG encoding)

---

## Design Principles (DO NOT BREAK)

1. **Opt-in by default**: Overlay is `false` unless explicitly requested
2. **Model-agnostic**: Works with Claude, Qwen, GPT, etc.
3. **Performance first**: <10ms overhead per frame
4. **Non-destructive**: Original frame content is preserved, overlay is additive

---

## Questions?

Contact Gemini for architectural review or qwencode for Vision-specific implementation details.
