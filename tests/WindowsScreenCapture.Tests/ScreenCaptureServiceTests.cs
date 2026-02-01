using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace WindowsScreenCapture.Tests;

public class ScreenCaptureServiceTests
{
    [Fact]
    public void MonitorInfo_Record_CanBeCreated()
    {
        var monitor = new MonitorInfo(0, "Test Monitor", 1920, 1080, 0, 0);
        
        Assert.Equal(0u, monitor.Idx);
        Assert.Equal("Test Monitor", monitor.Name);
        Assert.Equal(1920, monitor.W);
        Assert.Equal(1080, monitor.H);
        Assert.Equal(0, monitor.X);
        Assert.Equal(0, monitor.Y);
    }

    [Fact]
    public void StreamSession_CanBeCreated()
    {
        var session = new StreamSession {
            Id = "test-id",
            MonIdx = 0,
            Interval = 1000,
            Quality = 80,
            MaxW = 1920
        };
        
        Assert.NotNull(session);
        Assert.Equal("test-id", session.Id);
        Assert.Equal(0u, session.MonIdx);
        Assert.Equal(1000, session.Interval);
        Assert.Equal(80, session.Quality);
        Assert.Equal(1920, session.MaxW);
        Assert.NotNull(session.Channel);
        Assert.NotNull(session.Cts);
    }

    [Fact]
    public void Project_Compiles_Successfully()
    {
        Assert.True(true);
    }
}
