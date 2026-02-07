# windows-desktop-use-mcp

Windows 11 を AI から自在に操作・認識するための MCP サーバー。
AI に Windows の「目（視覚）」「耳（聴覚）」「手足（操作）」を与え、Claude などの MCP クライアントからデスクトップ環境を利用可能にします。

## 主な機能

- **視覚 (Screen Capture)**: モニター、特定のウィンドウ、または任意領域のキャプチャ。
- **聴覚 (Audio & Transcription)**: システム音やマイクの録音、および Whisper AI による高品質なローカル文字起こし。
- **手足 (Desktop Input)**: マウス移動、クリック、ドラッグ、キーボード入力（Unicode対応）、特殊キー操作。
- **ライブ監視 (Streaming)**: 画面の変更をリアルタイムで監視し、HTTP ストリーミングでブラウザから確認可能。

## クイックスタート

### 1. ビルド
```powershell
dotnet build src/WindowsDesktopUse.App/WindowsDesktopUse.App.csproj -c Release
```

### 2. Claude Desktop の設定
`%AppData%\Roaming\Claude\claude_desktop_config.json` に以下を追加します：
```json
{
  "mcpServers": {
    "windows-desktop-use": {
      "command": "C:\\path\\to\\WindowsDesktopUse.App.exe",
      "args": ["--httpPort", "5000"]
    }
  }
}
```

## 利用可能な MCP ツール（概要）

### 視覚・情報取得
- `list_all`: すべてのモニターとウィンドウを一覧表示。
- `capture`: 任意のターゲットを画像としてキャプチャ。
- `watch`: ターゲットの監視・ストリーミングを開始。

### 聴覚
- `listen`: システム音やマイク入力を録音し、テキストに変換。
- `list_audio_devices`: 利用可能なオーディオデバイスを一覧表示。

### 操作
- `mouse_move`: 指定座標へのカーソル移動。
- `mouse_click`: 左/右/中クリック、ダブルクリック。
- `mouse_drag`: ドラッグ＆ドロップ操作。
- `keyboard_type`: 文字列の入力（Unicode対応）。
- `keyboard_key`: 特殊キー（Enter, Tab, Win, Ctrl+C 等）の操作。

詳細な引数や使用例については、[**ツールガイド**](docs/TOOLS.ja.md) を参照してください。

## ドキュメント一覧

- [**ツールリファレンス**](docs/TOOLS.ja.md) - 詳細なコマンド一覧と使用例。
- [**開発者ガイド**](docs/DEVELOPMENT.ja.md) - ビルド、テスト、アーキテクチャ（DLL構成）の詳細。
- [**Whisper 音声認識**](docs/WHISPER.ja.md) - 音声認識機能とモデルについて。

## 動作要件

- Windows 11（または Windows 10 1803 以降）
- .NET 8.0 ランタイム/SDK
- 高DPI環境対応（物理ピクセル座標系）

## ライセンス
MIT License. 詳細は [LICENSE](LICENSE) ファイルを参照してください。
