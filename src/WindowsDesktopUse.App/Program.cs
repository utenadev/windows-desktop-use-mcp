using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using WindowsDesktopUse.App;
using WindowsDesktopUse.Audio;
using WindowsDesktopUse.Input;
using WindowsDesktopUse.Screen;
using WindowsDesktopUse.Transcription;

[DllImport("user32.dll")] static extern bool SetProcessDPIAware();

// Localization helper
var currentCulture = CultureInfo.CurrentCulture;
var isJapanese = currentCulture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase);

string GetText(string en, string ja) => isJapanese ? ja : en;

bool IsRunningAsAdmin()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}

// Create subcommands
var doctorCmd = new Command("doctor", GetText("Diagnose system compatibility", "システム互換性を診断"));
var setupCmd = new Command("setup", GetText("Configure Claude Desktop integration", "Claude Desktop統合を設定"));
var whisperCmd = new Command("whisper", GetText("Manage Whisper AI models", "Whisper AIモデルを管理"));

// Whisper command options
var whisperListOption = new Option<bool>(
    name: "--list",
    description: GetText("Show list of available models only", "利用可能なモデル一覧のみ表示"),
    getDefaultValue: () => false);
whisperCmd.AddOption(whisperListOption);

// Doctor command options
var doctorVerboseOption = new Option<bool>(
    name: "--verbose",
    description: GetText("Show detailed diagnostic information", "詳細な診断情報を表示"),
    getDefaultValue: () => false);
var doctorJsonOption = new Option<bool>(
    name: "--json",
    description: GetText("Output results in JSON format", "結果をJSON形式で出力"),
    getDefaultValue: () => false);
doctorCmd.AddOption(doctorVerboseOption);
doctorCmd.AddOption(doctorJsonOption);

// Setup command options
var setupConfigPathOption = new Option<string?>(
    name: "--config-path",
    description: GetText("Custom path for Claude Desktop config file", "Claude Desktop設定ファイルのカスタムパス"),
    getDefaultValue: () => null);
var setupNoMergeOption = new Option<bool>(
    name: "--no-merge",
    description: GetText("Overwrite existing config instead of merging", "マージせず既存の設定を上書き"),
    getDefaultValue: () => false);
var setupDryRunOption = new Option<bool>(
    name: "--dry-run",
    description: GetText("Show generated config without writing to file", "設定をファイルに書き込まずに表示"),
    getDefaultValue: () => false);
setupCmd.AddOption(setupConfigPathOption);
setupCmd.AddOption(setupNoMergeOption);
setupCmd.AddOption(setupDryRunOption);

