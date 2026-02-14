# 実装指示書: タイムスタンプ精度およびキャプチャ同期の改善

## 📌 作業コンテキスト
**本修正は現在の作業ブランチ (`fix/wgc-implementation`) でそのまま継続してください。**
理由は、今回の精度改善が直前の「GPU対応（PrintWindow）実装」で追加された `StartTime` 等のコード基盤を直接リファクタリングするものであるため、また、GPU対応が有効な状態でなければ正しい時刻精度の検証が困難であるためです。

## 1. 目的
`watch_video` および `watch_video_v2` において、キャプチャ処理のオーバーヘッド（100ms〜500ms）による「累積遅延」と「映像・音声の同期ズレ」を解消する。

## 2. 修正方針
- **絶対時刻スケジュール**: ループ開始時に「次のキャプチャ予定時刻 (`nextCaptureTime`)」を計算し、そこまで正確に待機する。
- **実測タイムスタンプ**: `ts` (タイムスタンプ) は、ループ開始時ではなく、**キャプチャ処理が完了した直後の時刻**を使用して計算する。

---

## 3. 具体的な修正タスク

### 3.1 `VideoCaptureService.cs` の修正
1.  `CaptureLoopAsync` 内の待機ロジックを、`startTime` ベースから `nextCaptureTime` ベースに変更する。
    - ループの初回開始時に `nextCaptureTime = session.StartTime` とする。
    - 各ループの開始時に `nextCaptureTime - DateTime.UtcNow` を計算し、正の値であればその分だけ `Task.Delay` する。
    - キャプチャ完了後、`nextCaptureTime = nextCaptureTime.AddMilliseconds(session.FrameIntervalMs)` で更新する。
2.  `CreateVideoPayload` に `double ts` 引数を追加し、ハードコードを廃止する。
3.  `CaptureLoopAsync` 内で、`CaptureVideoFrameAsync` 完了直後の `DateTime.UtcNow` を用いて `ts` を計算し、ペイロードに渡す。

### 3.2 `DesktopUseTools.cs` (`watch_video_v2`) の修正
1.  `watch_video_v2` 内の `Task.Run` ループを同様に `nextCaptureTime` 方式に書き換える。
2.  映像フレームの `frameBase64` を取得した**直後**の時刻で `ts` を確定させ、その値を MCP 通知のペイロードに使用する。
3.  音声文字起こし結果（`transcriptionResult`）も、確定した `ts` とセットにして通知を送信する。

### 3.3 `UnifiedTimelineTests.cs` (テスト) の追加・更新
- 連続して 3回キャプチャを行った際、それぞれの `ts` の間隔が指定した `intervalMs` (±100ms以内) で維持されていることを検証するテストを追加する。

---

## 4. 技術的留意点
- **負の待機時間の扱い**: `nextCaptureTime - DateTime.UtcNow` が負（＝処理が遅れている）の場合、待機せずに即座に次のキャプチャを実行すること。これにより、一時的な遅れを取り戻すことができる。
- **ログ**: `Console.Error.WriteLine` を使用し、大幅な遅延（例：予定より 500ms 以上遅れた場合）を警告として出力すると調査に役立つ。

## 5. 受入基準
1. `watch_video_v2` を 2秒間隔で 1分間実行し、最後の通知の `ts` が 60.0秒 前後であることを確認する（累積遅延がないこと）。
2. 文字起こしテキストの内容と、映像フレームの内容が、時間軸上で一致していること。
