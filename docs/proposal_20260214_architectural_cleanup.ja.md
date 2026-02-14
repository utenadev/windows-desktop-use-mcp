# アーキテクチャ刷新および MCP ツールセット統合案 (更新版)

## 1. 目的
ツールセットの断片化を解消し、LLM が迷いなく、かつ最小のトークン消費でデスクトップを操作・観測できるクリーンな基盤を構築する。また、`qwencode` のテスト結果に基づき、画質（Quality）の動的制御を組み込む。

---

## 2. 刷新後のアーキテクチャ方針

### 2.1 統合インターフェースの「動詞」化
1.  **`visual_list`**: ターゲット（モニター、ウィンドウ、領域）を探す。
2.  **`visual_capture`**: 静止画を取得する。
3.  **`visual_watch`**: 継続的な監視・同期ストリームを開始する。

### 2.2 動的クオリティ制御 (Dynamic Quality Control)
`qwencode` の調査結果（quality=30 が実用臨界点）に基づき、以下の挙動を標準化する。
- **Watchモード (Normal)**: デフォルト `quality=30`。トークンを節約しつつ場面転換を追跡。
- **Inspectモード (Detailed)**: `ReadResource` 時や高精度指定時に `quality=70` へ自動昇格。顔の表情や文字の認識をサポート。

---

## 3. 具体的な統合マッピング（opencode への指示用）

### 3.1 視覚系 (Visual)
| 新ツール名 | 旧ツール群 | 主な改善点 |
|------------|------------|------------|
| `visual_list` | `list_all`, `list_monitors`, `list_windows` | 検索対象を `type` 引数で集約。 |
| `visual_capture` | `capture`, `see`, `capture_window`, `capture_region` | **Dynamic Quality**: デフォルト30だが、詳細解析指示で自動的に高画質化。 |
| `visual_watch` | `watch`, `watch_video_v2`, `monitor` | `mode` ("video", "monitor", "unified") で挙動を統一。 |
| `visual_stop` | 全ての `stop_...` | 全ての非同期タスクを一括管理・停止。 |

### 3.2 操作系 (Input)
| 新ツール名 | 旧ツール群 | 備考 |
|------------|------|------|
| `input_mouse` | `mouse_move`, `mouse_click`, `mouse_drag` | `action` 引数でマウス操作を完結。 |
| `input_keyboard`| `keyboard_key` | 既存のセキュリティ制限を維持。 |
| `input_window` | `close_window` | 将来的に最小化・最大化なども追加。 |

---

## 4. opencode への実装ステップ提案

1.  **Phase 1: 統合ツールの実装と `SessionManager` の導入**
    - `DesktopUseTools.cs` の肥大化を防ぐため、セッション管理ロジックを分離。
2.  **Phase 2: 動的クオリティ・ロジックの組み込み**
    - `visual_watch` のデフォルト画質を 30 に設定。
    - `visual_capture` において、`detailed: true` 引数によって画質を 70 に引き上げるスイッチを実装。
3.  **Phase 3: リソースバッファリングとの連携**
    - `plan_20260214_mcp_resources_buffering.ja.md` に基づき、過去のフレームを高画質で参照できる仕組みを統合。

---

## 5. 期待される効果
- **トークン効率の最大化**: 普段は `quality=30` の軽量データで運用し、必要な時だけ `quality=70` の詳細データを取り出す「賢い」運用が可能になる。
- **LLM の精度向上**: 画質による情報喪失の特性を考慮したツール設計により、LLM の誤認を最小限に抑える。
