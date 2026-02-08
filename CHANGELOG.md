# Changelog

All notable changes to windows-desktop-use-mcp will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.0.0] - 2026-02-08

### Added
- **CLI Subcommands**: New `System.CommandLine` based CLI interface with enhanced usability.
  - `doctor`: System diagnostics with `--verbose` (detailed output) and `--json` (JSON format) options
  - `setup`: Automatic Claude Desktop configuration with `--config-path`, `--no-merge`, and `--dry-run` options
  - `whisper`: Whisper AI model management with `--list` option
- **Administrator Privilege Check**: Enhanced `doctor` command with administrator privilege detection and helpful guidance for audio device issues
- **Desktop Input Module**: New `WindowsDesktopUse.Input` DLL for controlling Windows desktop.
  - Mouse control: `mouse_move`, `mouse_click`, `mouse_drag`.
  - Keyboard control: `keyboard_type` (Unicode/International support), `keyboard_key` (Special keys like Ctrl, Alt, Win, Enter, etc.).
  - Unified coordinate system using physical pixels, ensuring 1:1 mapping between screen capture and input actions.
- **Modular Architecture**: Project refactored into functional DLLs for better maintainability and extensibility.
  - `WindowsDesktopUse.Core`: Shared models and interfaces.
  - `WindowsDesktopUse.Screen`: Screen and window capture logic.
  - `WindowsDesktopUse.Audio`: WASAPI audio recording.
  - `WindowsDesktopUse.Transcription`: Whisper AI transcription.
  - `WindowsDesktopUse.Input`: Desktop automation (SendInput).
  - `WindowsDesktopUse.App`: MCP Server host.
- **Enhanced E2E Tests**: Expanded test suite to cover new input features.
  - Added Notepad automation tests (Type -> Capture -> Verify).
  - Total non-explicit tests increased to 26, plus 9 explicit GUI tests.
- **Documentation Overhaul**:
  - Updated `README.md` and `README.ja.md` with new project name and features.
  - Added architecture diagrams and module details to `DEVELOPMENT.md`.
  - Comprehensive tool documentation in `TOOLS.md`.

### Changed
- **Project Renamed**: Migrated from `mcp-windows-screen-capture` to **`windows-desktop-use-mcp`**.
- **Namespace Unified**: All code now uses the `WindowsDesktopUse` namespace.
- **CI/CD Improvements**: GitHub Actions workflows updated to support multi-project solution and new paths.
- **Improved High-DPI Support**: Explicit use of `SetProcessDPIAware()` to ensure consistent pixel-perfect coordinates across all tools.

### Fixed
- E2E test server path resolution issue.
- Discordant documentation between English and Japanese versions.
- Cleaned up obsolete solution files and temporary debug artifacts.
- **InputService API Consistency**: Unified all mouse/keyboard operations to use `SendInput` API instead of mixed `mouse_event`/`SetCursorPos`.
- **Static Analysis**: Resolved all warnings in source projects (0 warnings achieved).
- **Resource Management**: Implemented `IDisposable` pattern in `StreamSession` for proper cleanup.
- **CI/CD**: Fixed GitHub Actions build error (MSB1011) by specifying explicit solution file paths.

### Security
- **Input Security Restriction**: Restricted keyboard input to safe navigation keys only for security.
  - Removed `keyboard_type` tool to prevent arbitrary text input and system command execution.
  - Blocked dangerous modifier keys (Ctrl, Alt, Win) at API level.
  - Allowed keys: arrows, Tab, Enter, Escape, Space, Backspace, Delete, Home, End, PageUp, PageDown.
  - All keyboard operations now use secure `SendInput` API with validation.

### Added
- **Planning Documents**: Added `docs/report/plan_20260208_safe_input.md` for secure desktop input implementation strategy.
- **Review Documents**: Added `docs/report/review_20260208_safe_input.md` with comprehensive code review feedback.

## [2.1.0] - 2026-02-04

