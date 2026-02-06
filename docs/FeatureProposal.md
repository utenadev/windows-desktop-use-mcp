# MCP Windows Screen Capture 機能追加提案書

## 概要

本提案では、以下の4つの主要機能追加を段階的に実装する計画を提示します：

1. **ツール統合（段階的移行）**: `list_all`/`capture`/`watch` ツール追加
2. **Windows Graphics Capture API 実装**: 現代のキャプチャAPIへの移行
3. **オーディオキャプチャ統合**: NAudioによる音声キャプチャ
4. **音声認識（聴く）**: Whisper.netによる文字起こし

---

## 1. ツール統合と段階的移行 (Proposal 3)

### 現状分析

現在のツール：
- `list_monitors` - モニター一覧
- `list_windows` - ウィンドウ一覧
- `see` - スクリーンショット取得
- `capture_window` - 特定ウィンドウキャプチャ
- `capture_region` - 領域キャプチャ
- `start_watching` / `stop_watching` - ストリーミング
- `get_latest_frame` - 最新フレーム取得

### 問題点

1. **命名の不一致**: `see` (動詞) vs `list_monitors` (動詞+名詞)
2. **機能の重複**: `see` と `capture_window` / `capture_region` で重複あり
3. **直感性の欠如**: `see` が何をするか直感的でない
4. **APIの断片化**: 関連機能が分散している

### 提案する新しいツール構成

```csharp
// Phase 1: 新ツール追加（後方互換性維持）
[McpServerTool, Description("List all available capture targets (monitors and windows) with unified interface")]
public static CaptureTargets ListAll(
    [Description("Filter: 'all', 'monitors', 'windows', 'regions' (saved regions)")] string filter = "all"
)

[McpServerTool, Description("Capture screen, window, or region as image or video")]
public static CaptureResult Capture(
    [Description("Target type: 'monitor', 'window', 'region', 'primary' (default monitor)")] string target,
    [Description("Target identifier: monitor index, hwnd, region name, or 'primary' (default)")] string? targetId = null,
    [Description("Output format: 'jpeg', 'png', 'mp4', 'gif' (animated)")] string format = "jpeg",
    [Description("For video: duration in seconds, 0=single capture")] int duration = 0,
    [Description("JPEG quality (1-100)")] int quality = 80,
    [Description("Maximum width in pixels")] int maxWidth = 1920
)

[McpServerTool, Description("Start watching/streaming a target (monitor, window, or region)")]
public static WatchSession Watch(
    [Description("Target type: 'monitor', 'window', 'region'")] string target,
    [Description("Target identifier")] string? targetId = null,
    [Description("Interval in milliseconds (minimum 100ms)")] int intervalMs = 1000,
    [Description("Output mode: 'sse' (Server-Sent Events), 'http', 'callback'")] string mode = "sse",
    [Description("Include audio capture")] bool includeAudio = false
)

// Phase 2: 非推奨化（既存ツールにObsolete属性追加）
[Obsolete("Use ListAll() instead. This method will be removed in v2.0 (2026-08).")]
public static List<MonitorInfo> ListMonitors()

[Obsolete("Use Capture(target='window') instead. This method will be removed in v2.0 (2026-08).")]
public static ImageContentBlock CaptureWindow(...)

// Phase 3: 削除（6-12ヶ月後）
```

### 移行タイムライン

| Phase | 期間 | 内容 |
|-------|------|------|
| Phase 1 | 0-2ヶ月 | 新ツール実装、既存ツールは維持 |
| Phase 2 | 2-6ヶ月 | 既存ツールに `[Obsolete]` 属性追加、警告ログ出力 |
| Phase 3 | 6-12ヶ月 | 既存ツール削除、新ツールのみ維持 |

### 互換性確保策

```csharp
// 移行ヘルパークラス
public static class CaptureMigrationHelper
{
    // 旧パラメータ→新パラメータ変換
    public static CaptureRequest ConvertLegacyRequest(
        string? targetType,
        uint? monitor,
        long? hwnd,
        int? x, int? y, int? w, int? h
    )
    
    // 古いセッションID→新セッションIDマッピング
    private static readonly ConcurrentDictionary<string, string> _sessionIdMap = new();
}
```

---

## 2. Windows Graphics Capture API 実装

### 現状の課題

現在の `PrintWindow` 方式の問題：
1. **最小化ウィンドウのキャプチャ不可**: `IsWindowVisible` で弾かれる
2. **GPUアプリの制限**: DirectX/OpenGLウィンドウが黒くなることがある
3. **性能**: CPU負荷が高い
4. **セキュリティ**: UACプロンプト等がキャプチャできない

### Windows.Graphics.Capture API の利点

