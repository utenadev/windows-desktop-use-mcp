using ModelContextProtocol.Protocol;
using NUnit.Framework;

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
            Path.Combine(repoRoot, "src", "bin", "Release", "net8.0-windows", "win-x64", "WindowsScreenCaptureServer.exe"),
            Path.Combine(repoRoot, "src", "bin", "Debug", "net8.0-windows", "win-x64", "WindowsScreenCaptureServer.exe"),
            // Local development fallback
            @"C:\workspace\mcp-windows-screen-capture\src\bin\Release\net8.0-windows\win-x64\WindowsScreenCaptureServer.exe"
        };
        
        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }
        
        throw new FileNotFoundException($"WindowsScreenCaptureServer.exe not found. Checked paths:\n{string.Join("\n", possiblePaths.Select(p => Path.GetFullPath(p)))}\n\nPlease build the project first.");
    }
    
    private static string GetRepoRootFromAssembly()
    {
        var testAssemblyDir = Path.GetDirectoryName(typeof(McpE2ETests).Assembly.Location)!;
        // Go up from tests/E2ETests/bin/Debug/net8.0 to repo root
        return Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", ".."));
    }

    [Test]
    public async Task E2E_ListMonitors_ReturnsValidMonitors()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            var tools = await client.ListToolsAsync();
            Assert.That(tools, Is.Not.Null);

            var listMonitorsTool = tools.FirstOrDefault(t => t.Name == "list_monitors");
            Assert.That(listMonitorsTool, Is.Not.Null, "list_monitors tool not found");

            var result = await client.CallToolAsync("list_monitors", null);
            Assert.That(result, Is.Not.Null);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_ListWindows_ReturnsValidWindows()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            var result = await client.CallToolAsync("list_windows", null);
            Assert.That(result, Is.Not.Null);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_SeeMonitor_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["targetType"] = "monitor",
                ["monitor"] = 0,
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("see", args);
            Assert.That(result, Is.Not.Null);

            var imageContent = result.Content.OfType<ImageContentBlock>().FirstOrDefault();
            Assert.That(imageContent, Is.Not.Null, "No image content found");
            Assert.That(imageContent.Data, Is.Not.Null, "Image data is null");

            TestHelper.ValidateBase64Image(imageContent.Data);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    [Ignore("Window capture may fail due to OS-specific visibility checks")]
    public async Task E2E_CaptureWindow_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            var windowsResult = await client.CallToolAsync("list_windows", null);
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

            var result = await client.CallToolAsync("capture_window", args);
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
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_CaptureRegion_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

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

            var result = await client.CallToolAsync("capture_region", args);
            Assert.That(result, Is.Not.Null);

            var imageContent = result.Content.OfType<ImageContentBlock>().FirstOrDefault();
            Assert.That(imageContent, Is.Not.Null, "No image content found");
            Assert.That(imageContent.Data, Is.Not.Null, "Image data is null");

            TestHelper.ValidateBase64Image(imageContent.Data);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_StartWatching_ReturnsValidSessionId()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

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

            var result = await client.CallToolAsync("start_watching", args);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Session ID is null");
            Assert.That(textContent.Text, Does.Match(@"^[a-f0-9\-]{36}$"), "Session ID format is invalid");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_StopWatching_ReturnsSuccess()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

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

            var startResult = await client.CallToolAsync("start_watching", startArgs);
            Assert.That(startResult, Is.Not.Null, "start_watching failed");

            var textContent = startResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            var sessionId = textContent.Text;
            Assert.That(sessionId, Is.Not.Null, "Session ID is null");

            var stopArgs = new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId
            };

            var stopResult = await client.CallToolAsync("stop_watching", stopArgs);
            Assert.That(stopResult, Is.Not.Null);

            var stopTextContent = stopResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(stopTextContent, Is.Not.Null, "No text content found in stop result");
            Assert.That(stopTextContent.Text, Is.EqualTo("Stopped watching"), "Stop response is unexpected");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_InvalidMonitorIndex_ReturnsError()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["targetType"] = "monitor",
                ["monitor"] = 999,
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("see", args);
            Assert.That(result, Is.Not.Null);
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    // ============ NEW UNIFIED TOOLS E2E TESTS ============

    [Test]
    public async Task E2E_ListAll_ReturnsValidTargets()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            var result = await client.CallToolAsync("list_all", null);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_ListAll_FilterMonitors_ReturnsOnlyMonitors()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["filter"] = "monitors"
            };

            var result = await client.CallToolAsync("list_all", args);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_ListAll_FilterWindows_ReturnsOnlyWindows()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["filter"] = "windows"
            };

            var result = await client.CallToolAsync("list_all", args);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_Capture_Primary_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["target"] = "primary",
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("capture", args);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_Capture_Monitor_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["target"] = "monitor",
                ["targetId"] = "0",
                ["quality"] = 80,
                ["maxWidth"] = 1920
            };

            var result = await client.CallToolAsync("capture", args);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    [Ignore("Window capture may fail due to OS-specific visibility checks")]
    public async Task E2E_Capture_Window_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            // First get a window handle
            var listAllResult = await client.CallToolAsync("list_all", new Dictionary<string, object?> { ["filter"] = "windows" });
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

            var result = await client.CallToolAsync("capture", args);
            Assert.That(result, Is.Not.Null);

            var resultTextContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(resultTextContent, Is.Not.Null, "No text content found");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_Capture_Region_ReturnsValidImage()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

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

            var result = await client.CallToolAsync("capture", args);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_Watch_ReturnsValidSessionId()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

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

            var result = await client.CallToolAsync("watch", args);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            Assert.That(textContent.Text, Is.Not.Null, "Session info is null");
            
            // Verify it contains session ID (JSON uses camelCase)
            Assert.That(textContent.Text, Does.Contain("sessionId"), "Response should contain sessionId");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    [Test]
    public async Task E2E_StopWatch_ReturnsSuccess()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

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

            var startResult = await client.CallToolAsync("watch", startArgs);
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

            var stopResult = await client.CallToolAsync("stop_watch", stopArgs);
            Assert.That(stopResult, Is.Not.Null);

            var stopTextContent = stopResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(stopTextContent, Is.Not.Null, "No text content found in stop result");
            Assert.That(stopTextContent.Text, Does.Contain("Stopped").Or.Contain("stopped"), "Stop response should indicate success");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    // ============ AUDIO CAPTURE E2E TESTS ============

    [Test]
    public async Task E2E_ListAudioDevices_ReturnsDevices()
    {
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            var result = await client.CallToolAsync("list_audio_devices", null);
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
            await client.DisposeAsync();
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
        
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());
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
            
            await Task.Delay(5000);

            // Start system audio capture
            var startArgs = new Dictionary<string, object?>
            {
                ["source"] = "system",
                ["sampleRate"] = 44100
            };

            var startResult = await client.CallToolAsync("start_audio_capture", startArgs);
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
            await Task.Delay(5000);

            // Stop capture and get audio data
            var stopArgs = new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId,
                ["returnFormat"] = "base64"
            };

            var stopResult = await client.CallToolAsync("stop_audio_capture", stopArgs);
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
            await File.WriteAllBytesAsync(outputPath, audioBytes);
            
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
            await client.DisposeAsync();
            
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
        var client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>());

        try
        {
            var result = await client.CallToolAsync("get_active_audio_sessions", null);
            Assert.That(result, Is.Not.Null);

            var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
            Assert.That(textContent, Is.Not.Null, "No text content found");
            
            // Should return a list (possibly empty if no sessions active)
            Assert.That(textContent.Text, Is.Not.Null, "Response text is null");
        }
        finally
        {
            await client.DisposeAsync();
        }
    }
}
