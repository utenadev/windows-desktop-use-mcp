# Tool Unification Guide

## 目的
モニター一覧とウィンドウ一覧を統合し、使いやすくします。

## 変更案

### 案1: 単一化 - `list_all`
```json
{"method": "tools/call", "params": {"name": "list_all"}}
```
- `list_monitors` と `list_windows` の統合
- 呼い使い分け

### 案2: 段存 - `see` と `capture` で分離
```json
{"method": "tools/call", "params": {"name": "see", "targetType": "monitor|window"}}
{"method": "tools/call", "params": {"name": "capture", "targetType": "monitor|window"}}
```

### 案3: 完全な後方互換 - 将来のリファクタリング時
- 今のまま維持（`list_monitors`/`list_windows` 独立）
- 将来は `list_all` に統合し、`targetType` 引数で柔軟に対応

## 推奨
**案1**が最シンプル：`list_all` + `targetType` 拡張（既存ツールを壊さず）

**案2**が将来のため最柔軟：`list_all` に統合（将来的には `list_monitors` と `list_windows` を非推奨）

---

**どれを進みますか？**