// Doctor command
doctorCmd.SetHandler((bool verbose, bool json) =>
{
    if (json)
    {
        var result = new Dictionary<string, object>
        {
            ["status"] = "ok",
            ["checks"] = new List<Dictionary<string, object>>()
        };
        var hasError = false;
        var hasWarning = false;

        // OS check
        var osCheck = new Dictionary<string, object>
        {
            ["name"] = "operating_system",
            ["status"] = "pass",
            ["version"] = Environment.OSVersion.ToString()
        };
        if (Environment.OSVersion.Version.Major >= 10)
        {
            osCheck["message"] = GetText("Windows 10/11 detected", "Windows 10/11を検出");
        }
        else
        {
            osCheck["status"] = "error";
            osCheck["message"] = GetText("Windows 10 or later required", "Windows 10以降が必要");
            hasError = true;
        }
        ((List<Dictionary<string, object>>)result["checks"]).Add(osCheck);

        // Admin privilege check
        var isAdmin = IsRunningAsAdmin();
        var adminCheck = new Dictionary<string, object>
        {
            ["name"] = "administrator_privileges",
            ["status"] = isAdmin ? "pass" : "warning",
            ["isAdmin"] = isAdmin
        };
        if (isAdmin)
        {
            adminCheck["message"] = GetText("Running with administrator privileges", "管理者権限で実行中");
        }
        else
        {
            adminCheck["message"] = GetText("Running without administrator privileges", "管理者権限なしで実行中");
            adminCheck["note"] = GetText("Some features may require administrator privileges", "一部の機能には管理者権限が必要");
        }
        ((List<Dictionary<string, object>>)result["checks"]).Add(adminCheck);
        if (!isAdmin) hasWarning = true;

        // .NET check
        ((List<Dictionary<string, object>>)result["checks"]).Add(new Dictionary<string, object>
        {
            ["name"] = "dotnet_runtime",
            ["status"] = "pass",
            ["version"] = Environment.Version.ToString()
        });

        // Monitors check
        try
        {
            SetProcessDPIAware();
            var captureService = new ScreenCaptureService(0);
            captureService.InitializeMonitors();
            var monitors = captureService.GetMonitors();
            ((List<Dictionary<string, object>>)result["checks"]).Add(new Dictionary<string, object>
            {
                ["name"] = "monitors",
                ["status"] = "pass",
                ["count"] = monitors.Count,
                ["monitors"] = monitors.Select(m => new
                {
                    name = m.Name,
                    width = m.W,
                    height = m.H,
                    x = m.X,
                    y = m.Y
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            var monitorCheck = new Dictionary<string, object>
            {
                ["name"] = "monitors",
                ["status"] = "error",
                ["message"] = GetText("Screen capture test failed", "画面キャプチャテスト失敗"),
                ["error"] = ex.Message
            };
            if (verbose) monitorCheck["stackTrace"] = ex.StackTrace;
            ((List<Dictionary<string, object>>)result["checks"]).Add(monitorCheck);
            hasError = true;
        }

        // Audio devices check
        try
        {
            var devices = AudioCaptureService.GetAudioDevices();
            ((List<Dictionary<string, object>>)result["checks"]).Add(new Dictionary<string, object>
            {
                ["name"] = "audio_devices",
                ["status"] = "pass",
                ["count"] = devices.Count
            });
        }
        catch (Exception ex)
        {
            var audioCheck = new Dictionary<string, object>
            {
                ["name"] = "audio_devices",
                ["status"] = "warning",
                ["message"] = GetText("Audio device detection failed", "オーディオデバイス検出失敗"),
                ["error"] = ex.Message
            };
            if (!isAdmin)
            {
                audioCheck["note"] = GetText("This may require administrator privileges", "これは管理者権限が必要な可能性があります");
                audioCheck["suggestion"] = GetText("Run as administrator", "管理者として実行してください");
            }
            if (verbose) audioCheck["stackTrace"] = ex.StackTrace;
            ((List<Dictionary<string, object>>)result["checks"]).Add(audioCheck);
            hasWarning = true;
        }

        // Whisper models check
        try
        {
            var modelDir = Path.Combine(AppContext.BaseDirectory, "models");
            var whisperCheck = new Dictionary<string, object>
            {
                ["name"] = "whisper_models",
                ["status"] = "warning",
                ["message"] = GetText("Model directory not found", "モデルディレクトリが見つかりません")
            };
            if (Directory.Exists(modelDir))
            {
                var models = Directory.GetFiles(modelDir, "*.bin");
                if (models.Length > 0)
                {
                    whisperCheck["status"] = "pass";
                    whisperCheck["message"] = GetText($"{models.Length} model(s) found", $"{models.Length}個のモデルを検出");
                    whisperCheck["models"] = models.Select(m => new
                    {
                        name = Path.GetFileName(m),
                        sizeMb = new FileInfo(m).Length / (1024 * 1024)
                    }).ToList();
                }
                else
                {
                    whisperCheck["message"] = GetText("No models found. Run 'WindowsDesktopUse whisper' to download.", "モデルが見つかりません。'WindowsDesktopUse whisper'でダウンロードしてください。");
                }
            }
            ((List<Dictionary<string, object>>)result["checks"]).Add(whisperCheck);
        }
        catch (Exception ex)
        {
            var whisperCheck = new Dictionary<string, object>
            {
                ["name"] = "whisper_models",
                ["status"] = "warning",
                ["message"] = GetText("Whisper check failed", "Whisperチェック失敗"),
                ["error"] = ex.Message
            };
            if (verbose) whisperCheck["stackTrace"] = ex.StackTrace;
            ((List<Dictionary<string, object>>)result["checks"]).Add(whisperCheck);
            hasWarning = true;
        }

        if (hasError) result["status"] = "error";
        else if (hasWarning) result["status"] = "warning";

        var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, jsonOptions));
        Environment.Exit(hasError ? 1 : 0);
    }
    else
    {
        Console.WriteLine(GetText(
            "🔍 Windows Desktop Use - System Diagnostics",
            "🔍 Windows Desktop Use - システム診断"));
        Console.WriteLine(GetText(
            "==========================================",
            "=========================================="));
        Console.WriteLine();

        var hasError = false;
        var hasWarning = false;

        // Check OS
        Console.WriteLine($"✓ {GetText("Operating System", "オペレーティングシステム")}: {Environment.OSVersion}");
        if (Environment.OSVersion.Version.Major >= 10)
        {
            Console.WriteLine($"  ✓ {GetText("Windows 10/11 detected", "Windows 10/11を検出")}");
        }
        else
        {
            Console.WriteLine($"  ✗ {GetText("Windows 10 or later required", "Windows 10以降が必要")}");
            hasError = true;
        }

        // Check .NET
        Console.WriteLine($"✓ {GetText(".NET Runtime", ".NETランタイム")}: {Environment.Version}");

        // Check administrator privileges
        var isAdmin = IsRunningAsAdmin();
        if (isAdmin)
        {
            Console.WriteLine($"✓ {GetText("Administrator Privileges", "管理者権限")}: {GetText("Enabled", "有効")}");
            Console.WriteLine($"  ✓ {GetText("Running with administrator privileges", "管理者権限で実行中")}");
        }
        else
        {
            Console.WriteLine($"⚠ {GetText("Administrator Privileges", "管理者権限")}: {GetText("Not available", "なし")}");
            Console.WriteLine($"  ⚠ {GetText("Running without administrator privileges", "管理者権限なしで実行中")}");
            Console.WriteLine($"  ⚠ {GetText("Some features may require administrator privileges", "一部の機能には管理者権限が必要")}");
            hasWarning = true;
        }

        // Check monitors
        try
        {
            SetProcessDPIAware();
            var captureService = new ScreenCaptureService(0);
            captureService.InitializeMonitors();
            var monitors = captureService.GetMonitors();
            Console.WriteLine($"✓ {GetText("Displays detected", "ディスプレイ検出")}: {monitors.Count}");
            foreach (var mon in monitors)
            {
                Console.WriteLine($"  - {mon.Name}: {mon.W}x{mon.H} at ({mon.X},{mon.Y})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ {GetText("Screen capture test failed", "画面キャプチャテスト失敗")}: {ex.Message}");
            if (verbose) Console.WriteLine($"    {ex.StackTrace}");
            hasError = true;
        }

        // Check audio devices
        try
        {
            var devices = AudioCaptureService.GetAudioDevices();
            Console.WriteLine($"✓ {GetText("Audio devices", "オーディオデバイス")}: {devices.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠ {GetText("Audio device detection failed", "オーディオデバイス検出失敗")}: {ex.Message}");
            if (!isAdmin)
            {
                Console.WriteLine($"  ⚠ {GetText("This may require administrator privileges", "これは管理者権限が必要な可能性があります")}");
                Console.WriteLine($"  ⚠ {GetText("Please run as administrator: Right-click -> Run as administrator", "管理者として実行してください：右クリック -> 管理者として実行")}");
            }
            if (verbose) Console.WriteLine($"    {ex.StackTrace}");
            hasWarning = true;
        }

        // Check Whisper models
        Console.WriteLine($"✓ {GetText("Whisper AI Models", "Whisper AIモデル")}:");
        try
        {
            var modelDir = Path.Combine(AppContext.BaseDirectory, "models");
            if (Directory.Exists(modelDir))
            {
                var models = Directory.GetFiles(modelDir, "*.bin");
                if (models.Length > 0)
                {
                    Console.WriteLine($"  ✓ {GetText($"{models.Length} model(s) found", $"{models.Length}個のモデルを検出")}");
                    foreach (var model in models)
                    {
                        var fileName = Path.GetFileName(model);
                        var size = new FileInfo(model).Length / (1024 * 1024);
                        if (verbose)
                        {
                            var fullPath = Path.GetFullPath(model);
                            Console.WriteLine($"    - {fileName} ({size} MB)");
                            Console.WriteLine($"      Path: {fullPath}");
                        }
                        else
                        {
                            Console.WriteLine($"    - {fileName} ({size} MB)");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"  ⚠ {GetText("No models found. Run 'WindowsDesktopUse whisper' to download.", "モデルが見つかりません。'WindowsDesktopUse whisper'でダウンロードしてください。")}");
                    hasWarning = true;
                }
            }
            else
            {
                Console.WriteLine($"  ⚠ {GetText("Model directory not found. Run 'WindowsDesktopUse whisper' to setup.", "モデルディレクトリが見つかりません。'WindowsDesktopUse whisper'でセットアップしてください。")}");
                hasWarning = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠ {GetText("Whisper check failed", "Whisperチェック失敗")}: {ex.Message}");
            if (verbose) Console.WriteLine($"    {ex.StackTrace}");
            hasWarning = true;
        }

        Console.WriteLine();
        if (hasError)
        {
            Console.WriteLine(GetText("❌ Diagnostics completed with errors", "❌ 診断がエラーで完了しました"));
            Environment.Exit(1);
        }
        else if (hasWarning)
        {
            Console.WriteLine(GetText("⚠️  Diagnostics completed with warnings", "⚠️  診断が警告付きで完了しました"));
            Console.WriteLine();
            Console.WriteLine(GetText("You can continue, but some features may not work correctly.", "続行できますが、一部の機能が正常に動作しない可能性があります。"));
        }
        else
        {
            Console.WriteLine(GetText("✅ All diagnostics passed!", "✅ すべての診断が合格しました！"));
        }

        if (!verbose)
        {
            Console.WriteLine();
            Console.WriteLine(GetText("Run with --verbose for more details.", "詳細情報を表示するには--verboseを指定してください。"));
        }

        Console.WriteLine();
        Console.WriteLine(GetText("Next steps:", "次のステップ："));
        Console.WriteLine(GetText("  1. Run 'WindowsDesktopUse setup' to configure Claude Desktop", "  1. 'WindowsDesktopUse setup'を実行してClaude Desktopを設定"));
        Console.WriteLine(GetText("  2. Start Claude Desktop and begin using WindowsDesktopUse", "  2. Claude Desktopを起動してWindowsDesktopUseを使用開始"));
    }
}, doctorVerboseOption, doctorJsonOption);

// Setup command
setupCmd.SetHandler((string? configPath, bool noMerge, bool dryRun) =>
{
    Console.WriteLine(GetText(
        "🔧 Windows Desktop Use - Setup",
        "🔧 Windows Desktop Use - セットアップ"));
    Console.WriteLine(GetText(
        "==============================",
        "=============================="));
    Console.WriteLine();

    // Get executable path using Process
    var exePath = Process.GetCurrentProcess().MainModule?.FileName;
    if (string.IsNullOrEmpty(exePath))
    {
        exePath = Path.Combine(AppContext.BaseDirectory, "WindowsDesktopUse.exe");
    }

    var targetConfigPath = configPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Claude", "claude_desktop_config.json");

    Console.WriteLine($"{GetText("Executable", "実行ファイル")}: {exePath}");
    Console.WriteLine($"{GetText("Config file", "設定ファイル")}: {targetConfigPath}");
    Console.WriteLine();

    if (noMerge)
    {
        Console.WriteLine(GetText("⚠️  --no-merge option enabled: Existing configuration will be overwritten.", "⚠️  --no-mergeオプション有効: 既存の設定は上書きされます。"));
        Console.WriteLine();
    }

    // Check existing config
    var existingConfig = new Dictionary<string, object>();
    if (File.Exists(targetConfigPath))
    {
        Console.WriteLine(GetText("⚠️  Existing configuration found!", "⚠️  既存の設定が見つかりました！"));
        if (noMerge)
        {
            Console.WriteLine(GetText("Existing configuration will be overwritten due to --no-merge.", "--no-mergeにより既存の設定は上書きされます。"));
        }
        else
        {
            try
            {
                var existingJson = File.ReadAllText(targetConfigPath);
                existingConfig = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson) ?? new Dictionary<string, object>();

                if (existingConfig.ContainsKey("mcpServers"))
                {
                    Console.WriteLine(GetText("Existing MCP servers will be preserved.", "既存のMCPサーバー設定は保持されます。"));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(GetText("⚠️  Could not read existing config. It may be overwritten.", "⚠️  既存設定を読み込めません。上書きされる可能性があります。"));
                Console.WriteLine($"  {ex.Message}");
            }
        }
        Console.WriteLine();
    }

    // Build new config
    var newMcpServer = new
    {
        command = exePath,
        args = new[] { "--httpPort", "5000" }
    };

    Dictionary<string, object> config;
    if (noMerge)
    {
        config = new Dictionary<string, object>
        {
            ["mcpServers"] = new Dictionary<string, object>
            {
                ["windowsDesktopUse"] = newMcpServer
            }
        };
    }
    else
    {
        Dictionary<string, object> mcpServers;
        if (existingConfig.TryGetValue("mcpServers", out var existingMcpObj) && existingMcpObj is Dictionary<string, object> existingMcp)
        {
            mcpServers = existingMcp;
            mcpServers["windowsDesktopUse"] = newMcpServer;
        }
        else
        {
            mcpServers = new Dictionary<string, object>
            {
                ["windowsDesktopUse"] = newMcpServer
            };
        }

        config = new Dictionary<string, object>(existingConfig);
        config["mcpServers"] = mcpServers;
    }

    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true
    };
    var json = System.Text.Json.JsonSerializer.Serialize(config, jsonOptions);

    Console.WriteLine(GetText("Generated configuration:", "生成された設定："));
    Console.WriteLine(GetText("------------------------", "------------------------"));
    Console.WriteLine(json);
    Console.WriteLine(GetText("------------------------", "------------------------"));

    if (!dryRun)
    {
        Console.WriteLine();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetConfigPath)!);
            File.WriteAllText(targetConfigPath, json);
            Console.WriteLine(GetText("✅ Configuration saved to Claude Desktop!", "✅ Claude Desktopに設定を保存しました！"));
            Console.WriteLine();
            Console.WriteLine(GetText("Please restart Claude Desktop to apply changes.", "変更を適用するにはClaude Desktopを再起動してください。"));
        }
        catch (Exception ex)
        {
            Console.WriteLine(GetText($"✗ Failed to save configuration: {ex.Message}", $"✗ 設定の保存に失敗しました: {ex.Message}"));
            Console.WriteLine();
            Console.WriteLine(GetText("Please manually add the above configuration to:", "上記の設定を手動で以下に追加してください："));
            Console.WriteLine(targetConfigPath);
            Environment.Exit(1);
        }
    }
}, setupConfigPathOption, setupNoMergeOption, setupDryRunOption);

// Whisper command
whisperCmd.SetHandler((bool list) =>
{
    var modelDir = Path.Combine(AppContext.BaseDirectory, "models");
    Directory.CreateDirectory(modelDir);

    if (list)
    {
        var availableModels = WhisperTranscriptionService.GetModelInfo();
        foreach (var kvp in availableModels)
        {
            var size = kvp.Key.ToString().ToUpperInvariant();
            Console.WriteLine($"{size}: {kvp.Value.Size} - {kvp.Value.Performance}");
        }
        return;
    }

    Console.WriteLine(GetText(
        "🎤 Windows Desktop Use - Whisper Setup",
        "🎤 Windows Desktop Use - Whisperセットアップ"));
    Console.WriteLine(GetText(
        "=======================================",
        "======================================="));
    Console.WriteLine();

    Console.WriteLine(GetText($"Model directory: {modelDir}", $"モデルディレクトリ: {modelDir}"));
    Console.WriteLine();

    // Show available models
    var models = WhisperTranscriptionService.GetModelInfo();
    Console.WriteLine(GetText("Available Whisper models:", "利用可能なWhisperモデル："));
    foreach (var kvp in models)
    {
        var size = kvp.Key.ToString().ToUpperInvariant();
        Console.WriteLine($"  - {size}: {kvp.Value.Size} - {kvp.Value.Performance}");
    }
    Console.WriteLine();

    // Check existing models
    var existingModels = Directory.GetFiles(modelDir, "ggml-*.bin")
        .Select(f => Path.GetFileName(f))
        .ToList();

    if (existingModels.Count > 0)
    {
        Console.WriteLine(GetText("Installed models:", "インストール済みモデル："));
        foreach (var model in existingModels)
        {
            Console.WriteLine($"  ✓ {model}");
        }
    }
    else
    {
        Console.WriteLine(GetText("No models installed.", "モデルがインストールされていません。"));
    }
    Console.WriteLine();

    Console.WriteLine(GetText("To download a model, use the 'listen' tool in Claude Desktop.", "モデルをダウンロードするには、Claude Desktopで'listen'ツールを使用してください。"));
    Console.WriteLine(GetText("The model will be automatically downloaded on first use.", "初回使用時に自動的にダウンロードされます。"));
}, whisperListOption);

// Main server command options
var desktopOption = new Option<uint>(
    name: "--desktopNum",
    description: GetText("Default monitor index (0=primary)", "デフォルトモニター番号（0=プライマリ）"),
    getDefaultValue: () => 0u);

var httpPortOption = new Option<int>(
    name: "--httpPort",
    description: GetText("HTTP server port for frame streaming (0=disable)", "フレームストリーミング用HTTPサーバーポート（0=無効）"),
    getDefaultValue: () => 5000);

var testOption = new Option<bool>(
    name: "--test-whisper",
    description: GetText("Test Whisper transcription directly", "Whisper文字起こしを直接テスト"),
    getDefaultValue: () => false);

// Root command with subcommands
var rootCmd = new RootCommand(GetText("Windows Desktop Use MCP Server", "Windows Desktop Use MCPサーバー"));
rootCmd.AddCommand(doctorCmd);
rootCmd.AddCommand(setupCmd);
rootCmd.AddCommand(whisperCmd);

// Add server options to root command (default behavior)
rootCmd.AddOption(desktopOption);
rootCmd.AddOption(httpPortOption);
rootCmd.AddOption(testOption);

// Create a service provider to be used by the tools
ServiceProvider? serviceProvider = null;

rootCmd.SetHandler(async (desktop, httpPort, testWhisper) =>
{
    SetProcessDPIAware();

    // Create service collection and register services
    var services = new ServiceCollection();

    // Register singleton services
    services.AddSingleton<ScreenCaptureService>(provider =>
    {
        var service = new ScreenCaptureService(desktop);
        service.InitializeMonitors();
        return service;
    });
    services.AddSingleton<AudioCaptureService>();
    services.AddSingleton<WhisperTranscriptionService>();
    services.AddSingleton<InputService>();

    serviceProvider = services.BuildServiceProvider();

    // Initialize static tools with services
    var captureService = serviceProvider.GetRequiredService<ScreenCaptureService>();
    var audioCaptureService = serviceProvider.GetRequiredService<AudioCaptureService>();
    var whisperService = serviceProvider.GetRequiredService<WhisperTranscriptionService>();
    var inputService = serviceProvider.GetRequiredService<InputService>();

    DesktopUseTools.SetCaptureService(captureService);
    DesktopUseTools.SetAudioCaptureService(audioCaptureService);
    DesktopUseTools.SetWhisperService(whisperService);
    DesktopUseTools.SetInputService(inputService);

    if (testWhisper)
    {
        Console.Error.WriteLine("[TEST] Testing Whisper transcription...");
        Console.Error.WriteLine("[TEST] Please play audio on YouTube! Starting in 3 seconds...");
        await Task.Delay(3000).ConfigureAwait(false); // Use async delay instead of Thread.Sleep

        try
        {
            var result = await DesktopUseTools.Listen(
                source: DesktopUseTools.AudioSourceType.System,
                duration: 30,
                language: "ja",
                modelSize: "small",
                translate: false).ConfigureAwait(false);

            Console.Error.WriteLine($"[TEST] ========================================");
            Console.Error.WriteLine($"[TEST] 検出言語: {result.Language}");
            Console.Error.WriteLine($"[TEST] セグメント数: {result.Segments.Count}");
            Console.Error.WriteLine($"[TEST] 合計時間: {result.Duration.TotalSeconds:F2}秒");
            Console.Error.WriteLine($"[TEST] ========================================");
            Console.Error.WriteLine($"[TEST] 【文字起こし結果】");

            int i = 1;
            foreach (var seg in result.Segments)
            {
                var timeStr = seg.Start.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
                Console.Error.WriteLine($"[TEST] [{i:D2} {timeStr}] {seg.Text}");
                i++;
            }
            Console.Error.WriteLine($"[TEST] ========================================");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TEST] ERROR: {ex.GetType().Name}");
            Console.Error.WriteLine($"[TEST] Message: {ex.Message}");
            Console.Error.WriteLine($"[TEST] Stack: {ex.StackTrace}");
        }

        return;
    }

    Console.Error.WriteLine(GetText("[Stdio] Windows Desktop Use MCP Server started in stdio mode", "[Stdio] Windows Desktop Use MCPサーバーがstdioモードで起動しました"));

    if (httpPort > 0)
    {
        _ = StartHttpServer(captureService, httpPort);
        Console.Error.WriteLine($"[HTTP] Frame streaming server started on http://localhost:{httpPort}");
        Console.Error.WriteLine($"[HTTP] Endpoint: http://localhost:{httpPort}/frame/{{sessionId}}");
    }

    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(new StderrLoggerProvider());
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(typeof(DesktopUseTools).Assembly);

    var host = builder.Build();
    host.Run();
}, desktopOption, httpPortOption, testOption);

