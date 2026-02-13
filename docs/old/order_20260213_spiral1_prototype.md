# 高効率ビデオパイプライン・プロトタイプ実装指示書 (Spiral 1 - 改訂版)

## 1. 目的
既存の `camera_capture_stream`（360p/15fps）をベースに、映像フレームと文字起こし結果に共通の「セッション相対時間」を付与。LLM が動画の内容と発話を正確な時系列で把握できる基盤を構築します。

## 2. タスク詳細

### タスク A: 共通タイムライン付きペイロードの導入 (Core)
映像と音声を統合管理するためのモデルを定義します。
- **実装場所:** `src/WindowsDesktopUse.Core/Models.cs`
- **内容:** 
    - `UnifiedEventPayload` レコードを定義。
    - プロパティ:
        - `SessionId`: セッション識別子。
        - `SystemTime`: ISO8601 形式の現在時刻。
        - `RelativeTime`: **セッション開始からの経過秒数 (double)**。
        - `Type`: "video" または "audio"。
        - `Data`: Base64画像データ または 文字起こしテキスト。
        - `Metadata`: ウィンドウタイトル、音量、ハッシュ値等の付随情報。

### タスク B: セッション管理の拡張
`StreamSession` に開始時刻を保持させます。
- **実装場所:** `src/WindowsDesktopUse.Core/Models.cs`
- **内容:** 
    - `StartTime` (DateTime) プロパティを追加。
    - セッション生成時に `DateTime.UtcNow` をセット。
    - `RelativeTime = (DateTime.UtcNow - StartTime).TotalSeconds` で相対時間を算出するユーティリティメソッドを追加。

### タスク C: Whisper.net の OS 言語自動設定
- **実装場所:** `src/WindowsDesktopUse.Transcription/WhisperTranscriptionService.cs`
- **内容:** 
    - `CultureInfo.CurrentCulture.TwoLetterISOLanguageName` を使用。
    - 明示的な言語指定がない場合、この OS 言語コードをデフォルトとして文字起こしを開始する。

### タスク D: 新 MCP ツール `watch_video_v1` の実装
既存の `camera_capture_stream` と `Listen` の機能を統合したプロトタイプツールを作成します。
- **実装場所:** `src/WindowsDesktopUse.App/DesktopUseTools.cs`
- **仕様:**
    - 引数: `x, y, w, h` (または HWND), `quality`, `fps`.
    - 内部動作:
        1. 映像キャプチャ（`StartRegionStream2` 相当）を開始。
        2. 同時に音声キャプチャ（ループバック）を開始。
        3. **映像フレーム取得時:** `UnifiedEventPayload` を作成し、`RelativeTime` を付与して Notification 送信。
        4. **音声セグメント確定時:** Whisper の出力（相対開始時間 + セッションオフセット）から `RelativeTime` を算出し、`UnifiedEventPayload` として Notification 送信。

## 3. 実装上のポイント
- **既存資産の活用:** `qwencode` が実装した `ToJpegBase64Fixed` (640x360) ロジックをそのまま利用してください。
- **時間の整合性:** 映像と音声で `StartTime` を共有し、LLM 側で `RelativeTime` に基づいてソートできるようにします。
