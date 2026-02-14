# ツール移行ガイド (v2.0)

## 概要
アーキテクチャ刷新により、ツールセットが統合されました。以下の移行表を参考に、新ツールをご利用ください。

## ツール移行表

### 視覚系ツール

| 旧ツール | 新ツール | 変更点 |
|----------|----------|--------|
| `list_monitors` | `visual_list` | `type="monitor"` を指定 |
| `list_windows` | `visual_list` | `type="window"` を指定 |
| `list_all` | `visual_list` | `type="all"` (デフォルト) |
| `capture` | `visual_capture` | `target` パラメータで対象指定 |
| `see` | `visual_capture` | 同じ機能、`target="primary"` がデフォルト |
| `capture_window` | `visual_capture` | `target="window"` + `hwnd` パラメータ |
| `capture_region` | `visual_capture` | `target="region"` + `x,y,w,h` パラメータ |
| `watch` | `visual_watch` | `mode` パラメータで動作指定 |
| `watch_video_v2` | `visual_watch` | `mode="video"` を指定 |
| `monitor` | `visual_watch` | `mode="monitor"` を指定 |
| `stop_watch_video` | `visual_stop` | 全 `stop_*` を統合 |
| `stop_monitor` | `visual_stop` | 全 `stop_*` を統合 |

### 操作系ツール

| 旧ツール | 新ツール | 変更点 |
|----------|----------|--------|
| `mouse_move` | `input_mouse` | `action="move"` を指定 |
| `mouse_click` | `input_mouse` | `action="click"` を指定 |
| `mouse_drag` | `input_mouse` | `action="drag"` を指定 |
| `close_window` | `input_window` | `action="close"` (デフォルト) |

## 使用例

### 視覚リストの取得
```json
// すべての対象を取得
{
  "tool": "visual_list",
  "params": {
    "type": "all"
  }
}

// ウィンドウのみを検索
{
  "tool": "visual_list", 
  "params": {
    "type": "window",
    "filter": "YouTube"
  }
}
```

### キャプチャ（動的クオリティ制御付き）
```json
// 通常画質 (quality=30)
{
  "tool": "visual_capture",
  "params": {
    "target": "primary",
    "mode": "normal"
  }
}

// 詳細画質 (quality=70) - 文字認識などに使用
{
  "tool": "visual_capture",
  "params": {
    "target": "window",
    "hwnd": 123456,
    "mode": "detailed"
  }
}
```

### 監視の開始と停止
```json
// ビデオ監視を開始
{
  "tool": "visual_watch",
  "params": {
    "mode": "video",
    "target": "window",
    "hwnd": 123456,
    "fps": 5
  }
}

// 特定のセッションを停止
{
  "tool": "visual_stop",
  "params": {
    "sessionId": "uuid-here"
  }
}

// すべての監視セッションを停止
{
  "tool": "visual_stop",
  "params": {
    "type": "all"
  }
}
```

### マウス操作
```json
// 移動
{
  "tool": "input_mouse",
  "params": {
    "action": "move",
    "x": 100,
    "y": 200
  }
}

// クリック
{
  "tool": "input_mouse",
  "params": {
    "action": "click",
    "x": 100,
    "y": 200,
    "button": "left",
    "clicks": 2
  }
}

// ドラッグ
{
  "tool": "input_mouse",
  "params": {
    "action": "drag",
    "x": 100,
    "y": 200,
    "endX": 300,
    "endY": 400
  }
}
```

### ウィンドウ操作
```json
// ウィンドウを閉じる
{
  "tool": "input_window",
  "params": {
    "hwnd": 123456,
    "action": "close"
  }
}

// ウィンドウを最小化
{
  "tool": "input_window",
  "params": {
    "hwnd": 123456,
    "action": "minimize"
  }
}
```

## 新機能

### 動的クオリティ制御
- **Normalモード**: `quality=30`（デフォルト）- トークン節約
- **Detailedモード**: `quality=70` - 高精度解析（文字認識など）

### トークン効率化プロトコル
画像データを含むレスポンスには `_llm_instruction` フィールドが含まれます：
```json
{
  "_llm_instruction": {
    "action": "PROCESS_IMMEDIATELY_AND_DISCARD",
    "steps": [...],
    "token_warning": "This image consumes approx 2000+ tokens..."
  }
}
```

LLMはこの指示に従い、画像を解析後すぐに破棄することで、トークン消費を95%節約します。

### SessionManagerによる統合管理
すべての非同期セッション（watch, capture, audio）が統合され、以下で一括管理できます：
- `visual_stop` - 特定セッションまたは全セッションの停止
- セッションタイプ別停止（`type: "watch"`, `"capture"`, `"audio"` など）
