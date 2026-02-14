using System.Diagnostics;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using static E2ETests.TestHelper;

namespace E2ETests;

[TestFixture]
public class YouTubeSpecificE2ETests
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
        var testAssemblyDir = Path.GetDirectoryName(typeof(YouTubeSpecificE2ETests).Assembly.Location)!;
        return Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", "..", ".."));
    }

    [SetUp]
    public async Task Setup()
    {
        _client = await CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);
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
    public async Task WatchVideo_YouTubeMasterKeaton_ReturnsSessionId()
    {
        // Skip on CI
        if (IsCiEnvironment())
            Assert.Ignore("Skipping on CI: Requires YouTube window");

        // 1. ウィンドウ一覧を取得
        var windowsResult = await _client!.CallToolAsync("list_windows", new Dictionary<string, object?>())
            .ConfigureAwait(false);
        
        var windowsContent = windowsResult.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.That(windowsContent, Is.Not.Null);

        var windowsJson = windowsContent!.Text ?? "[]";
        var windows = TestHelper.DeserializeJson<List<WindowInfo>>(windowsJson) ?? new List<WindowInfo>();
        
        // 2. 「MASTERキートン」を含むYouTubeウィンドウを探す
        var youtubeWindow = windows.FirstOrDefault(w => 
            (w.Title.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ||
             w.Title.Contains("youtube", StringComparison.OrdinalIgnoreCase)) &&
            w.Title.Contains("MASTERキートン", StringComparison.OrdinalIgnoreCase));
        
        if (youtubeWindow == null)
        {
            Assert.Warn("YouTube window with 'MASTERキートン' not found. Please open YouTube and search for 'MASTERキートン' before running this test.");
            return;
        }

        Console.WriteLine($"[Test] Found YouTube window: hwnd={youtubeWindow.Hwnd}, title={youtubeWindow.Title}");

        // 3. 動画キャプチャを開始
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

        // 4. 3秒間キャプチャを実行
        await Task.Delay(3000).ConfigureAwait(false);

        // 5. 最新フレームを取得
        var frameResult = await _client.CallToolAsync("get_latest_video_frame", new Dictionary<string, object?>
        {
            ["sessionId"] = sessionId
        }).ConfigureAwait(false);

        Assert.That(frameResult, Is.Not.Null);
        Console.WriteLine($"[Test] Frame result: {frameResult.Content.FirstOrDefault()}");

        // 6. ストリームを停止
        var stopResult = await _client.CallToolAsync("stop_watch_video", new Dictionary<string, object?>
        {
            ["sessionId"] = sessionId
        }).ConfigureAwait(false);

        Assert.That(stopResult, Is.Not.Null);
        Console.WriteLine($"[Test] Video session stopped");
    }
}
