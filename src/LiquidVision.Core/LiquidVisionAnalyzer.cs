using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiquidVision.Core.Configuration;
using LiquidVision.Core.DependencyInjection;
using LiquidVision.Core.Exceptions;
using LiquidVision.Core.Services;

namespace LiquidVision.Core;

/// <summary>
/// Native .NET image tagging analyzer backed by the LFM2-VL ONNX model. Downloads/verifies/loads the
/// model on first use, then produces a description, keywords, and categories for each image.
/// </summary>
public sealed class LiquidVisionAnalyzer : ILiquidVisionAnalyzer
{
    private readonly LiquidVisionOptions _options;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ModelCacheManager _cacheManager;
    private readonly ModelVerificationMarker _marker;
    private readonly ModelLoader _loader = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private LoadedModel? _model;
    private Lfm2VlGenerator? _generator;
    private ImagePreprocessor? _preprocessor;
    private PromptBuilder? _promptBuilder;
    private bool _isInitialized;
    private double _downloadProgress;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LiquidVisionAnalyzer() : this(new LiquidVisionOptions()) { }

    public LiquidVisionAnalyzer(LiquidVisionOptions options)
    {
        _options = options;
        _cacheManager = new ModelCacheManager(options);
        _marker = new ModelVerificationMarker(_cacheManager.Layout);
    }

    public LiquidVisionAnalyzer(LiquidVisionOptions options, IHttpClientFactory httpClientFactory)
        : this(options)
    {
        _httpClientFactory = httpClientFactory;
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        private set
        {
            if (Math.Abs(_downloadProgress - value) > 0.001)
            {
                _downloadProgress = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsInitialized
    {
        get => _isInitialized;
        private set
        {
            if (_isInitialized != value)
            {
                _isInitialized = value;
                OnPropertyChanged();
            }
        }
    }

    public async Task InitializeAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            if (IsInitialized) return;

            if (!await _marker.IsModelVerifiedAsync(_options.ModelVersion, ct))
                await PerformDownloadAsync(progress, ct);

            try
            {
                LoadModel();
            }
            catch (ModelLoadException)
            {
                // Corruption / partial cache: clear and retry once.
                await _marker.ClearAsync(ct);
                await _cacheManager.ClearCacheAsync(ct);
                await PerformDownloadAsync(progress, ct);
                LoadModel();
            }

            IsInitialized = true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task PerformDownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        _cacheManager.EnsureDirectoryExists();

        var internalProgress = new Progress<double>(p =>
        {
            DownloadProgress = p;
            progress?.Report(p);
        });

        var downloader = CreateDownloader();
        try
        {
            await downloader.DownloadModelAsync(_cacheManager.Layout, internalProgress, ct);
        }
        finally
        {
            downloader.Dispose();
        }

        await _marker.WriteVerificationMarkerAsync(_options.ModelVersion, ct);
    }

    private ModelDownloader CreateDownloader()
    {
        if (_httpClientFactory is not null)
            return new ModelDownloader(_httpClientFactory.CreateClient(ServiceCollectionExtensions.HttpClientName), _options);
        return new ModelDownloader(_options);
    }

    private void LoadModel()
    {
        _model = _loader.Load(_cacheManager.Layout, _options);
        _generator = new Lfm2VlGenerator(_model, _options);
        _preprocessor = new ImagePreprocessor(_model.PreprocessorConfig, _model.Tokenizer, _model.VisionEncoder);
        _promptBuilder = new PromptBuilder(_model.Tokenizer);
    }

    public async Task<ImageTagResult> AnalyzeAsync(string imagePath, CancellationToken ct = default)
    {
        var imageData = await File.ReadAllBytesAsync(imagePath, ct);
        return await AnalyzeAsync(imageData, ct);
    }

    public async Task<ImageTagResult> AnalyzeAsync(byte[] imageData, CancellationToken ct = default)
    {
        if (!IsInitialized || _model is null || _generator is null || _preprocessor is null || _promptBuilder is null)
            throw new InvalidOperationException("Analyzer is not initialized. Call InitializeAsync first.");

        await _semaphore.WaitAsync(ct);
        try
        {
            var sw = Stopwatch.StartNew();

            // Heavy CPU work off the async caller's thread.
            var (raw, parsed, ok) = await Task.Run(() =>
            {
                var image = _preprocessor.Process(imageData);
                var promptIds = _promptBuilder.Build(_options.SystemPrompt, _options.InstructionPrompt, image.ImageTokenIds);
                var text = _generator.Generate(promptIds, image, ct);
                bool parsedOk = TagResultParser.TryParse(text, out var p);
                return (text, p, parsedOk);
            }, ct);

            sw.Stop();

            if (!ok)
            {
                // Tolerant fallback: surface the raw text as the description.
                return new ImageTagResult(
                    Description: raw.Trim(),
                    Keywords: Array.Empty<string>(),
                    Categories: Array.Empty<string>(),
                    RawOutput: raw,
                    ProcessingTimeMs: sw.Elapsed.TotalMilliseconds,
                    ModelVersion: _options.ModelVersion);
            }

            return new ImageTagResult(
                Description: parsed.Description,
                Keywords: parsed.Keywords,
                Categories: parsed.Categories,
                RawOutput: raw,
                ProcessingTimeMs: sw.Elapsed.TotalMilliseconds,
                ModelVersion: _options.ModelVersion);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public async ValueTask DisposeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            _loader.Dispose();
        }
        finally
        {
            _semaphore.Release();
            _semaphore.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
