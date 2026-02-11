using System.Drawing;
using System.Runtime.InteropServices;

namespace WindowsDesktopUse.Screen;

/// <summary>
/// Capture API preference options
/// </summary>
public enum CaptureApiPreference
{
    Auto,
    Modern,
    Legacy,
    Hybrid
}

/// <summary>
/// Interface for capture services
/// </summary>
public interface ICaptureService
{
    bool IsAvailable { get; }
    string ApiName { get; }
    Task<Bitmap?> CaptureWindowAsync(IntPtr hwnd, CancellationToken ct = default);
    Task<Bitmap?> CaptureMonitorAsync(uint monitorIndex, CancellationToken ct = default);
}

/// <summary>
/// Windows Graphics Capture API implementation (stub)
/// </summary>
public sealed class ModernCaptureService : ICaptureService, IDisposable
{
    public string ApiName => "Windows.Graphics.Capture (Stub)";

    public bool IsAvailable
    {
        get
        {
            return Environment.OSVersion.Version.Build >= 17134 &&
                   IsGraphicsCaptureAvailable();
        }
    }

    public ModernCaptureService()
    {
        throw new NotImplementedException(
            "ModernCaptureService requires C#/WinRT projection. " +
            "Use Legacy mode or Hybrid with fallback.");
    }

    public Task<Bitmap?> CaptureWindowAsync(IntPtr hwnd, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<Bitmap?> CaptureMonitorAsync(uint monitorIndex, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    private static bool IsGraphicsCaptureAvailable()
    {
        return false;
    }
}

/// <summary>
/// Hybrid capture service that tries modern API first, falls back to legacy
/// </summary>
public sealed class HybridCaptureService : ICaptureService, IDisposable
{
    private readonly ModernCaptureService? _modern;
    private readonly ScreenCaptureService _legacy;
    private readonly CaptureApiPreference _preference;

    public string ApiName => _preference switch
    {
        CaptureApiPreference.Modern => _modern?.ApiName ?? "Legacy (Modern unavailable)",
        CaptureApiPreference.Legacy => _legacy.GetType().Name,
        CaptureApiPreference.Hybrid => "Hybrid (Modern + Legacy)",
        _ => "Auto"
    };

    public bool IsAvailable => _preference switch
    {
        CaptureApiPreference.Modern => _modern?.IsAvailable ?? false,
        CaptureApiPreference.Legacy => true,
        CaptureApiPreference.Hybrid => _modern?.IsAvailable ?? true,
        _ => _modern?.IsAvailable ?? true
    };

    public HybridCaptureService(ScreenCaptureService legacy, CaptureApiPreference preference = CaptureApiPreference.Auto)
    {
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
        _preference = preference;

        try
        {
            _modern = new ModernCaptureService();
        }
        catch
        {
            _modern = null;
        }
    }

    public async Task<Bitmap?> CaptureWindowAsync(IntPtr hwnd, CancellationToken ct = default)
    {
        if (ShouldTryModern())
        {
            try
            {
                var result = await _modern!.CaptureWindowAsync(hwnd, ct).ConfigureAwait(false);
                if (result != null)
                    return result;
            }
            catch
            {
            }
        }

        return CaptureWindowLegacy(hwnd);
    }

    public async Task<Bitmap?> CaptureMonitorAsync(uint monitorIndex, CancellationToken ct = default)
    {
        if (ShouldTryModern())
        {
            try
            {
                var result = await _modern!.CaptureMonitorAsync(monitorIndex, ct).ConfigureAwait(false);
                if (result != null)
                    return result;
            }
            catch
            {
            }
        }

        return CaptureMonitorLegacy(monitorIndex);
    }

    private bool ShouldTryModern()
    {
        if (_modern == null || !_modern.IsAvailable)
            return false;

        return _preference switch
        {
            CaptureApiPreference.Modern => true,
            CaptureApiPreference.Hybrid => true,
            CaptureApiPreference.Auto => true,
            _ => false
        };
    }

    private Bitmap CaptureWindowLegacy(IntPtr hwnd)
    {
        var hwndLong = hwnd.ToInt64();
        var imageData = ScreenCaptureService.CaptureWindow(hwndLong, 1920, 80);

        var base64Data = imageData.Contains(";base64,", StringComparison.Ordinal)
            ? imageData.Split(',')[1]
            : imageData;

        var bytes = Convert.FromBase64String(base64Data);
        using var ms = new MemoryStream(bytes);
        return new Bitmap(ms);
    }

    private Bitmap CaptureMonitorLegacy(uint monitorIndex)
    {
        var imageData = _legacy.CaptureSingle(monitorIndex, 1920, 80);

        var base64Data = imageData.Contains(";base64,", StringComparison.Ordinal)
            ? imageData.Split(',')[1]
            : imageData;

        var bytes = Convert.FromBase64String(base64Data);
        using var ms = new MemoryStream(bytes);
        return new Bitmap(ms);
    }

    public void Dispose()
    {
        _modern?.Dispose();
        GC.SuppressFinalize(this);
    }
}