### Changed
- Simplified server to stdio-only mode (removed HTTP mode)
- Removed ~500 lines of code (StreamableHttpServer.cs, McpSession.cs)
- Updated documentation to focus on Claude Desktop use case
- Improved GitHub Actions workflow (artifacts only on release)

### Removed
- HTTP mode and related CLI options (`--http`, `--ip_addr`, `--port`)
- StreamableHttpServer.cs
- McpSession.cs

### Added
- CONTRIBUTING.md with development guidelines
- CHANGELOG.md for version tracking

## [2.0.0] - 2026-02-04

### Added
- Migrated to official Microsoft.ModelContextProtocol SDK (0.7.0-preview.1)
- E2E test suite with 8 automated tests
- Comprehensive test coverage for all MCP tools
- GitHub Actions workflow for automated testing
- Official MCP protocol compliance
- ImageContentBlock return type for image tools

### Changed
- All MCP tools now return proper MCP protocol content types
- Simplified JSON-RPC handling (SDK-based)
- Updated README.md and README.ja.md for Claude Desktop integration
- Changed default transport to stdio (recommended for Claude Desktop)

### Fixed
- JSON-RPC response format issues
- Zod validation errors in Claude Desktop
- Tool parameter handling

### Removed
- Manual JSON-RPC implementation (replaced by SDK)

## [1.5.0] - 2026-01-XX

### Added
- Window enumeration tool (`list_windows`)
- Window capture tool (`capture_window`)
- Region capture tool (`capture_region`)
- Window capture with PW_RENDERFULLCONTENT flag for GPU-accelerated apps
- Stream sessions for window watching

### Changed
- Improved window capture reliability
- Better error handling for invalid window handles

## [1.4.0] - 2026-01-XX

### Added
- Dual transport support (Streamable HTTP + SSE)
- MCP-Session-Id header for session management
- Session cleanup on connection close
- Stream endpoint (`/stream/{id}`) for continuous capture

### Changed
- Improved HTTP mode stability
- Better error messages for connection issues

## [1.3.0] - 2026-01-XX

### Added
- Graceful shutdown using IHostApplicationLifetime
- Proper cleanup on Ctrl+C or process termination
- Cleanup logging

### Changed
- Improved startup and shutdown experience

## [1.2.0] - 2026-01-XX

### Added
- Unit tests
- CI improvements (GitHub Actions)

### Changed
- Better error handling
- Improved test coverage

## [1.1.0] - 2026-01-XX

### Added
- Tool naming improvements (verbs)
- Input schema for all tools
- Better error handling
- Parameter validation

### Changed
- Improved tool descriptions
- Better error messages

## [1.0.0] - 2026-01-XX

### Added
- Initial SSE-only implementation
- Screen capture tools (`list_monitors`, `see`, `start_watching`, `stop_watching`)
- Basic session management
- GDI+ based screen capture

## [Unreleased] - 2026-01-XX

### Added
- Initial project setup
- Basic MCP server implementation
- Screen capture functionality

[Unreleased]: https://github.com/utenadev/windows-desktop-use-mcp/compare/v3.0.0...HEAD
[3.0.0]: https://github.com/utenadev/windows-desktop-use-mcp/compare/v2.1.0...v3.0.0
[2.1.0]: https://github.com/utenadev/windows-desktop-use-mcp/compare/v2.0.0...v2.1.0
[2.0.0]: https://github.com/utenadev/windows-desktop-use-mcp/compare/v1.5.0...v2.0.0
[1.5.0]: https://github.com/utenadev/windows-desktop-use-mcp/compare/v1.4.0...v1.5.0
[1.4.0]: https://github.com/utenadev/windows-desktop-use-mcp/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/utenadev/windows-desktop-use-mcp/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/utenadev/windows-desktop-use-mcp/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/utenadev/windows-desktop-use-mcp/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/utenadev/windows-desktop-use-mcp/releases/tag/v1.0.0