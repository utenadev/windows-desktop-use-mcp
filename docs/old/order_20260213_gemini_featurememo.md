# 高効率ビデオパイプライン実装指示書 (2026-02-13)

## 1. 概要
本タスクの目的は、動画コンテンツ（ブラウザ、動画再生ソフト等）を対象に、低遅延・低消費トークンでLLMに視覚情報を送るためのパイプラインを `WindowsDesktopUse` プロジェクトに実装することです。

## 2. タスク詳細

### タスク A: UI Automation による動的ターゲット特定
動画エリアを特定し、自動追跡するロジックを実装してください。
- **実装場所:** `WindowsDesktopUse.Screen` 内に `VideoTargetFinder.cs` (仮) を作成。
- **要件:** 
    - `System.Windows.Automation` を使用し、特定の名称（例: "YouTube Video Player", "Video Player"）や役割（`ControlType.Pane` かつ特定のクラス名）を持つ要素の `BoundingRectangle` を取得する。
    - ウィンドウタイトルから動画タイトルなどのメタデータを抽出する。
    - **動的追跡:** ウィンドウが移動・リサイズされた場合でも、キャプチャ範囲を自動更新する仕組みを用意する。

### タスク B: 高効率映像処理（GDI+ 最適化）
LLM向けに必要十分な画質を維持しつつ、処理負荷を最小化します。
- **リサイズ設定:**
    - `Graphics.InterpolationMode` を `HighQualityBicubic` から `Bilinear` または `NearestNeighbor` に変更。
    - アスペクト比を維持しつつ、最大幅 640px (360p) に縮小。
- **JPEG 圧縮:**
    - `EncoderParameters` を使い、`Quality` を **60〜70** に設定。
    - `System.Drawing.Imaging` を使用し、メモリ上での変換効率を最大化する。

### タスク C: 変化検知ロジック (Visual Change Detection)
静止画の重複送信を避け、動きがある時だけ送信する「賢い送信」を実装します。
- **アルゴリズム:**
    1. 画面を 8x8 または 16x16 のグリッドに分割。
    2. 各グリッドの中心ピクセルの RGB 値を前回のフレームと比較。
    3. 全グリッドの 5〜10% 以上で変化があった場合のみ「動きあり」と判定。
- **強制キーフレーム:** 
    - 変化がなくても、設定した秒数（例: 10秒）ごとに 1 回は最新状態を強制送信する。

### タスク D: 構造化ペイロードの定義
LLM が文脈を理解しやすいよう、以下の JSON 構造を `WindowsDesktopUse.Core` に定義してください。
```json
{
  "timestamp": "00:00:00",       // 動画内の再生位置（取得可能な場合）
  "system_time": "ISO8601",
  "window_info": {
    "title": "String",
    "is_active": "Boolean"
  },
  "visual_metadata": {
    "has_change": "Boolean",
    "event_tag": "String (Scene Change, etc.)"
  },
  "image_data": "Base64 JPEG",
  "ocr_text": "String (Optional)"
}
```

### タスク E: MCP ツール `watch_video` の追加
- **実装場所:** `WindowsDesktopUse.App/DesktopUseTools.cs`
- **引数:**
    - `target_name`: "YouTube", "ActiveWindow" 等の識別子。
    - `fps`: 希望フレームレート（デフォルト 10〜15fps）。
    - `quality`: JPEG 品質。
- **動作:** 
    - ツールが呼ばれると `ScreenCaptureService` で新しいストリーミングセッションを開始し、通知経由で構造化ペイロードをクライアントに送出する。

## 3. 実装上の重要ルール

1.  **メモリ管理 (Crucial):**
    - キャプチャごとに生成される `Bitmap`, `Graphics` オブジェクトは、必ず `using` ブロックで確実に `Dispose()` すること。
2.  **非同期処理:**
    - キャプチャと圧縮処理は `Task.Run` を使用し、メインの JSON-RPC 受信スレッドをブロックしないこと。
3.  **送信スタック:**
    - LLM の応答待ち中に新しいフレームが生成された場合、古いフレームは破棄し、常に最新の 1 枚だけをキューに保持すること。
4.  **ログ出力:**
    - 動作状況のログは必ず `Console.Error.WriteLine` を使用すること（`stdout` はプロトコル通信用）。