| 項目 | PrintWindow | Windows.Graphics.Capture |
|------|-------------|-------------------------|
| 最小化ウィンドウ | 不可 | 可能（最新のWindows 11） |
| GPUアプリ | 部分的に問題あり | 完全対応 |
| 性能 | CPU処理 | GPUアクセラレーション |
| 黄色の枠線 | なし | あり（Windows 10）/ 削除可能（Windows 11） |
| 対応OS | Windows XP+ | Windows 10 1803+ |

### 実装計画

```csharp
// 新しいキャプチャサービス
public class ModernCaptureService : ICaptureService
{
    private readonly GraphicsCaptureHelper _captureHelper;
    
    public async Task<Bitmap> CaptureWindowAsync(IntPtr hwnd, CaptureOptions options)
    {
        // Windows.Graphics.Capture を使用
        var item = await GraphicsCaptureHelper.CreateItemForWindowAsync(hwnd);
        var framePool = Direct3D11CaptureFramePool.Create(
            _device, 
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size
        );
        
        using var session = framePool.CreateCaptureSession(item);
        
        // Windows 11では枠線を非表示に可能
        if (IsWindows11OrLater)
        {
            session.IsBorderRequired = false;
        }
        
        var frame = await framePool.TryGetNextFrameAsync();
        return ConvertD3DTextureToBitmap(frame.Surface);
    }
}

// フォールバックメカニズム
public class HybridCaptureService : ICaptureService
{
    private readonly ModernCaptureService _modern;
    private readonly LegacyCaptureService _legacy;
    
    public async Task<Bitmap> CaptureAsync(CaptureTarget target)
    {
        try
        {
            // まず新APIを試行
            return await _modern.CaptureAsync(target);
        }
        catch (NotSupportedException)
        {
            // フォールバック: 旧API
            return _legacy.Capture(target);
        }
    }
}
```

### 技術要件

```xml
<!-- Windows SDK 参照追加 -->
<PackageReference Include="Microsoft.Windows.SDK.NET.Ref" Version="10.0.26100.54" />

<!-- CsWin32 for P/Invoke -->
<PackageReference Include="Microsoft.Windows.CsWin32" Version="0.3.106">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

### 移行戦略

```csharp
public enum CaptureApiPreference
{
    Auto,       // 自動選択（推奨）
    Modern,     // Windows.Graphics.Capture のみ
    Legacy,     // PrintWindow のみ（互換性重視）
    Hybrid      // 両方試行
}

// 設定で切り替え可能に
public class CaptureOptions
{
    public CaptureApiPreference ApiPreference { get; set; } = CaptureApiPreference.Auto;
    public bool AllowFallback { get; set; } = true;
}
```

---

## 3. オーディオキャプチャ統合 (NAudio)

### 機能概要

システム音声・マイク入力をキャプチャし、動画キャプチャや音声認識に使用可能にします。

### 実装コンポーネント

```csharp
// オーディオキャプチャサービス
public class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _systemCapture;    // システム音声
    private WaveInEvent? _microphoneCapture;          // マイク
    
    // システム音声キャプチャ開始
    public void StartSystemAudioCapture(string outputPath, WaveFormat? format = null)
    {
        _systemCapture = new WasapiLoopbackCapture();
        _systemCapture.DataAvailable += (s, e) => 
        {
            _audioBuffer.Write(e.Buffer, 0, e.BytesRecorded);
        };
        _systemCapture.StartRecording();
    }
    
    // マイク音声キャプチャ開始
    public void StartMicrophoneCapture(int deviceNumber = 0)
    {
        _microphoneCapture = new WaveInEvent { DeviceNumber = deviceNumber };
        _microphoneCapture.DataAvailable += OnMicrophoneDataAvailable;
        _microphoneCapture.StartRecording();
    }
    
    // 音声ミキシング（システム音声 + マイク）
    public MixingSampleProvider CreateMixedAudio()
    {
        var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
        mixer.AddMixerInput(_systemCapture.ToSampleProvider());
        mixer.AddMixerInput(_microphoneCapture.ToSampleProvider());
        return mixer;
    }
}
```

### ツール統合

```csharp
[McpServerTool, Description("Start audio capture from system or microphone")]
public static AudioSession StartAudioCapture(
    [Description("Source: 'system', 'microphone', 'both'")] string source = "system",
    [Description("Duration in seconds (0=infinite)")] int duration = 0,
    [Description("Sample rate: 16000, 44100, 48000")] int sampleRate = 44100,
    [Description("Output format: 'wav', 'mp3', 'raw'")] string format = "wav",
    [Description("Microphone device index (when source='microphone' or 'both')")] int deviceIndex = 0
)

[McpServerTool, Description("Stop audio capture and return captured audio data")]
public static AudioContent StopAudioCapture(
    [Description("Session ID from start_audio_capture")] string sessionId,
    [Description("Return format: 'base64', 'file_path', 'stream_url'")] string returnFormat = "base64"
)
```

### ビデオキャプチャとの連携

```csharp
public class VideoCaptureSession
{
    public ScreenCaptureService Video { get; set; }
    public AudioCaptureService Audio { get; set; }
    
