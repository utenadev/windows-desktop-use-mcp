# プロジェクト進化・リファクタリング計画 (2026-02-07)

本プロジェクトを、単なるスクリーンキャプチャツールから、AI による Windows 操作を包括的に支援するエージェント基盤へと進化させるための計画書です。

## 1. プロジェクト再定義

### 新プロジェクト名
**`windows-desktop-use-mcp`**

### コンセプト
「AI に Windows の『目（視覚）』『耳（聴覚）』『手足（操作）』を与える MCP サーバー」

## 2. アーキテクチャ設計（DLL 分割）

利用シーンに応じた依存関係の最適化と再利用性の向上のため、以下のモジュール構成に分割します。

### プロジェクト一覧

| プロジェクト名 (DLL/EXE) | 分類 | 役割・内容 |
| :--- | :--- | :--- |
| **`WindowsDesktopUse.Core`** | DLL | インターフェース、共通データモデル、例外定義。他プロジェクトの基盤。 |
| **`WindowsDesktopUse.Screen`** | DLL | 画面・ウィンドウキャプチャ、ターゲット列挙。GDI+ / Modern API。 |
| **`WindowsDesktopUse.Audio`** | DLL | システム音・マイクの録音、WAV変換。 (依存: NAudio) |
| **`WindowsDesktopUse.Transcription`** | DLL | Whisper AI による文字起こし。 (依存: Whisper.net) |
| **`WindowsDesktopUse.Input`** | DLL | **【新規】** マウス操作、キーボード入力。 (Win32 SendInput API) |
| **`WindowsDesktopUse.App`** | EXE | MCP サーバーホスト。各 DLL を統合しツールとして公開。 |

## 3. 新機能開発：Input モジュール

AI がデスクトップを操作するための `WindowsDesktopUse.Input` を新規実装します。

### 予定されている MCP ツール
- `mouse_move(x, y)`: 指定座標へのカーソル移動。
- `mouse_click(button, count)`: 左/右/中クリック、ダブルクリック。
- `mouse_drag(start_x, start_y, end_x, end_y)`: ドラッグ＆ドロップ。
- `keyboard_type(text)`: 文字列の入力（IMEを介さない直接入力）。
- `keyboard_key(key, action)`: 特殊キー（Enter, Tab, Win, Ctrl+C 等）の操作。

## 4. 実施ステップ

### フェーズ 1: 名称変更と基盤整備
1.  ソリューション名および名前空間を `WindowsDesktopUse` に一括置換。
2.  `src/` 配下のディレクトリ構造を上記プロジェクト構成に合わせて再編。
3.  各プロジェクトの `.csproj` 作成と NuGet 依存関係の整理。

### フェーズ 2: モジュール分割の実行
1.  `Core` プロジェクトへの共通モデル（`MonitorInfo` 等）の移動。
2.  `Screen`, `Audio`, `Transcription` サービスのそれぞれの DLL への切り出し。
3.  `App` プロジェクト（旧メイン）をホスト専用にリファクタリング。

### フェーズ 3: Input モジュールの実装
1.  Win32 `SendInput` API を用いた操作ロジックの実装。
2.  スクリーンキャプチャ（座標系）との整合性確保。
3.  MCP ツールとしての公開 (`mouse_*`, `keyboard_*`)。

### フェーズ 4: 検証とドキュメント更新
1.  モジュールごとの動作確認。
2.  `docs/TOOLS.md` 等のドキュメントを新名称・新機能に合わせて更新。
3.  E2E テストの修正と拡張。

## 5. 注意事項・考慮点

- **座標系の整合性**: 高 DPI 環境における画面キャプチャの座標と、マウス操作の座標の不一致を防ぐための設計。
- **セキュリティ**: AI による自動操作のリスクについて、ドキュメントに明記する。
- **管理者権限**: 操作対象のアプリケーションによっては、本サーバーを管理者権限で実行する必要がある点。