await rootCmd.InvokeAsync(args).ConfigureAwait(false);

static async Task StartHttpServer(ScreenCaptureService captureService, int port)
{
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.Services.AddSingleton(captureService);

    var app = builder.Build();

    app.MapGet("/frame/{sessionId}", (string sessionId, ScreenCaptureService svc) =>
    {
        if (!svc.TryGetSession(sessionId, out var session) || session == null)
        {
            return Results.NotFound(new { error = "Session not found" });
        }

        var frameData = session.LatestFrame;
        if (string.IsNullOrEmpty(frameData))
        {
            return Results.NotFound(new { error = "No frame captured yet" });
        }

        try
        {
            var imageBytes = Convert.FromBase64String(frameData);
            return Results.Bytes(imageBytes, "image/jpeg");
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to decode image: {ex.Message}");
        }
    });

    app.MapGet("/frame/{sessionId}/info", (string sessionId, ScreenCaptureService svc) =>
    {
        if (!svc.TryGetSession(sessionId, out var session) || session == null)
        {
            return Results.NotFound(new { error = "Session not found" });
        }

        return Results.Ok(new
        {
            sessionId = sessionId,
            hasFrame = !string.IsNullOrEmpty(session.LatestFrame),
            hash = session.LastFrameHash,
            captureTime = session.LastCaptureTime.ToString("O"),
            targetType = session.TargetType,
            interval = session.Interval
        });
    });

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

    app.MapGet("/", () => Results.Ok(new
    {
        message = "Windows Desktop Use MCP HTTP Server",
        endpoints = new
        {
            frame = "/frame/{sessionId} - Get latest frame as JPEG image",
            frameInfo = "/frame/{sessionId}/info - Get frame metadata (hash, timestamp)",
            health = "/health - Health check"
        },
        usage = "Use start_watching tool to create a session, then access /frame/{sessionId}"
    }));

    await app.RunAsync($"http://localhost:{port}").ConfigureAwait(false);
}

public class StderrLoggerProvider : ILoggerProvider
{
    private bool _disposed = false;

    public ILogger CreateLogger(string categoryName) => new StderrLogger(categoryName);

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

public class StderrLogger : ILogger
{
    private readonly string _category;
    public StderrLogger(string category) => _category = category;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var message = formatter(state, exception);
        Console.Error.WriteLine($"[{logLevel}] {_category}: {message}");
    }
}

public class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new NullScope();
    private bool _disposed = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
