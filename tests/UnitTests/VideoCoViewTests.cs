using NUnit.Framework;
using WindowsDesktopUse.Core;

namespace UnitTests;

[TestFixture]
public class VideoCoViewTests
{
    [Test]
    public void VideoCoViewPayload_ShouldStoreAllProperties()
    {
        var payload = new VideoCoViewPayload(
            SessionId: "test-session",
            Ts: 2.5,
            Frame: "base64imagedata",
            Transcript: "Hello world",
            WindowTitle: "Test Window"
        );

        Assert.That(payload.SessionId, Is.EqualTo("test-session"));
        Assert.That(payload.Ts, Is.EqualTo(2.5));
        Assert.That(payload.Frame, Is.EqualTo("base64imagedata"));
        Assert.That(payload.Transcript, Is.EqualTo("Hello world"));
        Assert.That(payload.WindowTitle, Is.EqualTo("Test Window"));
    }

    [Test]
    public void VideoCoViewPayload_TranscriptCanBeNull()
    {
        var payload = new VideoCoViewPayload(
            SessionId: "test-session",
            Ts: 0.0,
            Frame: "base64",
            Transcript: null,
            WindowTitle: "Window"
        );

        Assert.That(payload.Transcript, Is.Null);
    }

    [Test]
    public void VideoCoViewSession_DefaultValues()
    {
        var session = new VideoCoViewSession();

        Assert.That(session.IntervalMs, Is.EqualTo(2000));
        Assert.That(session.Quality, Is.EqualTo(60));
        Assert.That(session.MaxWidth, Is.EqualTo(640));
        Assert.That(session.ModelSize, Is.EqualTo("base"));
    }

    [Test]
    public void VideoCoViewSession_GetTs_ReturnsElapsedSeconds()
    {
        var session = new VideoCoViewSession
        {
            StartTime = DateTime.UtcNow.AddSeconds(-5)
        };

        var ts = session.GetTs();

        Assert.That(ts, Is.GreaterThan(4.5));
        Assert.That(ts, Is.LessThan(6.0));
    }

    [Test]
    public void VideoCoViewSession_Dispose_CancelsCts()
    {
        var session = new VideoCoViewSession();

        session.Dispose();

        Assert.That(session.Cts.IsCancellationRequested, Is.True);
    }
}
