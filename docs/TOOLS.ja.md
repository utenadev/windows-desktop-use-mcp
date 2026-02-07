# windows-desktop-use-mcp ツール詳細

このドキュメントでは、本サーバーで利用可能な MCP ツールの詳細について説明します。

## ツール一覧

| カテゴリ | ツール名 | 説明 |
|----------|----------|------|
| **情報取得** | `list_all` | すべてのモニターとウィンドウを一覧表示 |
| | `list_monitors` | 利用可能なモニターの一覧を取得 |
| | `list_windows` | 表示されているアプリケーションの一覧を取得 |
| **視覚 (Capture)** | `capture` | 任意のターゲット（モニター、ウィンドウ、領域）をキャプチャ |
| | `see` | モニターまたはウィンドウのスクリーンショットを撮影 |
| | `capture_region` | 画面の指定領域をキャプチャ |
| | `capture_window` | HWND を指定してウィンドウをキャプチャ |
| **視覚 (Watch)** | `watch` | 任意のターゲットの監視・ストリーミングを開始 |
| | `stop_watch` | 監視セッションを停止 |
| | `start_watching` | 画面キャプチャストリームを開始 (レガシー) |
| | `stop_watching` | キャプチャストリームを停止 (レガシー) |
| | `get_latest_frame`| 最新フレームを取得 |
| **操作 (Mouse)** | `mouse_move` | 指定座標へマウスカーソルを移動 |
| | `mouse_click` | マウスクリック（左/右/中、ダブルクリック） |
| | `mouse_drag` | ドラッグ＆ドロップ操作 |
| **操作 (Keyboard)**| `keyboard_type` | 文字列を入力（Unicode対応） |
| | `keyboard_key` | 特殊キーの操作（Enter, Tab, Ctrl+C 等） |
| **聴覚** | `listen` | システム音やマイクを文字起こし |
| | `list_audio_devices` | オーディオデバイスの一覧を取得 |
| | `get_active_audio_sessions` | 実行中のオーディオセッション一覧を取得 |
| **設定/AI** | `get_whisper_model_info` | 利用可能な Whisper モデルの情報を取得 |

---

## ツール詳細リファレンス

### スクリーン & ウィンドウキャプチャ

#### `capture` (推奨)
一つのツールで、モニター、ウィンドウ、または指定領域をキャプチャします。
- **引数:**
  - `target` (string): "primary", "monitor", "window", "region" (デフォルト: "primary")
  - `targetId` (string): モニター番号または HWND (target が "monitor" または "window" の場合に必要)
  - `x`, `y`, `w`, `h` (number): 領域指定 (target が "region" の場合に必須)
  - `quality` (number): JPEG 品質 1-100 (デフォルト: 80)
  - `maxWidth` (number): 最大幅。これより大きい場合はアスペクト比を維持して縮小されます (デフォルト: 1920)

---

### デスクトップ操作 (Input)

#### `mouse_move`
マウスカーソルを指定した座標に移動させます。
- **引数:**
  - `x` (number): 移動先の X 座標（物理ピクセル）
  - `y` (number): 移動先の Y 座標（物理ピクセル）

#### `mouse_click`
マウスのクリックをシミュレートします。
- **引数:**
  - `button` (string): "left", "right", "middle" (デフォルト: "left")
  - `count` (number): クリック回数。2 を指定するとダブルクリックになります (デフォルト: 1)

#### `mouse_drag`
指定した開始座標から終了座標まで、左ボタンを押した状態でドラッグ＆ドロップします。
- **引数:**
  - `startX`, `startY` (number): ドラッグ開始座標
  - `endX`, `endY` (number): ドロップ先の座標

#### `keyboard_type`
指定した文字列をキーボード入力として送信します。Unicode 文字（日本語など）に対応しています。
- **引数:**
  - `text` (string): 入力する文字列

#### `keyboard_key`
特殊キーの押下、解放、またはクリック（押して離す）をシミュレートします。
- **引数:**
  - `key` (string): キー名。
    - 利用可能: `enter`, `return`, `tab`, `escape`, `esc`, `space`, `backspace`, `delete`, `del`, `left`, `up`, `right`, `down`, `home`, `end`, `pageup`, `pagedown`, `shift`, `ctrl`, `alt`, `win`
  - `action` (string): "click", "press" (押し続ける), "release" (離す) (デフォルト: "click")

---

### オーディオ & 音声認識

#### `listen`
Whisper を使用してオーディオを録音し、文字起こしを行います。
- **引数:**
  - `source` (string): "system" (システム音), "microphone", "file", "audio_session" (デフォルト: "system")
  - `sourceId` (string): ファイルパスまたはオーディオセッション ID
  - `duration` (number): 録音秒数 (デフォルト: 10)
  - `language` (string): 言語。 "auto" または "ja", "en", "zh" 等 (デフォルト: "auto")
  - `modelSize` (string): モデルサイズ。 "tiny", "base", "small", "medium", "large" (デフォルト: "base")
  - `translate` (boolean): 英語に翻訳するかどうか (デフォルト: false)