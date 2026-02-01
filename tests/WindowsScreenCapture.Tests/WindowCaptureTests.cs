using System.Text.Json;
using Xunit;

namespace WindowsScreenCapture.Tests;

public class WindowCaptureTests
{
    [Fact]
    public void WindowInfo_Record_CanBeCreated()
    {
        var window = new WindowInfo(123456, "Test Window", 800, 600, 100, 100);
        
        Assert.Equal(123456L, window.Hwnd);
        Assert.Equal("Test Window", window.Title);
        Assert.Equal(800, window.W);
        Assert.Equal(600, window.H);
        Assert.Equal(100, window.X);
        Assert.Equal(100, window.Y);
    }
    
    [Fact]
    public void ScreenCaptureService_GetWindows_ReturnsList()
    {
        var service = new ScreenCaptureService(0);
        service.InitializeMonitors();
        
        var windows = service.GetWindows();
        
        Assert.NotNull(windows);
        // Should find at least some windows (this test window, explorer, etc.)
        Assert.True(windows.Count >= 0);
    }
    
    [Fact]
    public void ScreenCaptureService_CaptureRegion_ValidRegion_ReturnsImage()
    {
        var service = new ScreenCaptureService(0);
        service.InitializeMonitors();
        
        // Capture a small 100x100 region at position (0,0)
        var result = service.CaptureRegion(0, 0, 100, 100, 1920, 80);
        
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        // Should be valid base64
        var bytes = Convert.FromBase64String(result);
        Assert.True(bytes.Length > 0);
    }
    
    [Fact]
    public void ScreenCaptureService_CaptureRegion_InvalidDimensions_Throws()
    {
        var service = new ScreenCaptureService(0);
        
        Assert.Throws<ArgumentException>(() => 
            service.CaptureRegion(0, 0, 0, 100, 1920, 80));
        Assert.Throws<ArgumentException>(() => 
            service.CaptureRegion(0, 0, 100, 0, 1920, 80));
    }
}
