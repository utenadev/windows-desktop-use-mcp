# Changelog

All notable changes to windows-desktop-use-mcp will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **`watch_video` Tool**: New high-efficiency video capture pipeline for LLM consumption.
    - **Video Target Finder**: UI Automation-based dynamic video element detection with window title tracking (YouTube, Netflix, etc.).
    - **Visual Change Detection**: Grid-based (16x16) pixel sampling to skip duplicate frames and reduce token usage.
    - **Optimized GDI+ Processing**: Bilinear interpolation, 640px max width, JPEG quality 60-70 for low-latency streaming.
    - **Structured Payload**: JSON format with timestamp, window metadata, change detection results, and Base64 image data.
    - **MCP Tools**: `watch_video`, `stop_watch_video`, and `get_latest_video_frame` for video stream management.
    - **Memory Management**: Proper disposal of Bitmap/Graphics objects using `using` blocks.
- **`watch_video_v1` Tool (Spiral 1)**: Prototype unified video/audio capture with synchronized timeline.
    - **Unified Timeline**: `UnifiedEventPayload` with `RelativeTime` for video/audio synchronization.
    - **StreamSession Enhancement**: Added `StartTime` and `RelativeTime` property for time-based coordination.
    - **Whisper OS Language Detection**: Auto-detects OS language for transcription using `CultureInfo.CurrentCulture`.
    - **Combined Capture**: Integrates video capture with audio transcription in a single session.
- **`close_window` Tool**: New tool to terminate a process by its window handle (HWND).
- **Process Identification**: Added `GetWindowThreadProcessId` P/Invoke to `InputService` to link windows to their owning processes.
- **Unit Tests**: Added comprehensive tests for `VisualChangeDetector` and `VideoTargetFinder` components.
- **E2E Tests**: Added `VideoCaptureE2ETests` for video pipeline integration testing.
- **Improved E2E Test Infrastructure**: 
    - Refactored `McpE2ETests.cs` to use `OneTimeSetUp` and `OneTimeTearDown` for better resource management.
    - Implemented a more robust Notepad window identification logic with retries and title-based fallback.
    - Added PID-based tracking to clean up only processes started during the test session.
- **Development Guidelines**: Updated `AGENTS.md` to standardize on `rg` (ripgrep) and Japanese communication for developers.
- **Incident Report**: Added `docs/report/report_20260209_notepad_e2e_fix.md` documenting the challenges with Windows 11 Notepad automation.

### Changed
- **Code Quality Improvements**: Resolved all static analysis warnings and improved maintainability.
  - Fixed culture-dependent method calls (CA1304, CA1305, CA1307, CA1308, CA1311).
  - Implemented proper IDisposable pattern (CA1063, CA1816, CA1001).
  - Used read-only collections where appropriate (CA1002).
  - Added ConfigureAwait(false) to async method calls (CA2007).
  - Marked static holder class as static (CA1052).
  - Extracted nested VirtualKeys class to top-level (CA1034).
  - Fixed method return value usage (CA1806, CA1822).
- **Async Optimization**: Replaced Thread.Sleep with Task.Delay for better async performance.
- **Test Code Quality**: Fixed all static analysis warnings in E2E tests (CA1031, CA1050, CA1307, CA1310, CA1707).

### Fixed
- Stabilized E2E tests by ensuring a fresh application instance is used for each suite.
- Fixed `NullReferenceException` in window enumeration when titles are null.
- **Build Errors**: Resolved async method call issues.
  - Updated `StopCapture` to `StopCaptureAsync` in DesktopUseTools.
  - Made Listen method async to properly handle await calls.
  - Added missing await keywords in Program.cs.
- **Null Safety**: Fixed NullReferenceException risks in DesktopUseTools with enhanced null checks.

### Security
- **Input Validation**: Enhanced error handling and parameter validation in DesktopUseTools.

## [3.0.0] - 2026-02-08
...
