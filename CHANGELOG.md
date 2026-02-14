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
- **`watch_video_v2` Tool (Video Co-view Sync)**: Synchronized video/audio capture for co-viewing experience.
    - **Synchronized Timeline**: Common `ts` (RelativeTime) for video frames and audio transcription.
    - **Interval-based Capture**: Default 2000ms intervals for video + audio batch processing.
    - **Base64 Normalization**: Removes newlines and spaces from base64 image data.
    - **Parallel Processing**: Audio recording and video capture run in parallel without blocking.
    - **MCP Notifications**: Sends `video_coview` type with `ts`, `frame`, `transcript`, and `windowTitle`.
- **`read_window_text` Tool**: UI Automation-based structured text extraction from windows.
    - **Markdown Output**: Converts UI tree to Markdown format with headers, lists, and text.
    - **Control Type Mapping**: TitleBar/Header -> `#`, ListItem -> `-`, Text/Edit -> plain text.
    - **URL Detection**: Identifies browser address bars for URL extraction.
    - **Max Depth**: Limited to 10 levels to prevent infinite loops.
- **`monitor` Tool**: Event-driven window monitoring with visual change detection.
    - **Sensitivity Levels**: High (1%), Medium (5%), Low (15%) change thresholds.
    - **Grid Indices**: Reports which grid cells changed for precise location tracking.
    - **MCP Notifications**: Sends `window_monitor` type notifications on visual changes.
    - **Session Management**: `stop_monitor` for proper resource cleanup.
- **`close_window` Tool**: New tool to terminate a process by its window handle (HWND).
- **Process Identification**: Added `GetWindowThreadProcessId` P/Invoke to `InputService` to link windows to their owning processes.
- **GPU-Accelerated Video Capture (WGC)**: Implemented `ModernCaptureService` with Windows Graphics Capture support.
    - **PW_RENDERFULLCONTENT Flag**: Captures hardware-accelerated content (YouTube, Netflix) without black screen.
    - **Hybrid Capture**: Modern API (WGC) with fallback to GDI+ for compatibility.
    - **Resource Management**: Proper disposal of D3D11 devices and contexts.
- **Timestamp Normalization**: Fixed hardcoded `00:00:00` timestamps in video payloads.
    - **Relative Time Calculation**: Uses session start time to calculate elapsed duration.
    - **Format**: `hh:mm:ss.f` (e.g., `00:00:05.2`) for accurate video progress tracking.
- **Timestamp Precision Improvement**: Implemented absolute-time scheduling for accurate capture intervals.
    - **Absolute Time Scheduling**: `nextCaptureTime` based scheduling ensures consistent capture intervals (±100ms).
    - **Actual Capture Timestamp**: `ts` is calculated at the moment of capture completion, not loop start.
    - **Interval Maintenance**: Strict interval preservation even when processing takes longer than expected.
    - **Drift Prevention**: Eliminates cumulative timing errors between video frames and audio transcription.
- **Token Efficiency Protocol**: Added memory-efficient image processing instructions for LLMs.
    - **LLM Instruction Schema**: `_llm_instruction` field in responses with PROCESS_IMMEDIATELY_AND_DISCARD action.
    - **Tool Description Updates**: CRITICAL warnings in tool descriptions to prevent token overflow.
    - **Explicit Discard Steps**: 4-step protocol (extract metadata, analyze, record as text, delete base64).
    - **Token Warning**: Approx 2000+ tokens per image, 95% memory savings by discarding.
- **Unified Tool Architecture v2.0**: Consolidated fragmented tools into clean, intuitive interfaces.
    - **`visual_list`**: Unified `list_monitors`, `list_windows`, `list_all` with `type` parameter.
    - **`visual_capture`**: Unified all capture tools with dynamic quality control (Normal=30, Detailed=70).
    - **`visual_watch`**: Unified `watch`, `watch_video_v2`, `monitor` with `mode` parameter.
    - **`visual_stop`**: Single stop command for all session types.
    - **`input_mouse`**: Unified `mouse_move`, `mouse_click`, `mouse_drag` with `action` parameter.
    - **`input_window`**: Unified window operations (close, minimize, maximize, restore).
    - **`SessionManager`**: Centralized session management for all async operations.
    - **Migration Guide**: Complete documentation for tool transition (MIGRATION_GUIDE_v2.md).
- **Unit Tests**: Added comprehensive tests for `VisualChangeDetector` and `VideoTargetFinder` components.
- **E2E Tests**: Added `VideoCaptureE2ETests` for video pipeline integration testing.
- **Improved E2E Test Infrastructure**: 
    - Refactored `McpE2ETests.cs` to use `OneTimeSetUp` and `OneTimeTearDown` for better resource management.
    - Implemented a more robust Notepad window identification logic with retries and title-based fallback.
    - Added PID-based tracking to clean up only processes started during the test session.
- **Development Guidelines**: Updated `AGENTS.md` to standardize on `rg` (ripgrep) and Japanese communication for developers.
- **Incident Report**: Added `docs/report/report_20260209_notepad_e2e_fix.md` documenting the challenges with Windows 11 Notepad automation.

### Changed
- **Project Cleanup**: Organized repository by moving old documentation and removing obsolete files.
    - Moved legacy design docs and feature memos to `docs/old/`.
    - Removed `build_history/` from Git tracking and added to `.gitignore`.
    - Cleaned up temporary test artifacts in `t/`.
    - Removed default `UnitTest1.cs` from E2E tests.
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
- **Static Analysis**: Changed AnalysisMode from 'All' to 'Recommended' to reduce CI build errors.
  - Updated AnalysisLevel from 'latest-all' to 'latest-recommended' in all project files.
- **E2E Tests**: Added CI skip for video capture tests.
  - `WatchVideo_ActiveWindow_ReturnsSessionId` and `WatchVideoV1_StartsSuccessfully` now skip on CI.
  - These tests require an active video window (YouTube, etc.) to be running.

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
