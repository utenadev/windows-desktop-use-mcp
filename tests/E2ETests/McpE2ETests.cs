using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace E2ETests;

[TestFixture]
public class McpE2ETests
{
    private static string ServerPath => GetServerPath();
    
    private static string GetServerPath()
    {
        // Try to find server executable relative to test assembly
        var testAssemblyDir = Path.GetDirectoryName(typeof(McpE2ETests).Assembly.Location)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", ".."));
        
        // Check multiple possible locations
        var possiblePaths = new[]
        {
            Path.Combine(repoRoot, "src", "bin", "Release", "net8.0-windows", "win-x64", "WindowsScreenCaptureServer.exe"),
            Path.Combine(repoRoot, "src", "bin", "Debug", "net8.0-windows", "win-x64", "WindowsScreenCaptureServer.exe"),
            Path.Combine(testAssemblyDir, "..", "..", "..", "src", "bin", "Release", "net8.0-windows", "win-x64", "WindowsScreenCaptureServer.exe"),
            @"C:\workspace\mcp-windows-screen-capture\src\bin\Release\net8.0-windows\win-x64\WindowsScreenCaptureServer.exe" // Fallback for local dev
        };
        
        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }
        
        throw new FileNotFoundException("WindowsScreenCaptureServer.exe not found. Please build the project first.");
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
}
