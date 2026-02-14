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

    [Test]
    public void CaptureInterval_NextCaptureTime_ShouldMaintainConsistentInterval()
    {
        // 絶対時刻スケジュールのテスト
        var startTime = DateTime.UtcNow;
        var nextCaptureTime = startTime;
        var intervalMs = 500; // 500ms間隔
        var timestamps = new List<double>();

        // 3回キャプチャをシミュレート
        for (int i = 0; i < 3; i++)
        {
            // 次のキャプチャ時刻まで待機
            var waitMs = (int)(nextCaptureTime - DateTime.UtcNow).TotalMilliseconds;
            if (waitMs > 0)
            {
                Thread.Sleep(waitMs);
            }

            // キャプチャ完了時刻を記録
            var captureTime = DateTime.UtcNow;
            var ts = (captureTime - startTime).TotalSeconds;
            timestamps.Add(ts);

            // 次のキャプチャ時刻を更新
            nextCaptureTime = nextCaptureTime.AddMilliseconds(intervalMs);

            // 処理時間をシミュレート（50ms）
            Thread.Sleep(50);
        }

        // 間隔が 500ms ± 100ms 以内で維持されていることを検証
        var interval1 = timestamps[1] - timestamps[0];
        var interval2 = timestamps[2] - timestamps[1];

        Assert.That(interval1, Is.EqualTo(0.5).Within(0.1), 
            $"First interval should be 0.5s ± 0.1s, but was {interval1:F3}s");
        Assert.That(interval2, Is.EqualTo(0.5).Within(0.1), 
            $"Second interval should be 0.5s ± 0.1s, but was {interval2:F3}s");
    }

    [Test]
    public void Timestamp_ShouldReflectActualCaptureTime()
    {
        // キャプチャ完了時の実際の時刻を反映するテスト
        var session = new VideoCoViewSession
        {
            StartTime = DateTime.UtcNow
        };

        // 開始時刻を記録
        var startTime = session.StartTime;

        // 処理をシミュレート（100msかかる）
        Thread.Sleep(100);

        // キャプチャ完了時の時刻で ts を計算
        var captureCompletedTime = DateTime.UtcNow;
        var ts = (captureCompletedTime - startTime).TotalSeconds;

        // ts は 0.1秒以上であるべき
        Assert.That(ts, Is.GreaterThan(0.09), 
            "Timestamp should reflect actual capture completion time");
        Assert.That(ts, Is.LessThan(0.2), 
            "Timestamp should not be too far from expected value");
    }
}
