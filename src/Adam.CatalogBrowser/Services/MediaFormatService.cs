using System.Collections.ObjectModel;
using Adam.CatalogBrowser.Models.Sidebar;
using Adam.Shared.Contracts;
using Adam.Shared.Data;
using Adam.Shared.Services;
using Avalonia.Threading;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Loads media format counts (All, Images, Videos, Documents, Audio) from DB or broker.
/// </summary>
public sealed class MediaFormatService
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<MediaFormatService> _logger;

    public MediaFormatService(ModeManager modeManager, ILogger<MediaFormatService> logger)
    {
        _modeManager = modeManager;
        _logger = logger;
    }

    public ObservableCollection<CategoryNode> MediaFormats { get; } =
    [
        new() { Name = "All", Count = 0 },
        new() { Name = "Images", Count = 0 },
        new() { Name = "Videos", Count = 0 },
        new() { Name = "Documents", Count = 0 },
        new() { Name = "Audio", Count = 0 },
    ];

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            var counts = await db.DigitalAssets
                .GroupBy(a => a.Type)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Type, x => x.Count, ct).ConfigureAwait(false);

            var total = counts.Values.Sum();
            counts.TryGetValue(Adam.Shared.Models.AssetType.Image, out var images);
            counts.TryGetValue(Adam.Shared.Models.AssetType.Video, out var videos);
            counts.TryGetValue(Adam.Shared.Models.AssetType.Document, out var docs);
            counts.TryGetValue(Adam.Shared.Models.AssetType.Audio, out var audio);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                MediaFormats[0].Count = total;
                MediaFormats[1].Count = images;
                MediaFormats[2].Count = videos;
                MediaFormats[3].Count = docs;
                MediaFormats[4].Count = audio;
            });
        }
        else if (_modeManager.BrokerClient != null)
        {
            var req = new Envelope
            {
                MessageType = MessageTypeCode.ListMediaFormatCountsRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListMediaFormatCountsRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListMediaFormatCountsResponse>(resp.Payload.ToByteArray());
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MediaFormats[0].Count = data.TotalCount;
                    MediaFormats[1].Count = data.ImageCount;
                    MediaFormats[2].Count = data.VideoCount;
                    MediaFormats[3].Count = data.DocumentCount;
                    MediaFormats[4].Count = data.AudioCount;
                });
                _logger.LogInformation("[MediaFormatService] Loaded format counts from broker: Total={Total}", data.TotalCount);
            }
        }
    }
}
