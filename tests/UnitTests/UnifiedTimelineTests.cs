using NUnit.Framework;
using WindowsDesktopUse.Core;

namespace UnitTests;

[TestFixture]
public class UnifiedTimelineTests
{
    [Test]
    public void StreamSession_StartTime_ShouldBeUtcNow()
    {
        var session = new StreamSession
        {
            Id = "test-session",
            StartTime = DateTime.UtcNow
        };

        Assert.That(session.StartTime, Is.Not.EqualTo(default(DateTime)));
        Assert.That(session.StartTime.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public void StreamSession_RelativeTime_ShouldReturnPositiveValue()
    {
        var session = new StreamSession
        {
            Id = "test-session",
            StartTime = DateTime.UtcNow.AddSeconds(-5) // Started 5 seconds ago
        };

        var relativeTime = session.RelativeTime;

        Assert.That(relativeTime, Is.GreaterThan(4.0));
        Assert.That(relativeTime, Is.LessThan(10.0));
    }

    [Test]
    public void UnifiedEventPayload_ShouldStoreAllProperties()
    {
        var payload = new UnifiedEventPayload(
            SessionId: "test-session",
            SystemTime: DateTime.UtcNow.ToString("O"),
            RelativeTime: 10.5,
            Type: "video",
            Data: "base64data",
            Metadata: new UnifiedEventMetadata(
                WindowTitle: "Test Window",
                Hash: "abc123"
            )
        );

        Assert.That(payload.SessionId, Is.EqualTo("test-session"));
        Assert.That(payload.RelativeTime, Is.EqualTo(10.5));
        Assert.That(payload.Type, Is.EqualTo("video"));
        Assert.That(payload.Data, Is.EqualTo("base64data"));
        Assert.That(payload.Metadata.WindowTitle, Is.EqualTo("Test Window"));
        Assert.That(payload.Metadata.Hash, Is.EqualTo("abc123"));
    }

    [Test]
    public void UnifiedEventMetadata_AudioProperties_ShouldBeOptional()
    {
        var metadata = new UnifiedEventMetadata(
            Language: "ja",
            SegmentStartTime: 5.0,
            SegmentEndTime: 10.0
        );

        Assert.That(metadata.Language, Is.EqualTo("ja"));
        Assert.That(metadata.SegmentStartTime, Is.EqualTo(5.0));
        Assert.That(metadata.SegmentEndTime, Is.EqualTo(10.0));
        Assert.That(metadata.WindowTitle, Is.Null);
    }
}
