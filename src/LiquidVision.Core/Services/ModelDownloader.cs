using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using LiquidVision.Core.Configuration;
using LiquidVision.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace LiquidVision.Core.Services;

/// <summary>
/// Downloads the set of files described by a <see cref="Lfm2VlModelLayout"/> with resumable
/// downloads, retry-with-backoff, weighted progress, and tolerance for optional (404) files.
/// </summary>
public sealed class ModelDownloader : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly LiquidVisionOptions _options;
    private readonly ILogger? _logger;

    /// <summary>Creates a downloader that owns its own <see cref="HttpClient"/>.</summary>
    public ModelDownloader(LiquidVisionOptions? options = null, ILogger? logger = null)
    {
        _options = options ?? new LiquidVisionOptions();
        _httpClient = new HttpClient { Timeout = _options.DownloadTimeout };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LiquidVision/1.0");
        _logger = logger;
        _ownsClient = true;
    }

    /// <summary>Creates a downloader using an injected <see cref="HttpClient"/> (e.g. from IHttpClientFactory).</summary>
    public ModelDownloader(HttpClient httpClient, LiquidVisionOptions options, ILogger? logger = null)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _ownsClient = false;
    }

    /// <summary>Downloads every file in the layout that is missing or incomplete.</summary>
    public async Task DownloadModelAsync(Lfm2VlModelLayout layout, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var files = layout.RemoteFiles;

        // Determine total bytes for weighted progress (best-effort; falls back to per-file weighting).
        var sizes = new long[files.Count];
        long totalBytes = 0;
        bool haveSizes = true;
        for (int i = 0; i < files.Count; i++)
        {
            var size = await TryGetContentLengthAsync(layout.BaseUrl + files[i].RemotePath, ct);
            if (size is null)
            {
                if (!files[i].Optional) haveSizes = false;
                sizes[i] = 0;
            }
            else
            {
                sizes[i] = size.Value;
                totalBytes += size.Value;
            }
        }

        long completedBytes = 0;
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var idx = i;
            Directory.CreateDirectory(Path.GetDirectoryName(file.LocalPath)!);

            // Skip if already fully present and matches known size.
            if (File.Exists(file.LocalPath) && (sizes[idx] == 0 || new FileInfo(file.LocalPath).Length == sizes[idx]))
            {
                completedBytes += sizes[idx];
                ReportOverall(progress, haveSizes, totalBytes, completedBytes, i + 1, files.Count);
                continue;
            }

            long fileBaseline = completedBytes;
            var fileProgress = new Progress<long>(read =>
            {
                if (haveSizes && totalBytes > 0)
                    progress?.Report(Math.Min(1.0, (fileBaseline + read) / (double)totalBytes));
            });

            bool downloaded = await DownloadFileWithRetryAsync(
                layout.BaseUrl + file.RemotePath, file.LocalPath, file.Optional, fileProgress, ct);

            if (!downloaded && !file.Optional)
                throw new ModelDownloadException($"Required file could not be downloaded: {file.RemotePath}");

            completedBytes += sizes[idx];
            ReportOverall(progress, haveSizes, totalBytes, completedBytes, i + 1, files.Count);
        }

        progress?.Report(1.0);
    }

    private static void ReportOverall(IProgress<double>? progress, bool haveSizes, long total, long completed, int doneCount, int fileCount)
    {
        if (haveSizes && total > 0)
            progress?.Report(Math.Min(1.0, completed / (double)total));
        else
            progress?.Report((double)doneCount / fileCount);
    }

    private async Task<long?> TryGetContentLengthAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.IsSuccessStatusCode)
                return resp.Content.Headers.ContentLength;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "HEAD request failed for {Url} — proceeding without content-length", url);
        }
        return null;
    }

    private async Task<bool> DownloadFileWithRetryAsync(
        string url, string destinationPath, bool optional, IProgress<long>? progress, CancellationToken ct)
    {
        var partPath = destinationPath + ".part";
        Exception? lastError = null;

        for (int attempt = 0; attempt <= _options.MaxDownloadRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // 1s, 2s, 4s, 8s...
                await Task.Delay(delay, ct);
            }

            try
            {
                long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (existing > 0)
                    req.Headers.Range = new RangeHeaderValue(existing, null);

                using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                if (resp.StatusCode == HttpStatusCode.NotFound && optional)
                    return false;

                // If the server ignored our Range (200 instead of 206), restart from scratch.
                bool append = existing > 0 && resp.StatusCode == HttpStatusCode.PartialContent;
                if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    // .part is already complete or stale; discard and retry fresh.
                    File.Delete(partPath);
                    existing = 0;
                    append = false;
                    continue;
                }

                if (!resp.IsSuccessStatusCode)
                    throw new ModelDownloadException($"Failed to download {url}", resp.StatusCode);

                await using (var http = await resp.Content.ReadAsStreamAsync(ct))
                await using (var fs = new FileStream(partPath, append ? FileMode.Append : FileMode.Create,
                                 FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    var buffer = new byte[1 << 16];
                    long readTotal = append ? existing : 0;
                    int read;
                    while ((read = await http.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) != 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                        readTotal += read;
                        progress?.Report(readTotal);
                    }
                }

                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);
                File.Move(partPath, destinationPath);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or ModelDownloadException or TaskCanceledException)
            {
                _logger?.LogWarning(ex, "Download attempt {Attempt}/{MaxRetries} failed for {Url}",
                    attempt + 1, _options.MaxDownloadRetries + 1, url);
                lastError = ex; // transient – retry (resuming from .part)
            }
        }

        if (optional)
            return false;

        _logger?.LogError("Download exhausted {Attempts} retries for {Url}: {Error}",
            _options.MaxDownloadRetries + 1, url, lastError?.Message);

        throw new ModelDownloadException(
            $"Network error downloading {url} after {_options.MaxDownloadRetries + 1} attempts: {lastError?.Message}",
            lastError ?? new Exception("unknown"));
    }

    public void Dispose()
    {
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
