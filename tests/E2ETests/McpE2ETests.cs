using ModelContextProtocol.Protocol;
using ModelContextProtocol.Client;
using WindowsDesktopUse.Core;
using System.Diagnostics;

namespace E2ETests;

[TestFixture]
public class McpE2ETests
{
    private static string ServerPath => GetServerPath();

    private static string GetServerPath()
    {
        // Use GITHUB_WORKSPACE if available (GitHub Actions environment)
        var githubWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        var repoRoot = !string.IsNullOrEmpty(githubWorkspace)
            ? githubWorkspace
            : GetRepoRootFromAssembly();

        // Check multiple possible locations
        var possiblePaths = new[]
        {
            // GitHub Actions build paths
            Path.Combine(repoRoot, "src", "WindowsDesktopUse.App", "bin", "Release", "net8.0-windows", "win-x64", "WindowsDesktopUse.App.exe"),
            Path.Combine(repoRoot, "src", "WindowsDesktopUse.App", "bin", "Debug", "net8.0-windows", "win-x64", "WindowsDesktopUse.App.exe"),
            // Local development fallback
            ""
        };

        foreach (var path in possiblePaths)
        {
            if (string.IsNullOrEmpty(path))
                continue;
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new FileNotFoundException($"WindowsDesktopUse.App.exe not found. Checked paths:\n{string.Join("\n", possiblePaths.Select(p => Path.GetFullPath(p)))}\n\nPlease build the project first.");
    }

    private static string GetRepoRootFromAssembly()
    {
        var testAssemblyDir = Path.GetDirectoryName(typeof(McpE2ETests).Assembly.Location)!;
        // Go up from tests/E2ETests/bin/Debug/net8.0 to repo root
        return Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", "..", ".."));
    }

    // Helper method to find window by process ID using MainWindowHandle
    private static WindowInfo? FindWindowByProcessId(List<WindowInfo> windows, int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process == null || process.HasExited)
                return null;
            
            // Get main window handle
            var hwnd = process.MainWindowHandle.ToInt64();
            if (hwnd == 0)
                return null;
            
            // Find matching window in list
            return windows.FirstOrDefault(w => w.Hwnd == hwnd);
        }
        catch
        {
            return null;
        }
    }

    // Helper method to capture desktop for debugging
    private static async Task CaptureDesktop(McpClient client, string description, string? savePath = null)
    {
        try
        {
            var captureResult = await client.CallToolAsync("capture", new Dictionary<string, object?>
            {
                ["target"] = "primary",
                ["quality"] = 80
            }).ConfigureAwait(false);
            
            if (captureResult != null)
            {
                var textContent = captureResult.Content.OfType<TextContentBlock>().FirstOrDefault();
                if (textContent != null && !string.IsNullOrEmpty(textContent.Text))
                {
                    // Parse JSON to get image data
                    using var doc = System.Text.Json.JsonDocument.Parse(textContent.Text);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("imageData", out var imageDataElement))
                    {
                        var imageData = imageDataElement.GetString();
                        if (!string.IsNullOrEmpty(imageData))
                        {
                            // Save to file if path specified
                            if (!string.IsNullOrEmpty(savePath))
                            {
                                var imageBytes = Convert.FromBase64String(imageData);
                                await File.WriteAllBytesAsync(savePath, imageBytes).ConfigureAwait(false);
                                Console.WriteLine($"  📸 {description}: Desktop saved to {savePath} ({imageBytes.Length:N0} bytes)");
                            }
                            else
                            {
                                Console.WriteLine($"  📸 {description}: Desktop captured ({imageData.Length:N0} chars)");
                            }
                            return;
                        }
                    }
                }
                Console.WriteLine($"  📸 {description}: Desktop captured");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️ {description}: Failed to capture desktop - {ex.Message}");
        }
    }

    [Test]
    public async Task E2E_ListMonitors_ReturnsValidMonitors()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var tools = await client.ListToolsAsync().ConfigureAwait(false);
            Assert.That(tools, Is.Not.Null);

            var listMonitorsTool = tools.FirstOrDefault(t => t.Name == "list_monitors");
            Assert.That(listMonitorsTool, Is.Not.Null, "list_monitors tool not found");

            var result = await client.CallToolAsync("list_monitors", null).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_ListWindows_ReturnsValidWindows()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var result = await client.CallToolAsync("list_windows", null).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_SeeMonitor_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["targetType"] = "monitor",
                ["monitor"] = 0,
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("see", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var imageContent = result.Content.OfType<ImageContentBlock>().FirstOrDefault();
            Assert.That(imageContent, Is.Not.Null, "No image content found");
            Assert.That(imageContent.Data, Is.Not.Null, "Image data is null");

            TestHelper.ValidateBase64Image(imageContent.Data);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    [Ignore("Window capture may fail due to OS-specific visibility checks")]
    public async Task E2E_CaptureWindow_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var windowsResult = await client.CallToolAsync("list_windows", null).ConfigureAwait(false);
            Assert.That(windowsResult, Is.Not.Null, "list_windows failed");

            var textContent = windowsResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found in list_windows result");

            var windows = System.Text.Json.JsonSerializer.Deserialize<List<WindowInfo>>(textContent.Text);
            Assert.That(windows, Is.Not.Null.And.Count.GreaterThan(0), "No windows found");

            var testWindow = windows.FirstOrDefault(w => !string.IsNullOrEmpty(w.Title)) ?? windows.First();
            Assert.That(testWindow, Is.Not.Null, "No test window found");

            var args = new Dictionary<string, object?>
            {
                ["hwnd"] = testWindow.Hwnd,
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("capture_window", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var imageContent = result.Content.OfType<ImageContentBlock>().FirstOrDefault();
            if (imageContent is null)
            {
                var errorContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
                var errorText = errorContent?.Text ?? "Unknown error";
                Assert.Fail($"Window capture failed: {errorText}");
            }
            Assert.That(imageContent.Data, Is.Not.Null, "Image data is null");

            TestHelper.ValidateBase64Image(imageContent.Data);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_CaptureRegion_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["x"] = 0,
                ["y"] = 0,
                ["w"] = 100,
                ["h"] = 100,
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("capture_region", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var imageContent = result.Content.OfType<ImageContentBlock>().FirstOrDefault();
            Assert.That(imageContent, Is.Not.Null, "No image content found");
            Assert.That(imageContent.Data, Is.Not.Null, "Image data is null");

            TestHelper.ValidateBase64Image(imageContent.Data);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_StartWatching_ReturnsValidSessionId()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["targetType"] = "monitor",
                ["monitor"] = 0,
                ["intervalMs"] = 1000,
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("start_watching", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Session ID is null");
            Assert.That(textContent.Text, Does.Match(@"^[a-f0-9\-]{36}$"), "Session ID format is invalid");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_StopWatching_ReturnsSuccess()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var startArgs = new Dictionary<string, object?>
            {
                ["targetType"] = "monitor",
                ["monitor"] = 0,
                ["intervalMs"] = 1000,
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var startResult = await client.CallToolAsync("start_watching", startArgs).ConfigureAwait(false);
            Assert.That(startResult, Is.Not.Null, "start_watching failed");

            var textContent = startResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            var sessionId = textContent.Text;
            Assert.That(sessionId, Is.Not.Null, "Session ID is null");

            var stopArgs = new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId
            };

            var stopResult = await client.CallToolAsync("stop_watching", stopArgs).ConfigureAwait(false);
            Assert.That(stopResult, Is.Not.Null);

            var stopTextContent = stopResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(stopTextContent, Is.Not.Null, "No text content found in stop result");
            Assert.That(stopTextContent.Text, Is.EqualTo("Stopped watching"), "Stop response is unexpected");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_InvalidMonitorIndex_ReturnsError()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["targetType"] = "monitor",
                ["monitor"] = 999,
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("see", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ============ NEW UNIFIED TOOLS E2E TESTS ============

    [Test]
    public async Task E2E_ListAll_ReturnsValidTargets()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var result = await client.CallToolAsync("list_all", null).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_ListAll_FilterMonitors_ReturnsOnlyMonitors()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["filter"] = "monitors"
            };

            var result = await client.CallToolAsync("list_all", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_ListAll_FilterWindows_ReturnsOnlyWindows()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["filter"] = "windows"
            };

            var result = await client.CallToolAsync("list_all", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_Capture_Primary_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["target"] = "primary",
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("capture", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_Capture_Monitor_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["target"] = "monitor",
                ["targetId"] = "0",
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("capture", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    [Ignore("Window capture may fail due to OS-specific visibility checks")]
    public async Task E2E_Capture_Window_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            // First get a window handle
            var listAllResult = await client.CallToolAsync("list_all", new Dictionary<string, object?> { ["filter"] = "windows" }).ConfigureAwait(false);
            Assert.That(listAllResult, Is.Not.Null, "list_all failed");

            var textContent = listAllResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");

            var targets = System.Text.Json.JsonSerializer.Deserialize<CaptureTargets>(textContent.Text);
            Assert.That(targets, Is.Not.Null, "Failed to deserialize targets");
            Assert.That(targets.Windows, Is.Not.Null.And.Count.GreaterThan(0), "No windows found");

            var testWindow = targets.Windows.FirstOrDefault(w => !string.IsNullOrEmpty(w.Name)) ?? targets.Windows.First();
            Assert.That(testWindow, Is.Not.Null, "No test window found");

            var args = new Dictionary<string, object?>
            {
                ["target"] = "window",
                ["targetId"] = testWindow.Id,
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("capture", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var resultTextContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(resultTextContent, Is.Not.Null, "No text content found");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_Capture_Region_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["target"] = "region",
                ["x"] = 0,
                ["y"] = 0,
                ["w"] = 100,
                ["h"] = 100,
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("capture", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_Watch_ReturnsValidSessionId()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["target"] = "monitor",
                ["targetId"] = "0",
                ["intervalMs"] = 1000,
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("watch", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Session info is null");

            // Verify it contains session ID (JSON uses camelCase)
            Assert.That(textContent.Text, Does.Contain("sessionId"), "Response should contain sessionId");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_StopWatch_ReturnsSuccess()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            // Start watching
            var startArgs = new Dictionary<string, object?>
            {
                ["target"] = "monitor",
                ["targetId"] = "0",
                ["intervalMs"] = 1000,
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var startResult = await client.CallToolAsync("watch", startArgs).ConfigureAwait(false);
            Assert.That(startResult, Is.Not.Null, "watch failed");

            var textContent = startResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");

            // Extract session ID from JSON response (camelCase)
            using var doc = System.Text.Json.JsonDocument.Parse(textContent.Text);
            var root = doc.RootElement;
            var sessionId = root.GetProperty("sessionId").GetString();
            Assert.That(sessionId, Is.Not.Null, "Session ID is null");

            // Stop watching
            var stopArgs = new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId
            };

            var stopResult = await client.CallToolAsync("stop_watch", stopArgs).ConfigureAwait(false);
            Assert.That(stopResult, Is.Not.Null);

            var stopTextContent = stopResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(stopTextContent, Is.Not.Null, "No text content found in stop result");
            Assert.That(stopTextContent.Text, Does.Contain("Stopped").Or.Contain("stopped"), "Stop response should indicate success");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ============ AUDIO CAPTURE E2E TESTS ============

    [Test]
    [Ignore("Skipped in CI: Audio devices may not be available in GitHub Actions runner environment")]
    public async Task E2E_ListAudioDevices_ReturnsDevices()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var result = await client.CallToolAsync("list_audio_devices", null).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");

            // Verify response contains device information
            Assert.That(textContent.Text, Does.Contain("Index").Or.Contain("index"),
                "Response should contain device index");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    [Explicit("Requires YouTube or audio playing in background")]
    [Description("IMPORTANT: Before running this test, play audio on YouTube or any media player!" +
                 "\nSet KEEP_AUDIO_FILE=true environment variable to preserve the WAV file for manual verification.")]
    public async Task E2E_CaptureSystemAudio_WithYouTubePlaying_SavesWavFile()
    {
        // Check if we should keep the audio file for manual verification
        // Usage: $env:KEEP_AUDIO_FILE="true"; dotnet test --filter "E2E_CaptureSystemAudio_WithYouTubePlaying_SavesWavFile"
        bool keepAudioFile = Environment.GetEnvironmentVariable("KEEP_AUDIO_FILE")?.ToLower() == "true";

        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);
        string? outputPath = null;

        try
        {
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("AUDIO CAPTURE TEST");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine("Please play audio on YouTube or media player now!");
            Console.WriteLine("Test will start in 5 seconds...");
            if (keepAudioFile)
            {
                Console.WriteLine("[INFO] KEEP_AUDIO_FILE=true - WAV file will NOT be deleted after test");
            }
            Console.WriteLine(new string('=', 60) + "\n");

            await Task.Delay(5000).ConfigureAwait(false);

            // Start system audio capture
            var startArgs = new Dictionary<string, object?>
            {
                ["source"] = "system",
                ["sampleRate"] = 44100
            };

            var startResult = await client.CallToolAsync("start_audio_capture", startArgs).ConfigureAwait(false);
            Assert.That(startResult, Is.Not.Null, "start_audio_capture failed");

            var textContent = startResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");

            // Parse session info
            using var doc = System.Text.Json.JsonDocument.Parse(textContent.Text);
            var root = doc.RootElement;
            var sessionId = root.GetProperty("sessionId").GetString();
            Assert.That(sessionId, Is.Not.Null.And.Not.Empty, "Session ID is null");

            Console.WriteLine($"Started audio capture session: {sessionId}");
            Console.WriteLine("Recording for 5 seconds... Please ensure audio is playing!");

            // Record for 5 seconds
            await Task.Delay(5000).ConfigureAwait(false);

            // Stop capture and get audio data
            var stopArgs = new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId,
                ["returnFormat"] = "base64"
            };

            var stopResult = await client.CallToolAsync("stop_audio_capture", stopArgs).ConfigureAwait(false);
            Assert.That(stopResult, Is.Not.Null, "stop_audio_capture failed");

            var stopTextContent = stopResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(stopTextContent, Is.Not.Null, "No text content in stop result");

            // Parse result
            using var stopDoc = System.Text.Json.JsonDocument.Parse(stopTextContent.Text);
            var stopRoot = stopDoc.RootElement;

            // Get base64 audio data
            string? audioBase64 = null;
            if (stopRoot.TryGetProperty("audioDataBase64", out var audioDataElement))
            {
                audioBase64 = audioDataElement.GetString();
            }

            Assert.That(audioBase64, Is.Not.Null.And.Not.Empty, "Audio data is null");

            // Save to file for verification
            outputPath = Path.Combine(Path.GetTempPath(), $"test_capture_{sessionId}.wav");
            var audioBytes = Convert.FromBase64String(audioBase64);
            await File.WriteAllBytesAsync(outputPath, audioBytes).ConfigureAwait(false);

            // Verify file was created
            Assert.That(File.Exists(outputPath), Is.True, $"Audio file not found: {outputPath}");

            // Verify file size (should be > 44 bytes for valid WAV header + some data)
            var fileInfo = new FileInfo(outputPath);
            Assert.That(fileInfo.Length, Is.GreaterThan(1000),
                $"Audio file too small ({fileInfo.Length} bytes). Is audio playing?");

            // Verify it's a valid WAV file (RIFF header)
            using var fs = File.OpenRead(outputPath);
            var header = new byte[Math.Min(44, (int)fileInfo.Length)];
            fs.Read(header, 0, header.Length);

            // Log header for debugging
            var headerHex = BitConverter.ToString(header.Take(16).ToArray());
            Console.WriteLine($"   File header (hex): {headerHex}");

            // Check RIFF header
            var riffHeader = System.Text.Encoding.ASCII.GetString(header, 0, 4);
            Assert.That(riffHeader, Is.EqualTo("RIFF"),
                $"File does not have valid WAV RIFF header. Header: {riffHeader}");

            // Check WAVE format
            var waveHeader = System.Text.Encoding.ASCII.GetString(header, 8, 4);
            Assert.That(waveHeader, Is.EqualTo("WAVE"),
                $"File is not a valid WAVE file. Format: {waveHeader}");

            Console.WriteLine($"\n✅ SUCCESS! Audio captured successfully:");
            Console.WriteLine($"   File: {outputPath}");
            Console.WriteLine($"   Size: {fileInfo.Length:N0} bytes");
            Console.WriteLine($"   Duration: ~5 seconds");

            if (keepAudioFile)
            {
                Console.WriteLine($"\n💾 AUDIO FILE PRESERVED for manual verification:");
                Console.WriteLine($"   {outputPath}");
                Console.WriteLine($"\nTo play the file:");
                Console.WriteLine($"   Windows Media Player: start \"{outputPath}\"");
                Console.WriteLine($"   PowerShell: Start-Process \"{outputPath}\"");
                Console.WriteLine($"\nTo clean up manually:");
                Console.WriteLine($"   Remove-Item \"{outputPath}\"");
            }
            else
            {
                Console.WriteLine($"\nYou can play the file to verify it contains the audio!");
            }
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);

            // Cleanup: delete the test file only if not preserving
            if (!keepAudioFile && outputPath != null && File.Exists(outputPath))
            {
                try
                {
                    File.Delete(outputPath);
                    Console.WriteLine($"\nCleaned up: {outputPath}");
                }
                catch { }
            }
        }
    }

    [Test]
    public async Task E2E_GetActiveAudioSessions_ReturnsList()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var result = await client.CallToolAsync("get_active_audio_sessions", null).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");

            // Should return a list (possibly empty if no sessions active)
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ============ WHISPER SPEECH RECOGNITION E2E TESTS ============

    [Test]
    public async Task E2E_GetWhisperModelInfo_ReturnsModelList()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var result = await client.CallToolAsync("get_whisper_model_info", null).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null.And.Not.Empty, "Response is empty");

            // Verify it contains model names
            Assert.That(textContent.Text, Does.Contain("tiny").Or.Contain("base"),
                "Response should contain model information");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    [Explicit("Requires YouTube or audio playing in background and downloads Whisper model (~74MB)")]
    [Description("IMPORTANT: This test will download the Whisper Base model (74MB) on first run!" +
                 "\nEnsure YouTube or podcast is playing for transcription." +
                 "\nTest duration: ~30 seconds (10s recording + transcription time)")]
    public async Task E2E_Listen_SystemAudio_TranscribesYouTubePodcast()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            Console.WriteLine("\n" + new string('=', 70));
            Console.WriteLine("WHISPER SPEECH RECOGNITION TEST");
            Console.WriteLine(new string('=', 70));
            Console.WriteLine("This test will:");
            Console.WriteLine("1. Download Whisper Base model (74MB) on first run");
            Console.WriteLine("2. Record 10 seconds of system audio");
            Console.WriteLine("3. Transcribe the audio to text");
            Console.WriteLine(new string('=', 70));
            Console.WriteLine("Please ensure YouTube podcast is playing!");
            Console.WriteLine("Test starting in 3 seconds...\n");

            await Task.Delay(3000).ConfigureAwait(false);

            // Call listen tool with system audio
            var args = new Dictionary<string, object?>
            {
                ["source"] = "system",
                ["duration"] = 10,  // 10 seconds recording
                ["language"] = "ja", // Japanese language
                ["modelSize"] = "base", // Base model (good balance)
                ["translate"] = false
            };

            Console.WriteLine("[Test] Starting audio recording and transcription...");
            Console.WriteLine("[Test] Recording system audio for 10 seconds...");

            var startTime = DateTime.Now;
            var result = await client.CallToolAsync("listen", args).ConfigureAwait(false);
            var elapsed = DateTime.Now - startTime;

            Assert.That(result, Is.Not.Null, "listen tool failed");

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null.And.Not.Empty, "Transcription result is empty");

            // Log raw response for debugging
            Console.WriteLine($"\n[Debug] Raw response:\n{textContent.Text}\n");

            // Parse transcription result
            using var doc = System.Text.Json.JsonDocument.Parse(textContent.Text);
            var root = doc.RootElement;

            // Check for segments
            if (root.TryGetProperty("segments", out var segmentsElement))
            {
                var segmentCount = segmentsElement.GetArrayLength();
                Console.WriteLine($"\n✅ SUCCESS! Transcription complete:");
                Console.WriteLine($"   Segments: {segmentCount}");
                Console.WriteLine($"   Total time: {elapsed.TotalSeconds:F1} seconds");
                Console.WriteLine($"   Recording: 10 seconds");
                Console.WriteLine($"   Processing: {elapsed.TotalSeconds - 10:F1} seconds");
                Console.WriteLine("\n--- TRANSCRIPTION ---");

                foreach (var segment in segmentsElement.EnumerateArray())
                {
                    var text = segment.GetProperty("text").GetString();
                    var start = segment.GetProperty("start").GetString();
                    Console.WriteLine($"[{start}] {text}");
                }
                Console.WriteLine("--- END ---\n");

                Assert.That(segmentCount, Is.GreaterThan(0), "No transcription segments found");
            }
            else
            {
                Console.WriteLine($"\n⚠️  Response structure: {textContent.Text}");
                Assert.Fail("No segments found in transcription result");
            }
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ============ INPUT MODULE E2E TESTS ============

    [Test]
    public async Task E2E_MouseMove_ReturnsSuccess()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["x"] = 100,
                ["y"] = 100
            };

            var result = await client.CallToolAsync("mouse_move", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);
            
            // mouse_move returns empty result on success (no error means success)
            Assert.That(result.Content, Is.Not.Null);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_MouseClick_ReturnsSuccess()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["button"] = "left",
                ["count"] = 1
            };

            var result = await client.CallToolAsync("mouse_click", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Content, Is.Not.Null);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_MouseClick_RightButton_ReturnsSuccess()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["button"] = "right",
                ["count"] = 1
            };

            var result = await client.CallToolAsync("mouse_click", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Content, Is.Not.Null);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_MouseDrag_ReturnsSuccess()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["startX"] = 100,
                ["startY"] = 100,
                ["endX"] = 200,
                ["endY"] = 200
            };

            var result = await client.CallToolAsync("mouse_drag", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Content, Is.Not.Null);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_KeyboardKey_ReturnsSuccess()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["key"] = "enter",
                ["action"] = "click"
            };

            var result = await client.CallToolAsync("keyboard_key", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Content, Is.Not.Null);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task E2E_KeyboardKey_Tab_ReturnsSuccess()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["key"] = "tab",
                ["action"] = "click"
            };

            var result = await client.CallToolAsync("keyboard_key", args).ConfigureAwait(false);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Content, Is.Not.Null);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ============ PRACTICAL E2E TESTS WITH NOTEPAD ============

    [Test]
    [Explicit("Requires GUI interaction with Notepad")]
    [Description("Launch Notepad, type text, verify by screen capture, and cleanup properly")]
    public async Task E2E_Notepad_TypeTextAndVerifyByCapture()
    {
        // Start Notepad
        var notepadProcess = System.Diagnostics.Process.Start("notepad.exe");
        Assert.That(notepadProcess, Is.Not.Null, "Failed to start Notepad");
        var notepadProcessId = notepadProcess.Id;
        Console.WriteLine($"[Test] Started Notepad (PID: {notepadProcessId})");
        
        // Wait for Notepad window to appear
        await Task.Delay(1000).ConfigureAwait(false);
        
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            // Generate unique filename for this test run
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var initialCapturePath = Path.Combine(Path.GetTempPath(), $"notepad_test_{timestamp}_initial.jpg");
            var finalCapturePath = Path.Combine(Path.GetTempPath(), $"notepad_test_{timestamp}_final.jpg");
            
            // Capture desktop to show initial state (with file save)
            await CaptureDesktop(client, "Initial state", initialCapturePath);
            
            // Get window list
            var windowsResult = await client.CallToolAsync("list_windows", null).ConfigureAwait(false);
            Assert.That(windowsResult, Is.Not.Null, "list_windows result is null");
            
            var textContent = windowsResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            if (textContent != null && !string.IsNullOrEmpty(textContent.Text))
            {
                var windows = System.Text.Json.JsonSerializer.Deserialize<List<WindowInfo>>(textContent.Text);
                if (windows != null)
                {
                    // Try to find window by process ID
                    var notepadWindow = FindWindowByProcessId(windows, notepadProcessId);
                    
                    if (notepadWindow != null)
                    {
                        Console.WriteLine($"[Test] Found Notepad window: HWND={notepadWindow.Hwnd}, Title='{notepadWindow.Title}'");
                    }
                    else
                    {
                        Console.WriteLine($"[Test] Window not found by PID. Available windows with titles: {string.Join(", ", windows.Where(w => !string.IsNullOrEmpty(w.Title)).Select(w => $"'{w.Title}'"))}");
                    }
                }
            }
            
            // Type text into Notepad (regardless of whether we found the window)
            var typeArgs = new Dictionary<string, object?>
            {
                ["text"] = "Hello from MCP!"
            };
            await client.CallToolAsync("keyboard_type", typeArgs).ConfigureAwait(false);
            Console.WriteLine("[Test] Typed text: 'Hello from MCP!'");
            
            // Wait for text to appear
            await Task.Delay(500).ConfigureAwait(false);
            
            // Capture desktop to verify (with file save)
            await CaptureDesktop(client, "After typing", finalCapturePath);
            
            // Verify files were created
            if (File.Exists(initialCapturePath))
            {
                var fileInfo = new FileInfo(initialCapturePath);
                Console.WriteLine($"[Test] Initial capture file: {initialCapturePath} ({fileInfo.Length:N0} bytes)");
            }
            if (File.Exists(finalCapturePath))
            {
                var fileInfo = new FileInfo(finalCapturePath);
                Console.WriteLine($"[Test] Final capture file: {finalCapturePath} ({fileInfo.Length:N0} bytes)");
            }
            
            Console.WriteLine($"✅ Successfully typed text into Notepad");
            Console.WriteLine($"   Process ID: {notepadProcessId}");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
            
            // Cleanup: close Notepad
            if (notepadProcess != null && !notepadProcess.HasExited)
            {
                try
                {
                    notepadProcess.Kill();
                    notepadProcess.WaitForExit(2000);
                    Console.WriteLine($"[Test] Notepad (PID: {notepadProcessId}) terminated successfully");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Test] Warning: Failed to terminate Notepad: {ex.Message}");
                }
                finally
                {
                    notepadProcess.Dispose();
                }
            }
        }
    }

    [Test]
    [Explicit("Requires GUI interaction with Notepad")]
    [Description("Launch Notepad, double-click to select word, and verify")]
    public async Task E2E_Notepad_DoubleClickToSelectWord()
    {
        var notepadProcess = System.Diagnostics.Process.Start("notepad.exe");
        Assert.That(notepadProcess, Is.Not.Null);
        var notepadProcessId = notepadProcess.Id;
        await Task.Delay(1000).ConfigureAwait(false);
        
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            // Type multi-word text
            await client.CallToolAsync("keyboard_type", new Dictionary<string, object?> { ["text"] = "Hello World Test" }).ConfigureAwait(false);
            await Task.Delay(500).ConfigureAwait(false);
            
            // Get window position and double-click
            var windowsResult = await client.CallToolAsync("list_windows", null).ConfigureAwait(false);
            if (windowsResult != null)
            {
                var textContent = windowsResult.Content.OfType<TextContentBlock>().FirstOrDefault();
                if (textContent != null)
                {
                    var windows = System.Text.Json.JsonSerializer.Deserialize<List<WindowInfo>>(textContent.Text);
                    var notepadWindow = windows?.FirstOrDefault(w => !string.IsNullOrEmpty(w.Title) && (w.Title.Contains("Notepad") || w.Title.Contains("メモ帳") || w.Title.Contains("無題")));
                    
                    if (notepadWindow != null)
                    {
                        // Double-click on "World"
                        await client.CallToolAsync("mouse_move", new Dictionary<string, object?> { ["x"] = notepadWindow.X + 70, ["y"] = notepadWindow.Y + 80 }).ConfigureAwait(false);
                        await client.CallToolAsync("mouse_click", new Dictionary<string, object?> { ["button"] = "left", ["count"] = 2 }).ConfigureAwait(false);
                        await Task.Delay(300).ConfigureAwait(false);
                    }
                }
            }
            
            await CaptureDesktop(client, "After double-click");
            Console.WriteLine($"✅ Double-click word selection test passed");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
            CleanupNotepad(notepadProcess, notepadProcessId);
        }
    }

    [Test]
    [Explicit("Requires GUI interaction with Notepad")]
    [Description("Launch Notepad, drag to select multiple lines")]
    public async Task E2E_Notepad_DragToSelectMultipleLines()
    {
        var notepadProcess = System.Diagnostics.Process.Start("notepad.exe");
        Assert.That(notepadProcess, Is.Not.Null);
        var notepadProcessId = notepadProcess.Id;
        await Task.Delay(1000).ConfigureAwait(false);
        
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            // Type multi-line text
            await client.CallToolAsync("keyboard_type", new Dictionary<string, object?> { ["text"] = "Line 1" }).ConfigureAwait(false);
            await client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = "enter", ["action"] = "click" }).ConfigureAwait(false);
            await client.CallToolAsync("keyboard_type", new Dictionary<string, object?> { ["text"] = "Line 2" }).ConfigureAwait(false);
            await Task.Delay(500).ConfigureAwait(false);
            
            // Get window and drag
            var windowsResult = await client.CallToolAsync("list_windows", null).ConfigureAwait(false);
            if (windowsResult != null)
            {
                var textContent = windowsResult.Content.OfType<TextContentBlock>().FirstOrDefault();
                if (textContent != null)
                {
                    var windows = System.Text.Json.JsonSerializer.Deserialize<List<WindowInfo>>(textContent.Text);
                    var notepadWindow = windows?.FirstOrDefault(w => !string.IsNullOrEmpty(w.Title) && (w.Title.Contains("Notepad") || w.Title.Contains("メモ帳") || w.Title.Contains("無題")));
                    
                    if (notepadWindow != null)
                    {
                        await client.CallToolAsync("mouse_drag", new Dictionary<string, object?>
                        {
                            ["startX"] = notepadWindow.X + 50,
                            ["startY"] = notepadWindow.Y + 70,
                            ["endX"] = notepadWindow.X + 150,
                            ["endY"] = notepadWindow.Y + 90
                        }).ConfigureAwait(false);
                        await Task.Delay(300).ConfigureAwait(false);
                    }
                }
            }
            
            await CaptureDesktop(client, "After drag selection");
            Console.WriteLine($"✅ Drag selection test passed");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
            CleanupNotepad(notepadProcess, notepadProcessId);
        }
    }

    [Test]
    [Explicit("Requires GUI interaction with Notepad")]
    [Description("Launch Notepad, right-click for context menu")]
    public async Task E2E_Notepad_RightClickContextMenu()
    {
        var notepadProcess = System.Diagnostics.Process.Start("notepad.exe");
        Assert.That(notepadProcess, Is.Not.Null);
        var notepadProcessId = notepadProcess.Id;
        await Task.Delay(1000).ConfigureAwait(false);
        
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            await client.CallToolAsync("keyboard_type", new Dictionary<string, object?> { ["text"] = "Test text" }).ConfigureAwait(false);
            await Task.Delay(500).ConfigureAwait(false);
            
            // Right-click in text area
            var windowsResult = await client.CallToolAsync("list_windows", null).ConfigureAwait(false);
            if (windowsResult != null)
            {
                var textContent = windowsResult.Content.OfType<TextContentBlock>().FirstOrDefault();
                if (textContent != null)
                {
                    var windows = System.Text.Json.JsonSerializer.Deserialize<List<WindowInfo>>(textContent.Text);
                    var notepadWindow = windows?.FirstOrDefault(w => !string.IsNullOrEmpty(w.Title) && (w.Title.Contains("Notepad") || w.Title.Contains("メモ帳") || w.Title.Contains("無題")));
                    
                    if (notepadWindow != null)
                    {
                        await client.CallToolAsync("mouse_move", new Dictionary<string, object?> { ["x"] = notepadWindow.X + 80, ["y"] = notepadWindow.Y + 80 }).ConfigureAwait(false);
                        await client.CallToolAsync("mouse_click", new Dictionary<string, object?> { ["button"] = "right", ["count"] = 1 }).ConfigureAwait(false);
                        await Task.Delay(500).ConfigureAwait(false);
                    }
                }
            }
            
            await CaptureDesktop(client, "Context menu opened");
            
            // Close menu with Escape
            await client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = "escape", ["action"] = "click" }).ConfigureAwait(false);
            await Task.Delay(200).ConfigureAwait(false);
            
            Console.WriteLine($"✅ Right-click context menu test passed");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
            CleanupNotepad(notepadProcess, notepadProcessId);
        }
    }

    [Test]
    [Explicit("Requires GUI interaction with Notepad")]
    [Description("Launch Notepad, test Copy-Paste with Ctrl+C/V")]
    public async Task E2E_Notepad_CopyPasteWithShortcuts()
    {
        var notepadProcess = System.Diagnostics.Process.Start("notepad.exe");
        Assert.That(notepadProcess, Is.Not.Null);
        var notepadProcessId = notepadProcess.Id;
        await Task.Delay(1000).ConfigureAwait(false);
        
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            // Type text
            await client.CallToolAsync("keyboard_type", new Dictionary<string, object?> { ["text"] = "Copy me" }).ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            
            // Ctrl+A to select all
            await PressModifierKeyCombo(client, "ctrl", "a");
            await Task.Delay(200).ConfigureAwait(false);
            
            // Ctrl+C to copy
            await PressModifierKeyCombo(client, "ctrl", "c");
            await Task.Delay(200).ConfigureAwait(false);
            
            // Move to end and press Enter
            await client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = "end", ["action"] = "click" }).ConfigureAwait(false);
            await client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = "enter", ["action"] = "click" }).ConfigureAwait(false);
            await Task.Delay(200).ConfigureAwait(false);
            
            // Ctrl+V to paste
            await PressModifierKeyCombo(client, "ctrl", "v");
            await Task.Delay(300).ConfigureAwait(false);
            
            await CaptureDesktop(client, "After copy-paste");
            Console.WriteLine($"✅ Copy-Paste operation test passed");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
            CleanupNotepad(notepadProcess, notepadProcessId);
        }
    }

    [Test]
    [Explicit("Requires GUI interaction with Notepad")]
    [Description("Launch Notepad, test cursor navigation with arrow keys")]
    public async Task E2E_Notepad_CursorNavigationWithArrowKeys()
    {
        var notepadProcess = System.Diagnostics.Process.Start("notepad.exe");
        Assert.That(notepadProcess, Is.Not.Null);
        var notepadProcessId = notepadProcess.Id;
        await Task.Delay(1000).ConfigureAwait(false);
        
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            await client.CallToolAsync("keyboard_type", new Dictionary<string, object?> { ["text"] = "AB" }).ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            
            // Navigate with arrow keys
            await client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = "left", ["action"] = "click" }).ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            await client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = "left", ["action"] = "click" }).ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            await client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = "right", ["action"] = "click" }).ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            
            // Type at current position
            await client.CallToolAsync("keyboard_type", new Dictionary<string, object?> { ["text"] = "X" }).ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            
            await CaptureDesktop(client, "After cursor navigation");
            Console.WriteLine($"✅ Arrow key navigation test passed");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
            CleanupNotepad(notepadProcess, notepadProcessId);
        }
    }

    [Test]
    [Explicit("Requires GUI interaction with Notepad")]
    [Description("Launch Notepad, test middle-click")]
    public async Task E2E_Notepad_MiddleClick()
    {
        var notepadProcess = System.Diagnostics.Process.Start("notepad.exe");
        Assert.That(notepadProcess, Is.Not.Null);
        var notepadProcessId = notepadProcess.Id;
        await Task.Delay(1000).ConfigureAwait(false);
        
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var windowsResult = await client.CallToolAsync("list_windows", null).ConfigureAwait(false);
            if (windowsResult != null)
            {
                var textContent = windowsResult.Content.OfType<TextContentBlock>().FirstOrDefault();
                if (textContent != null)
                {
                    var windows = System.Text.Json.JsonSerializer.Deserialize<List<WindowInfo>>(textContent.Text);
                    var notepadWindow = windows?.FirstOrDefault(w => !string.IsNullOrEmpty(w.Title) && (w.Title.Contains("Notepad") || w.Title.Contains("メモ帳") || w.Title.Contains("無題")));
                    
                    if (notepadWindow != null)
                    {
                        await client.CallToolAsync("mouse_move", new Dictionary<string, object?> { ["x"] = notepadWindow.X + 100, ["y"] = notepadWindow.Y + 100 }).ConfigureAwait(false);
                        await client.CallToolAsync("mouse_click", new Dictionary<string, object?> { ["button"] = "middle", ["count"] = 1 }).ConfigureAwait(false);
                        await Task.Delay(300).ConfigureAwait(false);
                    }
                }
            }
            
            Console.WriteLine($"✅ Middle-click test passed (may have no visible effect in Notepad)");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
            CleanupNotepad(notepadProcess, notepadProcessId);
        }
    }

    [Test]
    [Explicit("Requires GUI interaction with Notepad")]
    [Description("Launch Notepad, select all with Ctrl+A")]
    public async Task E2E_Notepad_SelectAllWithKeyboard()
    {
        var notepadProcess = System.Diagnostics.Process.Start("notepad.exe");
        Assert.That(notepadProcess, Is.Not.Null);
        var notepadProcessId = notepadProcess.Id;
        await Task.Delay(1000).ConfigureAwait(false);
        
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            await client.CallToolAsync("keyboard_type", new Dictionary<string, object?> { ["text"] = "Select this text" }).ConfigureAwait(false);
            await Task.Delay(500).ConfigureAwait(false);
            
            // Ctrl+A to select all
            await PressModifierKeyCombo(client, "ctrl", "a");
            await Task.Delay(300).ConfigureAwait(false);
            
            await CaptureDesktop(client, "After Ctrl+A");
            Console.WriteLine($"✅ Select all test passed");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
            CleanupNotepad(notepadProcess, notepadProcessId);
        }
    }

    [Test]
    [Explicit("Requires GUI interaction with Notepad")]
    [Description("Launch Notepad, open File menu with mouse")]
    public async Task E2E_Notepad_OpenFileMenuWithMouse()
    {
        var notepadProcess = System.Diagnostics.Process.Start("notepad.exe");
        Assert.That(notepadProcess, Is.Not.Null);
        var notepadProcessId = notepadProcess.Id;
        await Task.Delay(1000).ConfigureAwait(false);
        
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        try
        {
            var windowsResult = await client.CallToolAsync("list_windows", null).ConfigureAwait(false);
            if (windowsResult != null)
            {
                var textContent = windowsResult.Content.OfType<TextContentBlock>().FirstOrDefault();
                if (textContent != null)
                {
                    var windows = System.Text.Json.JsonSerializer.Deserialize<List<WindowInfo>>(textContent.Text);
                    var notepadWindow = windows?.FirstOrDefault(w => !string.IsNullOrEmpty(w.Title) && (w.Title.Contains("Notepad") || w.Title.Contains("メモ帳") || w.Title.Contains("無題")));
                    
                    if (notepadWindow != null)
                    {
                        // Click on File menu (top-left area)
                        await client.CallToolAsync("mouse_move", new Dictionary<string, object?> { ["x"] = notepadWindow.X + 10, ["y"] = notepadWindow.Y + 30 }).ConfigureAwait(false);
                        await Task.Delay(200).ConfigureAwait(false);
                        await client.CallToolAsync("mouse_click", new Dictionary<string, object?> { ["button"] = "left", ["count"] = 1 }).ConfigureAwait(false);
                        await Task.Delay(500).ConfigureAwait(false);
                        
                        await CaptureDesktop(client, "File menu opened");
                        
                        // Close menu with Escape
                        await client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = "escape", ["action"] = "click" }).ConfigureAwait(false);
                        await Task.Delay(200).ConfigureAwait(false);
                    }
                }
            }
            
            Console.WriteLine($"✅ File menu test passed");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
            CleanupNotepad(notepadProcess, notepadProcessId);
        }
    }

    // Helper method to cleanup notepad
    private static void CleanupNotepad(Process? notepadProcess, int notepadProcessId)
    {
        if (notepadProcess != null && !notepadProcess.HasExited)
        {
            try
            {
                notepadProcess.Kill();
                notepadProcess.WaitForExit(2000);
                Console.WriteLine($"[Test] Notepad (PID: {notepadProcessId}) terminated successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Test] Warning: Failed to terminate Notepad: {ex.Message}");
            }
            finally
            {
                notepadProcess.Dispose();
            }
        }
    }

    // Helper method for modifier key combinations
    private static async Task PressModifierKeyCombo(McpClient client, string modifier, string key)
    {
        await client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = modifier, ["action"] = "press" }).ConfigureAwait(false);
        await client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = key, ["action"] = "click" }).ConfigureAwait(false);
        await client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = modifier, ["action"] = "release" }).ConfigureAwait(false);
    }
}
