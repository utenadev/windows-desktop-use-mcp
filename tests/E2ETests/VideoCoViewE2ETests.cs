using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WindowsDesktopUse.Core;

namespace E2ETests;

[TestFixture]
public class VideoCoViewE2ETests
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
        var testAssemblyDir = Path.GetDirectoryName(typeof(VideoCoViewE2ETests).Assembly.Location)!;
        return Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", "..", ".."));
    }

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        _client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        if (_client != null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task WatchVideoV2StartsAndStopsSuccessfully()
    {
        // Start watching a small region
        var result = await _client!.CallToolAsync("watch_video_v2",
            new Dictionary<string, object?>
            {
                ["x"] = 0,
                ["y"] = 0,
                ["w"] = 100,
                ["h"] = 100,
                ["intervalMs"] = 2000,
                ["quality"] = 50,
                ["modelSize"] = "tiny"
            }).ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        var resultText = result.Content.OfType<TextContentBlock>().First()?.Text;
        Assert.That(resultText, Is.Not.Null);
        
        var sessionId = resultText.Trim('\"');
        Assert.That(string.IsNullOrEmpty(sessionId), Is.False);
        Console.WriteLine($"[Test] Started VideoCoViewV2 session: {sessionId}");

        await Task.Delay(2000).ConfigureAwait(false);

        // Stop the session
        var stopResult = await _client.CallToolAsync("stop_watch_video_v2",
            new Dictionary<string, object?> { ["sessionId"] = sessionId }).ConfigureAwait(false);

        Assert.That(stopResult, Is.Not.Null);
        var stopText = stopResult.Content.OfType<TextContentBlock>().First()?.Text;
        Assert.That(stopText, Does.Contain(sessionId));
        Console.WriteLine($"[Test] Stopped session: {sessionId}");
    }
}
