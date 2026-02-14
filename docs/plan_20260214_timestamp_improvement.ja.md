# タイムスタンプ精度改善設計書

## 1. 問題の概要

現在の実装では、キャプチャループの**開始時刻**を基準に `ts` (タイムスタンプ) を計算しているが、実際のキャプチャ完了時刻と乖離している。

### 1.1 現在の問題コード

```csharp
while (!session.Cts.IsCancellationRequested)
{
    var captureStartTime = DateTime.UtcNow;  // ← ここで ts を計算
    var ts = (captureStartTime - startTime).TotalSeconds;  // 2.0, 4.0, 6.0...
    
    // ... キャプチャ処理（100-500msかかる）...
    var frame = await CaptureFrameAsync();  // ← 実際のキャプチャは遅れて完了
    
    // ts=2.0 だが、実際は 2.3秒時点でキャプチャ完了
    await SendNotification(ts, frame);
    
    // 待機時間計算も開始時刻ベースのため、間隔が短縮される
    var elapsed = (int)(DateTime.UtcNow - captureStartTime).TotalMilliseconds;
    var remainingWait = intervalMs - elapsed;
}
```

### 1.2 問題の影響

1. **映像・音声の同期ズレ**
   - 映像の `ts=2.0` は実際には 2.3秒時点の映像
   - 音声の `ts=2.0` は実際には 2.0秒時点の音声
   - 結果: 0.3秒のズレが発生

2. **固定間隔の維持失敗**
   - 処理に時間がかかると待機時間が短縮・消失
   - 2秒間隔のはずが、1.5秒や1.0秒間隔になる

3. **累積遅延**
   - 遅れが次の遅れを生む悪循環
   - 長時間実行すると数秒単位のズレが発生

## 2. 解決方針

### 2.1 絶対時間管理

**次のキャプチャ時刻**を厳密に管理し、そこまで正確に待機する。

```csharp
var nextCaptureTime = session.StartTime;  // 絶対時刻で管理

while (!cts.IsCancellationRequested)
{
    // 1. 次のキャプチャ時刻まで正確に待機
    var delay = (int)(nextCaptureTime - DateTime.UtcNow).TotalMilliseconds;
    if (delay > 0) await Task.Delay(delay, cts.Token);
    
    // 2. キャプチャ実行
    var frame = await CaptureAsync();
    
    // 3. キャプチャ完了時の実際の時刻で ts を計算
    var actualCaptureTime = DateTime.UtcNow;
    var ts = (actualCaptureTime - session.StartTime).TotalSeconds;
    
    // 4. 次のキャプチャ時刻を厳密に更新（間隔を維持）
    nextCaptureTime = nextCaptureTime.AddMilliseconds(intervalMs);
    
    // 5. 通知送信（実際のキャプチャ時刻を反映した ts）
    await SendNotification(ts, frame);
}
```

### 2.2 修正対象

#### A. VideoCaptureService (watch_video)

現在:
- `startTime` をループ先頭で取得
- 処理時間を引いた残り時間を待機
- 遅れると即座に次のループへ

修正後:
- `nextCaptureTime` を絶対時刻で管理
- 厳密な時刻まで待機
- 遅れても次のキャプチャ時刻は維持

#### B. DesktopUseTools.watch_video_v2

現在:
- ループ開始時に `ts` を計算
- 音声録音・文字起こし完了後も同じ `ts` を使用

修正後:
- キャプチャ完了時または通知送信時に `ts` を計算
- 文字起こし遅延も考慮した時刻管理

## 3. 実装詳細

### 3.1 VideoCaptureService の修正

```csharp
private async Task CaptureLoopAsync(...)
{
    var nextCaptureTime = session.StartTime;  // ← 追加
    
    while (!cts.Token.IsCancellationRequested)
    {
        // 次のキャプチャ時刻まで待機
        var waitMs = (int)(nextCaptureTime - DateTime.UtcNow).TotalMilliseconds;
        if (waitMs > 0)
        {
            try { await Task.Delay(waitMs, cts.Token); }
            catch (OperationCanceledException) { break; }
        }
        
        try
        {
            // キャプチャ実行
            using var frame = await CaptureVideoFrameAsync(target, session.MaxWidth);
            if (frame == null) continue;
            
            // 実際のキャプチャ完了時刻で ts を計算 ← 修正
            var captureCompletedTime = DateTime.UtcNow;
            var ts = (captureCompletedTime - session.StartTime).TotalMilliseconds / 1000.0;
            
            // ペイロード生成（実際の時刻を反映）
            var payload = CreateVideoPayload(session, imageData, ts, ...);
            
            // 次のキャプチャ時刻を更新（厳密に間隔を維持）← 修正
            nextCaptureTime = nextCaptureTime.AddMilliseconds(session.FrameIntervalMs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[VideoCapture] Error: {ex.Message}");
        }
    }
}
```

### 3.2 CreateVideoPayload の修正

