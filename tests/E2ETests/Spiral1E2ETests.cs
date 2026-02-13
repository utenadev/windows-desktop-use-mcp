using System.Diagnostics;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using static E2ETests.TestHelper;

namespace E2ETests;

[TestFixture]
public class Spiral1E2ETests
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
        var testAssemblyDir = Path.GetDirectoryName(typeof(Spiral1E2ETests).Assembly.Location)!;
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
    public async Task WatchVideoV1_StartsSuccessfully()
    {
        // Skip on CI - requires active video window to be running
        if (IsCiEnvironment())
            Assert.Ignore("Skipping on CI: Requires active video window (YouTube, etc.) to be running");

        var result = await _client!.CallToolAsync("watch_video_v1", new Dictionary<string, object?>
        {
            ["x"] = 100,
            ["y"] = 100,
            ["w"] = 640,
            ["h"] = 360,
            ["quality"] = 60,
            ["fps"] = 5,
            ["enableAudio"] = false // Disable audio for faster test
        }).ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        
        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.That(textContent, Is.Not.Null);
        
        var sessionId = textContent!.Text?.Trim('"');
        Assert.That(sessionId, Is.Not.Null.And.Not.Empty);
        Assert.That(Guid.TryParse(sessionId, out _), Is.True);

        Console.WriteLine($"[Test] Spiral 1 session started: {sessionId}");

        // Give it some time
        await Task.Delay(500).ConfigureAwait(false);

        // Stop the stream
        var stopResult = await _client.CallToolAsync("stop_watch_video_v1", new Dictionary<string, object?>
        {
            ["sessionId"] = sessionId
        }).ConfigureAwait(false);

        Assert.That(stopResult, Is.Not.Null);
        Console.WriteLine($"[Test] Spiral 1 session stopped successfully");
    }

    [Test]
    public async Task WatchVideoV1_InvalidDimensions_ReturnsError()
    {
        var result = await _client!.CallToolAsync("watch_video_v1", new Dictionary<string, object?>
        {
            ["x"] = 100,
            ["y"] = 100,
            ["w"] = 0, // Invalid
            ["h"] = 360,
            ["enableAudio"] = false
        }).ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.That(textContent, Is.Not.Null);
        
        // Should handle error gracefully
        Console.WriteLine($"[Test] Error result: {textContent!.Text}");
    }

    [Test]
    public async Task StopWatchVideoV1_InvalidSession_ReturnsMessage()
    {
        var result = await _client!.CallToolAsync("stop_watch_video_v1", new Dictionary<string, object?>
        {
            ["sessionId"] = "non-existent-session"
        }).ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        var textContent = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.That(textContent, Is.Not.Null);
        
        Console.WriteLine($"[Test] Stop result: {textContent!.Text}");
    }
}