    public async Task<string> StartRecordingAsync(CaptureTarget target, bool includeAudio)
    {
        var videoSession = await Video.StartRecordingAsync(target);
        
        if (includeAudio)
        {
            // 同期された音声キャプチャ開始
            Audio.StartSystemAudioCapture($"temp_{videoSession.Id}_audio.wav");
        }
        
        return videoSession.Id;
    }
    
    public async Task<string> StopAndMergeAsync(string sessionId)
    {
        var videoPath = await Video.StopRecordingAsync(sessionId);
        var audioPath = Audio.StopCapture();
        
        // FFmpegで動画と音声をマージ
        return await FFmpegHelper.MergeAsync(videoPath, audioPath);
    }
}
```

### 依存関係

```xml
<PackageReference Include="NAudio" Version="2.2.1" />
<PackageReference Include="NAudio.Wasapi" Version="2.2.1" />
<PackageReference Include="FFmpeg.AutoGen" Version="6.1.0" /> <!-- 動画エンコード用 -->
```

---

## 4. 音声認識（聴く）- Whisper.net 統合

### 機能概要

オーディオキャプチャした音声をリアルタイムまたはファイルから文字起こしします。

### 実装計画

```csharp
// Whisper.net サービス
public class WhisperTranscriptionService
{
    private readonly WhisperFactory _whisperFactory;
    private readonly string _modelPath;
    
    public WhisperTranscriptionService(string modelPath = "ggml-base.bin")
    {
        _modelPath = modelPath;
        _whisperFactory = WhisperFactory.FromPath(modelPath);
    }
    
    // ファイルから文字起こし
    public async IAsyncEnumerable<TranscriptionSegment> TranscribeFileAsync(
        string audioPath, 
        string language = "auto",
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var processor = _whisperFactory.CreateBuilder()
            .WithLanguage(language)
            .Build();
        
        using var stream = File.OpenRead(audioPath);
        await foreach (var result in processor.ProcessAsync(stream, ct))
        {
            yield return new TranscriptionSegment(
                result.Start,
                result.End,
                result.Text
            );
        }
    }
    
    // リアルタイムストリーミング文字起こし
    public async IAsyncEnumerable<TranscriptionSegment> TranscribeStreamAsync(
        IAsyncEnumerable<byte[]> audioChunks,
        string language = "auto",
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var processor = _whisperFactory.CreateBuilder()
            .WithLanguage(language)
            .Build();
        
        await foreach (var chunk in audioChunks)
        {
            // 音声チャンクを処理
            var tempFile = Path.GetTempFileName() + ".wav";
            await File.WriteAllBytesAsync(tempFile, chunk, ct);
            
            await foreach (var result in processor.ProcessAsync(
                File.OpenRead(tempFile), ct))
            {
                yield return new TranscriptionSegment(
                    result.Start,
                    result.End,
                    result.Text
                );
            }
            
            File.Delete(tempFile);
        }
    }
}

// 文字起こし結果
public record TranscriptionSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text
);
```

### ツール統合

```csharp
[McpServerTool, Description("Transcribe audio to text using Whisper AI")]
public static async Task<TranscriptionResult> Listen(
    [Description("Audio source: 'microphone', 'system', 'file', 'stream_session'")] string source,
    [Description("For 'file': audio file path. For 'stream_session': session ID from start_audio_capture")] string? sourceId = null,
    [Description("Language code (auto=auto-detect, ja=Japanese, en=English, etc.)")] string language = "auto",
    [Description("Transcription mode: 'realtime', 'batch', 'file'")] string mode = "realtime",
    [Description("Maximum duration in seconds for real-time mode")] int maxDuration = 60,
    [Description("Model size: 'tiny', 'base', 'small', 'medium', 'large'")] string modelSize = "base",
    [Description("Include word-level timestamps")] bool wordTimestamps = false,
    [Description("Translate to English")] bool translate = false
)
```

### 使用例

```csharp
// マイク入力のリアルタイム文字起こし
var result = await tools.Listen(
    source: "microphone",
    mode: "realtime",
    language: "ja",
    maxDuration: 30
);
// Returns: {"segments": [{"start": "0:00", "end": "0:05", "text": "こんにちは"}, ...]}

// システム音声のキャプチャと文字起こし
var audioSession = await tools.StartAudioCapture(source: "system");
await Task.Delay(10000); // 10秒録音
await tools.StopAudioCapture(audioSession.Id);

var transcription = await tools.Listen(
    source: "stream_session",
    sourceId: audioSession.Id,
    mode: "batch",
    language: "en"
);

