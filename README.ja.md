# windows-desktop-use-mcp

Windows 11 を AI から自在に操作・認識するための MCP サーバー。
AI に Windows の「目（視覚）」「耳（聴覚）」「手足（操作）」を与え、Claude などの MCP クライアントからデスクトップ環境を利用可能にします。

[English](README.md) | [日本語](README.ja.md)

## 主な機能

- **視覚 (Screen Capture)**: モニター、特定のウィンドウ、または任意領域のキャプチャ。GPUアクセラレーション対応。
- **聴覚 (Audio & Transcription)**: システム音やマイクの録音、および Whisper AI による高品質なローカル文字起こし。
- **手足 (Desktop Input)**: マウス移動、クリック、ドラッグ、安全なナビゲーションキー操作。
- **補助 (Utility)**: UI Automation によるウィンドウテキストの構造化抽出 (Markdown)。

## クイックスタート

### ビルド済み実行ファイル（推奨）

1. [Releases](../../releases) から最新の `WindowsDesktopUse.zip` をダウンロード・展開。
2. `WindowsDesktopUse.exe setup` を実行して Claude Desktop に自動登録。
3. Claude Desktop を再起動。

## 利用可能な MCP ツール

### 視覚系（Visual）
- `visual_list`: モニターやウィンドウの一覧。
- `visual_capture`: 静止画取得（Normal=30/Detailed=70 画質）。
- `visual_watch`: 同期ストリーミング。
- `visual_stop`: 全セッション停止。

### 操作系（Input）
- `input_mouse`: 移動、クリック、ドラッグ。
- `input_window`: 閉じる、最小化、最大化、復元。
- `keyboard_key`: 安全なナビゲーションキー操作（セキュリティ制限あり）。

### 補助・音声（Hearing & Utility）
- `listen`: 録音と文字起こし。
- `read_window_text`: ウィンドウ内テキストの Markdown 抽出。

---

## 💡 AI への指示のコツ（テスト/デモ）

動画解析など、連続してキャプチャを行う際は、以下の **「3ステップ・プロンプト」** を使用すると、トークンの消費を劇的に抑えつつ正確な分析が可能です。

### 1. メモリ管理ルールの宣言
> 「WindowsDesktopUse MCP サーバを使用し、_llm_instruction の指示を厳守してください。image は即時破棄すること。」

### 2. バッチデータ収集
> 「YouTube ウィンドウの動画領域を対象に、2秒おきに 10フレーム取得してください。画像は即座に捨て、テキスト要約のみ記録してください。」

### 3. 総合分析
> 「収集した 10フレームの要約履歴を元に、動画の内容を解説してください。」

---

## ドキュメント一覧
- [**ツールリファレンス**](docs/TOOLS.ja.md) - 詳細なコマンド一覧と使用例。
- [**開発者ガイド**](docs/DEVELOPMENT.ja.md) - アーキテクチャとビルド手順。
- [**画質テスト報告書**](docs/quality_test_report.md) - Quality 30/70 の違いについて。

## 動作要件
- Windows 11 / 10 1803+
- .NET 8.0 ランタイム

## ライセンス
MIT License.
