using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;
using WindowsDesktopUse.Core;

namespace WindowsDesktopUse.Transcription;

/// <summary>
/// Service for transcribing audio using Whisper.net
/// </summary>
public class WhisperTranscriptionService : IDisposable
{
    private readonly string _modelDirectory;
    private WhisperFactory? _whisperFactory;
    private WhisperModelSize _loadedModelSize;
    private bool _disposed;

    public WhisperTranscriptionService(string? modelDirectory = null)
    {
        _modelDirectory = modelDirectory ?? Path.Combine(AppContext.BaseDirectory, "models");
        Directory.CreateDirectory(_modelDirectory);
    }

    /// <summary>
    /// Get model file path for the specified size
    /// </summary>
    public string GetModelPath(WhisperModelSize size)
    {
        var modelName = $"ggml-{size.ToString().ToLowerInvariant()}.bin";
        return Path.Combine(_modelDirectory, modelName);
    }

    /// <summary>
    /// Ensure model file exists, downloading if necessary
    /// </summary>
    public async Task EnsureModelExistsAsync(WhisperModelSize size, CancellationToken ct = default)
    {
        var modelPath = GetModelPath(size);

        if (File.Exists(modelPath))
        {
            Console.WriteLine($"[Whisper] Model already exists: {modelPath}");
            return;
        }

        Console.WriteLine($"[Whisper] Downloading model: {size}...");

        var ggmlType = size switch
        {
            WhisperModelSize.Tiny => GgmlType.Tiny,
            WhisperModelSize.Base => GgmlType.Base,
            WhisperModelSize.Small => GgmlType.Small,
            WhisperModelSize.Medium => GgmlType.Medium,
            WhisperModelSize.Large => GgmlType.LargeV3,
            _ => GgmlType.Base
        };

        try
        {
            using var modelStream = await WhisperGgmlDownloader.Default
                .GetGgmlModelAsync(ggmlType, QuantizationType.Q5_0, ct).ConfigureAwait(false);

            using var fileWriter = File.OpenWrite(modelPath);
            await modelStream.CopyToAsync(fileWriter, ct).ConfigureAwait(false);

            Console.WriteLine($"[Whisper] Model downloaded successfully: {modelPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Whisper] Failed to download model: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Load the specified model
    /// </summary>
    public async Task LoadModelAsync(WhisperModelSize size, CancellationToken ct = default)
    {
        if (_whisperFactory != null && _loadedModelSize == size)
        {
            return;
        }

        await EnsureModelExistsAsync(size, ct).ConfigureAwait(false);

        var modelPath = GetModelPath(size);
        _whisperFactory = WhisperFactory.FromPath(modelPath);
        _loadedModelSize = size;

        Console.WriteLine($"[Whisper] Model loaded: {size}");
    }

    /// <summary>
    /// Convert audio to Whisper-compatible format (16kHz, 16bit, mono PCM)
    /// </summary>
    private static string ConvertToWhisperFormat(string inputPath)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"whisper_converted_{Guid.NewGuid()}.wav");

        using var reader = new AudioFileReader(inputPath);

        var targetFormat = new WaveFormat(16000, 16, 1);
        using var resampler = new MediaFoundationResampler(reader, targetFormat);

        WaveFileWriter.CreateWaveFile(outputPath, resampler);

        return outputPath;
    }

    /// <summary>
    /// Transcribe audio file to text
    /// </summary>
    public async Task<TranscriptionResult> TranscribeFileAsync(
        string audioPath,
        string? language = null,
        WhisperModelSize modelSize = WhisperModelSize.Base,
        bool translateToEnglish = false,
        CancellationToken ct = default)
    {
        await LoadModelAsync(modelSize, ct).ConfigureAwait(false);

        if (_whisperFactory == null)
        {
            throw new InvalidOperationException("Whisper model not loaded");
        }

        var convertedPath = ConvertToWhisperFormat(audioPath);

        var segments = new List<TranscriptionSegment>();
        var detectedLanguage = language ?? "auto";

        var builder = _whisperFactory.CreateBuilder();

        if (!string.IsNullOrEmpty(language))
        {
            builder.WithLanguage(language);
        }

        if (translateToEnglish)
        {
            builder.WithTranslate();
        }

        using var processor = builder.Build();

        using var fileStream = File.OpenRead(convertedPath);

        try
        {
            await foreach (var result in processor.ProcessAsync(fileStream, ct))
            {
                segments.Add(new TranscriptionSegment(
                    result.Start,
                    result.End,
                    result.Text,
                    result.Probability,
                    detectedLanguage
                ));

                if (language == null && detectedLanguage == "auto")
                {
                    detectedLanguage = result.Language;
                }
            }
        }
        finally
        {
            try
            {
                File.Delete(convertedPath);
            }
            catch { }
        }

        var duration = segments.Count > 0
            ? segments.Last().End
            : TimeSpan.Zero;

        return new TranscriptionResult(
            Guid.NewGuid().ToString(),
            segments,
            detectedLanguage,
            duration,
            modelSize.ToString()
        );
    }

    /// <summary>
    /// Transcribe audio from memory stream
    /// </summary>
    public async Task<TranscriptionResult> TranscribeAsync(
        byte[] audioData,
        string? language = null,
        WhisperModelSize modelSize = WhisperModelSize.Base,
        bool translateToEnglish = false,
        CancellationToken ct = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"whisper_input_{Guid.NewGuid()}.wav");
        try
        {
            await File.WriteAllBytesAsync(tempPath, audioData, ct).ConfigureAwait(false);
            return await TranscribeFileAsync(tempPath, language, modelSize, translateToEnglish, ct).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch { }
        }
    }

    /// <summary>
    /// Get model information
    /// </summary>
    public static Dictionary<WhisperModelSize, ModelInfo> GetModelInfo()
    {
        return new Dictionary<WhisperModelSize, ModelInfo>
        {
            [WhisperModelSize.Tiny] = new("39 MB", "Fastest / Lowest accuracy", "Real-time streaming"),
            [WhisperModelSize.Base] = new("74 MB", "Fast / Medium accuracy", "Recommended for general use"),
            [WhisperModelSize.Small] = new("244 MB", "Medium / High accuracy", "Quality-focused transcription"),
            [WhisperModelSize.Medium] = new("769 MB", "Slow / Very high accuracy", "File processing"),
            [WhisperModelSize.Large] = new("1550 MB", "Slowest / Best accuracy", "Maximum accuracy needed")
        };
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _whisperFactory?.Dispose();
            }
            _disposed = true;
        }
    }
}
