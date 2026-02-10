# 統合テスト手順書

## 1. サーバー手動起動

PowerShellを**管理者権限**で開き、以下を実行：

```powershell
cd C:\workspace\mcp-windows-screen-capture\t\Artifacts\build_4
.\WindowsScreenCaptureServer.exe --ip_addr 127.0.0.1 --port 5001 --desktopNum 0
```

別のPowerShellウィンドウでテスト：

```powershell
# SSEエンドポイントテスト
curl http://127.0.0.1:5001/sse

# またはブラウザで開く
start http://127.0.0.1:5001/sse
```

## 2. Claude Desktop設定

`%USERPROFILE%\.claude\config.json`：

```json
{
  "mcpServers": {
    "windows-capture-test": {
      "command": "curl",
      "args": ["-N", "http://127.0.0.1:5001/sse"]
    }
  }
}
```

## 3. ファイアウォール設定（初回のみ）

管理者PowerShell：
```powershell
netsh advfirewall firewall add rule name="MCP Test" dir=in action=allow protocol=TCP localport=5001
```

## 4. 動作確認

Claude Desktopで以下を試行：
- 「画面をキャプチャして」
- 「モニターの一覧を表示して」

---

**注意**: 実際のMCPツール呼び出しにはSSE接続とメッセージエンドポイントの
適切なハンドシェイクが必要です。Claude Desktop経由でテストしてください。
