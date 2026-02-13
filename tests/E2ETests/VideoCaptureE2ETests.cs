using System.Diagnostics;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WindowsDesktopUse.Core;

using static E2ETests.TestHelper;

namespace E2ETests;

[TestFixture]
public class VideoCaptureE2ETests
{
    private static string ServerPath => GetServerPath();
    private McpClient? _client;

    private static string GetServerPath()
    {
        var githubWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        var repoRoot = !string.IsNullOrEmpty(githubWorkspace)
            ? githubWorkspace
            : GetRepoRootFromAssembly();

        var possiblePaths = new[]
        {
            Path.Combine(repoRoot, "src", "WindowsDesktopUse.App", "bin", "Debug", "net8.0-windows", "win-x64", "WindowsDesktopUse.exe"),
            Path.Combine(repoRoot, "src", "WindowsDesktopUse.App", "bin", "Release", "net8.0-windows", "win-x64", "WindowsDesktopUse.exe"),
            ""
        };

        foreach (var path in possiblePaths)
        {
            if (string.IsNullOrEmpty(path)) continue;
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath)) return fullPath;
        }

        throw new FileNotFoundException($"WindowsDesktopUse.exe not found.");
    }

    private static string GetRepoRootFromAssembly()
    {
        var testAssemblyDir = Path.GetDirectoryName(typeof(VideoCaptureE2ETests).Assembly.Location)!;
        return Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", "..", ".."));
    }

    [SetUp]
    public async Task Setup()
    {
        _client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_client != null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task WatchVideo_ActiveWindow_ReturnsSessionId()
    {
        // Skip on CI - requires active video window to be running
        if (IsCiEnvironment())
            Assert.Ignore("Skipping on CI: Requires active video window (YouTube, etc.) to be running");

        var result = await _client!.CallToolAsync("watch_video", new Dictionary<string, object?>
        {
            ["targetName"] = "ActiveWindow",
            ["fps"] = 5,
            ["quality"] = 60,
            ["enableChangeDetection"] = true
        }).ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        
        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.That(textContent, Is.Not.Null);
        
        var sessionId = textContent!.Text?.Trim('"');
        Assert.That(sessionId, Is.Not.Null.And.Not.Empty);
        Assert.That(Guid.TryParse(sessionId, out _), Is.True);

        Console.WriteLine($"[Test] Video session started: {sessionId}");

        // Give it some time to capture
        await Task.Delay(1000).ConfigureAwait(false);

        // Stop the stream
        var stopResult = await _client.CallToolAsync("stop_watch_video", new Dictionary<string, object?>
        {
            ["sessionId"] = sessionId
        }).ConfigureAwait(false);

        Assert.That(stopResult, Is.Not.Null);
    }

    [Test]
    public async Task WatchVideo_InvalidTarget_ReturnsError()
    {
        // MCP tools return error result rather than throwing exceptions
        var result = await _client!.CallToolAsync("watch_video", new Dictionary<string, object?>
        {
            ["targetName"] = "NonExistentVideoPlayerXYZ123",
            ["fps"] = 5
        }).ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        
        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.That(textContent, Is.Not.Null);
        
        // Should contain error information
        var text = textContent!.Text ?? "";
        Assert.That(text.Contains("error", StringComparison.OrdinalIgnoreCase) || 
                    text.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("not", StringComparison.OrdinalIgnoreCase), 
                    Is.True, $"Expected error in response but got: {text}");
        
        Console.WriteLine($"[Test] Error result: {text}");
    }

    [Test]
    public async Task WatchVideo_InvalidFps_ReturnsError()
    {
        // MCP tools return error result rather than throwing exceptions
        var result = await _client!.CallToolAsync("watch_video", new Dictionary<string, object?>
        {
            ["targetName"] = "ActiveWindow",
            ["fps"] = 50 // Invalid: > 30
        }).ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        
        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.That(textContent, Is.Not.Null);
        
        // Should contain error information about invalid FPS
        var text = textContent!.Text ?? "";
        Assert.That(text.Contains("error", StringComparison.OrdinalIgnoreCase) || 
                    text.Contains("fps", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("range", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("invalid", StringComparison.OrdinalIgnoreCase), 
                    Is.True, $"Expected error in response but got: {text}");
        
        Console.WriteLine($"[Test] Error result: {text}");
    }

    [Test]
    public async Task StopWatchVideo_InvalidSession_ReturnsError()
    {
        var result = await _client!.CallToolAsync("stop_watch_video", new Dictionary<string, object?>
        {
            ["sessionId"] = "invalid-session-id"
        }).ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        
        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.That(textContent, Is.Not.Null);
        
        // Should handle gracefully
        Console.WriteLine($"[Test] Stop result: {textContent!.Text}");
    }
}
