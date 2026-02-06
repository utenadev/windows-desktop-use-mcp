using System.Drawing;
using System.Runtime.InteropServices;

/// <summary>
/// Capture API preference options
/// </summary>
public enum CaptureApiPreference
{
    /// <summary>Automatically choose best available API</summary>
    Auto,
    /// <summary>Use Windows.Graphics.Capture API only (modern)</summary>
    Modern,
    /// <summary>Use PrintWindow API only (legacy)</summary>
    Legacy,
    /// <summary>Try modern first, fallback to legacy</summary>
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
/// Windows Graphics Capture API implementation
/// Requires Windows 10 1803+ or Windows 11
/// 
/// TODO: This is a stub implementation. Full implementation requires:
/// - C#/WinRT (CsWinRT) projection for Windows.Graphics.Capture
/// - Direct3D11 integration for frame capture
/// - See: https://github.com/microsoft/Windows.UI.Composition-Win32-Samples
/// </summary>
public class ModernCaptureService : ICaptureService, IDisposable
{
    public string ApiName => "Windows.Graphics.Capture (Stub)";
    
    public bool IsAvailable 
    { 
        get
        {
            // Check if Windows.Graphics.Capture is supported
            // Windows 10 1803+ (build 17134) or Windows 11
            return Environment.OSVersion.Version.Build >= 17134 &&
                   IsGraphicsCaptureAvailable();
        }
    }

    public ModernCaptureService()
    {
        // Full implementation requires:
        // 1. C#/WinRT projection generation
        // 2. Direct3D11 device creation
        // 3. GraphicsCaptureItem interop
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
        // In a full implementation, this would check:
        // GraphicsCaptureSession.IsSupported()
        // For now, assume not available until properly implemented
        return false;
    }
}

/// <summary>
/// Hybrid capture service that tries modern API first, falls back to legacy
/// </summary>
public class HybridCaptureService : ICaptureService
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

        // Try to create modern service
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
        // Try modern API first if available and preferred
        if (ShouldTryModern())
        {
            try
            {
                var result = await _modern!.CaptureWindowAsync(hwnd, ct);
                if (result != null)
                    return result;
            }
            catch
            {
                // Fall through to legacy
            }
        }

        // Fallback to legacy
        return CaptureWindowLegacy(hwnd);
    }

    public async Task<Bitmap?> CaptureMonitorAsync(uint monitorIndex, CancellationToken ct = default)
    {
        // Try modern API first if available and preferred
        if (ShouldTryModern())
        {
            try
            {
                var result = await _modern!.CaptureMonitorAsync(monitorIndex, ct);
                if (result != null)
                    return result;
            }
            catch
            {
                // Fall through to legacy
            }
        }

        // Fallback to legacy - use existing ScreenCaptureService
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
        // Use existing capture logic from ScreenCaptureTools
        var hwndLong = hwnd.ToInt64();
        var imageData = _legacy.CaptureWindow(hwndLong, 1920, 80);
        
        // Convert base64 to bitmap
        var base64Data = imageData.Contains(";base64,") 
            ? imageData.Split(',')[1] 
            : imageData;
        
        var bytes = Convert.FromBase64String(base64Data);
        using var ms = new MemoryStream(bytes);
        return new Bitmap(ms);
    }

    private Bitmap CaptureMonitorLegacy(uint monitorIndex)
    {
        // Use existing capture logic from ScreenCaptureService
        var imageData = _legacy.CaptureSingle(monitorIndex, 1920, 80);
        
        // Convert base64 to bitmap
        var base64Data = imageData.Contains(";base64,") 
            ? imageData.Split(',')[1] 
            : imageData;
        
        var bytes = Convert.FromBase64String(base64Data);
        using var ms = new MemoryStream(bytes);
        return new Bitmap(ms);
    }

    public void Dispose()
    {
        _modern?.Dispose();
    }
}