// ファイルからの文字起こし
var text = await tools.Listen(
    source: "file",
    sourceId: "C:\\recordings\\meeting.wav",
    mode: "batch",
    language: "auto",
    wordTimestamps: true
);
```

### モデル管理

```csharp
public class WhisperModelManager
{
    // モデル自動ダウンロード
    public static async Task EnsureModelExistsAsync(GgmlType type)
    {
        var modelName = $"ggml-{type.ToString().ToLower()}.bin";
        if (File.Exists(modelName)) return;
        
        using var modelStream = await WhisperGgmlDownloader.Default
            .GetGgmlModelAsync(type);
        using var fileWriter = File.OpenWrite(modelName);
        await modelStream.CopyToAsync(fileWriter);
    }
    
    // モデルサイズと性能のトレードオフ
    public static readonly Dictionary<GgmlType, ModelInfo> ModelSpecs = new()
    {
        [GgmlType.Tiny] = new("39 MB", "最速/精度低", "リアルタイム向け"),
        [GgmlType.Base] = new("74 MB", "速い/中精度", "推奨"),
        [GgmlType.Small] = new("244 MB", "中速/高精度", "品質重視"),
        [GgmlType.Medium] = new("769 MB", "遅い/高品質", "ファイル処理向け"),
        [GgmlType.Large] = new("1550 MB", "最遅/最高品質", "高精度が必要な場合")
    };
}
```

### 依存関係

```xml
<PackageReference Include="Whisper.net" Version="1.9.0" />
<PackageReference Include="Whisper.net.Runtime" Version="1.9.0" />
<!-- GPU加速が必要な場合 -->
<!-- <PackageReference Include="Whisper.net.Runtime.Cuda" Version="1.9.0" /> -->
```

---

## 実装フェーズ

### Phase 1: 基盤構築（2-3週間）

1. **依存関係追加**
   - NAudio, Whisper.net, Windows SDK
   
2. **新ツール実装（非推奨化なし）**
   - `list_all`
   - `capture`
   - `watch`
   
3. **テスト整備**
   - 新ツールの統合テスト
   - 旧ツールとの互換性テスト

### Phase 2: Windows Graphics Capture 移行（3-4週間）

1. **ModernCaptureService 実装**
2. **フォールバック機構実装**
3. **A/Bテスト**: 新旧APIの品質比較
4. **設定オプション追加**

### Phase 3: オーディオ統合（2-3週間）

1. **AudioCaptureService 実装**
2. **動画同期キャプチャ**
3. **ツール統合**: `start_audio_capture` / `stop_audio_capture`

### Phase 4: 音声認識（2-3週間）

1. **Whisper.net 統合**
2. **リアルタイム処理パイプライン**
3. **モデル自動ダウンロード**
4. **`listen` ツール実装**

### Phase 5: 段階的移行（並行）

1. **Month 2-6**: 旧ツールに `[Obsolete]` 属性追加
2. **Month 6-12**: 旧ツール削除
3. **ドキュメント更新**

---

## 技術スタックまとめ

```xml
<!-- 追加される NuGet パッケージ -->
<PackageReference Include="NAudio" Version="2.2.1" />
<PackageReference Include="Whisper.net" Version="1.9.0" />
<PackageReference Include="Whisper.net.Runtime" Version="1.9.0" />
<PackageReference Include="Microsoft.Windows.SDK.NET.Ref" Version="10.0.26100.54" />
<PackageReference Include="FFmpeg.AutoGen" Version="6.1.0" />
```

---

## リスクと対策

| リスク | 影響 | 対策 |
|--------|------|------|
| Windows.Graphics.Capture が古いOSで動作しない | 高 | フォールバック機構で旧API使用 |
| Whisper.net のモデルサイズが大きい | 中 | デフォルトはBaseモデル、Tinyモデル選択可能に |
| NAudio で音声デバイス取得失敗 | 中 | デバイス列挙失敗時は機能無効化 |
| 後方互換性破壊 | 高 | 6ヶ月の移行期間、段階的な非推奨化 |
| 性能劣化 | 中 | A/Bテスト、ベンチマーク自動化 |

---

## 結論

この提案により：

1. **統一された直感的なAPI**: `list_all`/`capture`/`watch` で全機能にアクセス
2. **現代的なキャプチャ**: Windows Graphics Capture API で品質向上
3. **マルチメディア対応**: 動画・音声キャプチャが可能に
4. **AI統合**: Whisper.net で音声認識が可能に

段階的な移行により、既存ユーザーへの影響を最小限に抑えながら、新機能を追加できます。

---

## 次のアクション

1. **本提案の承認**
2. **Phase 1の詳細設計**
3. **開発ブランチ作成**
4. **最初のPR作成** (Phase 1: 新ツール実装)

ご質問・ご意見があればお知らせください。
