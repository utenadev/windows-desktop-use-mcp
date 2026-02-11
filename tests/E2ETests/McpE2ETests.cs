using System.Diagnostics;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace E2ETests;

[TestFixture]
public class McpE2ETests
{
    private static string ServerPath => GetServerPath();
    private McpClient? _client;
    private long? _testNotepadHwnd;
    private HashSet<int> _preExistingNotepadPids = new();

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
        var testAssemblyDir = Path.GetDirectoryName(typeof(McpE2ETests).Assembly.Location)!;
        return Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", "..", ".."));
    }

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        // Record existing notepad PIDs to avoid closing them later
        _preExistingNotepadPids = Process.GetProcessesByName("notepad").Select(p => p.Id).ToHashSet();

        // Start a fresh Notepad instance
        Process.Start("notepad.exe");

        // Wait for it to initialize and show window
        await Task.Delay(3000).ConfigureAwait(false);

        _client = await TestHelper.CreateStdioClientAsync(ServerPath, Array.Empty<string>()).ConfigureAwait(false);

        // Find the newly opened notepad window and store its HWND
        var notepad = await FindNotepadWithRetry(5).ConfigureAwait(false);
        if (notepad != null)
        {
            _testNotepadHwnd = notepad.Hwnd;
            Console.WriteLine($"[Setup] Captured test Notepad HWND: {_testNotepadHwnd}");
        }
    }

    [OneTimeTearDown]
    public async Task GlobalTeardown()
    {
        if (_client != null)
        {
            // 1. Try closing by HWND using the new tool
            if (_testNotepadHwnd.HasValue)
            {
                try
                {
                    Console.WriteLine($"[Teardown] Closing test Notepad window (HWND: {_testNotepadHwnd})");
                    await _client.CallToolAsync("close_window", new Dictionary<string, object?> { ["hwnd"] = _testNotepadHwnd.Value }).ConfigureAwait(false);
                }
                catch { }
            }

            // 2. Kill any other notepad processes that were started during this test
            var currentNotepads = Process.GetProcessesByName("notepad");
            foreach (var p in currentNotepads)
            {
                if (!_preExistingNotepadPids.Contains(p.Id))
                {
                    try
                    {
                        Console.WriteLine($"[Teardown] Killing unexpected Notepad process (PID: {p.Id})");
                        p.Kill(true);
                    }
                    catch { }
                }
            }

            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<WindowInfo?> FindNotepadWithRetry(int retryCount = 3)
    {
        for (int i = 0; i < retryCount; i++)
        {
            var windowsResult = await _client!.CallToolAsync("list_windows", null).ConfigureAwait(false);
            var textContent = windowsResult.Content.OfType<TextContentBlock>().FirstOrDefault();
            if (textContent != null && !string.IsNullOrEmpty(textContent.Text))
            {
                var windows = System.Text.Json.JsonSerializer.Deserialize<List<WindowInfo>>(textContent.Text);
                if (windows != null)
                {
                    var notepad = windows.FirstOrDefault(w =>
                        !string.IsNullOrEmpty(w.Title) && (
                        w.Title.Contains("Notepad", StringComparison.OrdinalIgnoreCase) ||
                        w.Title.Contains("メモ帳") ||
                        w.Title.Contains("無題") ||
                        w.Title.Contains("Untitled")));

                    if (notepad != null) return notepad;
                }
            }
            await Task.Delay(1000).ConfigureAwait(false);
        }
        return null;
    }

    [Test]
    public async Task E2E_ListMonitors_ReturnsValidMonitors()
    {
        var result = await _client!.CallToolAsync("list_monitors", null).ConfigureAwait(false);
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task E2E_ListWindows_ReturnsValidWindows()
    {
        var result = await _client!.CallToolAsync("list_windows", null).ConfigureAwait(false);
        Assert.That(result, Is.Not.Null);
    }

    // ============ PRACTICAL NOTEPAD TESTS ============

    [Test]
    [Order(1)]
    public async Task Notepad_1_NavigationKeys()
    {
        await _client!.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = "enter", ["action"] = "click" }).ConfigureAwait(false);
        await _client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = "space", ["action"] = "click" }).ConfigureAwait(false);
        await Task.Delay(500).ConfigureAwait(false);
    }

    [Test]
    [Order(2)]
    public async Task Notepad_2_MouseOperations()
    {
        WindowInfo? notepad = null;
        if (_testNotepadHwnd.HasValue)
        {
            var windowsResult = await _client!.CallToolAsync("list_windows", null).ConfigureAwait(false);
            var textContent = windowsResult.Content.OfType<TextContentBlock>().First();
            var windows = System.Text.Json.JsonSerializer.Deserialize<List<WindowInfo>>(textContent.Text);
            notepad = windows?.FirstOrDefault(w => w.Hwnd == _testNotepadHwnd.Value);
        }

        if (notepad == null)
        {
            Assert.Ignore("Skipping mouse test: Test Notepad window not found in list");
            return;
        }

        int clickX = notepad.X + 50;
        int clickY = notepad.Y + 100;

        await _client!.CallToolAsync("mouse_move", new Dictionary<string, object?> { ["x"] = clickX, ["y"] = clickY }).ConfigureAwait(false);
        await _client.CallToolAsync("mouse_click", new Dictionary<string, object?> { ["button"] = "left", ["count"] = 1 }).ConfigureAwait(false);
        await Task.Delay(500).ConfigureAwait(false);

        await _client.CallToolAsync("mouse_click", new Dictionary<string, object?> { ["button"] = "right", ["count"] = 1 }).ConfigureAwait(false);
        await Task.Delay(1000).ConfigureAwait(false);

        await _client.CallToolAsync("keyboard_key", new Dictionary<string, object?> { ["key"] = "escape", ["action"] = "click" }).ConfigureAwait(false);
    }
}