```csharp
private VideoPayload CreateVideoPayload(
    VideoSession session, 
    string imageData, 
    double ts,  // ← 引数として受け取る
    string eventTag)
{
    var now = DateTime.UtcNow;
    var target = session.TargetInfo;
    
    return new VideoPayload(
        Timestamp: TimeSpan.FromSeconds(ts).ToString(@"hh\:mm\:ss\.f"),  // ← ts を使用
        SystemTime: now.ToString("O"),
        // ...
    );
}
```

### 3.3 DesktopUseTools.watch_video_v2 の修正

```csharp
_ = Task.Run(async () =>
{
    var nextCaptureTime = startTime;  // ← 追加
    
    while (!session.Cts.IsCancellationRequested)
    {
        // 次のキャプチャ時刻まで待機 ← 追加
        var waitMs = (int)(nextCaptureTime - DateTime.UtcNow).TotalMilliseconds;
        if (waitMs > 0)
        {
            await Task.Delay(waitMs, session.Cts.Token).ConfigureAwait(false);
        }
        
        try
        {
            // キャプチャ実行
            var frameBase64 = ScreenCaptureService.CaptureRegion(x, y, w, h, maxWidth, quality);
            
            // キャプチャ完了時の実際の時刻で ts を計算 ← 修正
            var actualCaptureTime = DateTime.UtcNow;
            var ts = (actualCaptureTime - startTime).TotalSeconds;
            
            // 次のキャプチャ時刻を更新 ← 追加
            nextCaptureTime = nextCaptureTime.AddMilliseconds(intervalMs);
            
            // 通知送信（実際の時刻を反映）
            await server.SendNotificationAsync(..., ts, ...).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WatchVideoV2] Error: {ex.Message}");
        }
    }
});
```

## 4. 期待される効果

### 4.1 時刻精度

- **ts の精度向上**: 理論値 ±0.1秒以内
- **固定間隔維持**: 2秒間隔なら厳密に 2.0秒間隔
- **同期精度**: 映像・音声のズレを最小限に抑制

### 4.2 具体例

```
修正前:
ループ開始: 00:00:00.0 → ts=0.0 → キャプチャ完了: 00:00:00.3 (0.3秒ズレ)
ループ開始: 00:00:02.0 → ts=2.0 → キャプチャ完了: 00:00:02.5 (0.5秒ズレ)
ループ開始: 00:00:04.0 → ts=4.0 → キャプチャ完了: 00:00:04.8 (0.8秒ズレ)

修正後:
キャプチャ時刻: 00:00:00.0 → ts=0.0 (厳密)
キャプチャ時刻: 00:00:02.0 → ts=2.0 (厳密)
キャプチャ時刻: 00:00:04.0 → ts=4.0 (厳密)
```

## 5. リスクと対策

### 5.1 処理時間が間隔を超える場合

**問題**: 2秒間隔で処理に3秒かかる場合

**対策**:
- `nextCaptureTime` は過去の時刻になるが、即座にキャプチャ実行
- 次の時刻も過去なら連続実行（バッファリング）
- またはスキップして次の時刻に同期

```csharp
var waitMs = (int)(nextCaptureTime - DateTime.UtcNow).TotalMilliseconds;
if (waitMs > 0) 
{
    await Task.Delay(waitMs, cts.Token);
}
// waitMs <= 0 の場合は即座にキャプチャ（遅延を許容）
```

### 5.2 文字起こしの遅延

**問題**: 音声文字起こしに時間がかかる

**対策**:
- 文字起こしは非同期で実行
- 通知はキャプチャ完了時の `ts` を使用
- 文字起こし結果は後から別途通知（または次のフレームに付与）

## 6. テスト計画

### 6.1 単体テスト

```csharp
[Test]
public void Timestamp_ShouldReflectActualCaptureTime()
{
    var session = new VideoSession { StartTime = DateTime.UtcNow };
    
    // シミュレーション: 処理に100msかかる
    Thread.Sleep(100);
    var ts = (DateTime.UtcNow - session.StartTime).TotalSeconds;
    
    // ts は 0.1秒以上であるべき
    Assert.That(ts, Is.GreaterThan(0.09));
}
```

### 6.2 統合テスト

```csharp
[Test]
public async Task CaptureInterval_ShouldBeConsistent()
{
    var timestamps = new List<double>();
    
    // 3回キャプチャ実行
    for (int i = 0; i < 3; i++)
    {
        var start = DateTime.UtcNow;
        await CaptureAsync();
        var ts = (DateTime.UtcNow - start).TotalSeconds;
        timestamps.Add(ts);
        await Task.Delay(1000);
    }
    
    // 間隔は 1.0秒 ± 0.1秒
    var interval1 = timestamps[1] - timestamps[0];
    var interval2 = timestamps[2] - timestamps[1];
    
    Assert.That(interval1, Is.EqualTo(1.0).Within(0.1));
    Assert.That(interval2, Is.EqualTo(1.0).Within(0.1));
}
```

## 7. まとめ

- **現状**: 開始時刻ベースのため、実際のキャプチャ時刻とズレる
- **修正**: 絶対時間管理 + キャプチャ完了時の時刻取得
- **効果**: 厳密な固定間隔 + 正確なタイムスタンプ
- **影響**: 映像・音声同期精度の大幅向上
