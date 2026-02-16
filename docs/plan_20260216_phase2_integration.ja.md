# 実装指示書: Vision 最適化 Phase 2 (統合とリリース準備)

## 📌 コンテキスト
qwencode による Phase 1（オーバーレイ描画、FrameContext）が main ブランチにマージされました。Phase 2 では、これらを MCP ツールおよび CLI オプションとして統合し、ユーザー（LLM）が利用可能にします。

## 1. 実装タスク

### 1.1 統合ツール `visual_watch` の拡張 (DesktopUseTools.cs)
- **引数の追加**: `visual_watch` ツールに以下の引数を追加してください。
    - `overlay`: bool (default: false) - タイムスタンプ等を画像に焼き込むか。
    - `context`: bool (default: false) - 文脈付きプロンプト（FrameContext）を通知に含めるか。
- **SessionManager 連携**:
    - `RegisterSession` 時に、上記のフラグを `UnifiedSession` オブジェクトに保持させてください。
- **キャプチャループへの適用**:
    - ループ内で `EnableOverlay` フラグを参照し、`ImageOverlayService` のオーバーレイロジックを呼び出すように統合を完了させてください。

### 1.2 CLI オプションの実装 (Program.cs)
- サーバー起動時の引数として `--default-overlay` や `--enable-context` を受け取れるようにし、デフォルト設定として保持してください。

### 1.3 通知ペイロードの強化
- `context: true` の場合、MCP 通知の `data` オブジェクトに `FrameContext.GenerateContextualPrompt()` で生成したテキスト（`prompt` フィールド等）を含めてください。

### 1.4 ドキュメントの最終更新
- **CHANGELOG.md**: `[0.9.0]` セクションに以下を追記してください。
    - 「視覚最適化 (Vision Optimization) の導入: タイムスタンプ・イベントタグのオーバーレイ描画」
    - 「FrameContext エンジンの導入: 時系列プロンプト自動生成による LLM の文脈理解力向上」
- **MIGRATION_GUIDE_v2.md**: 必要に応じて最新の引数情報を追記してください。

## 2. 受入基準
1. Claude Desktop 等から `visual_watch(overlay=true)` を実行し、画像にタイムコードが乗っていることを確認。
2. 通知データの中に、前回の状況を踏まえた「問いかけテキスト（プロンプト）」が含まれていることを確認。
3. `dotnet test` で E2E テストがパスすることを確認。

---
詳細は、マージ済みの `docs/PHASE2_INTEGRATION_GUIDE.md` を参照してください。
