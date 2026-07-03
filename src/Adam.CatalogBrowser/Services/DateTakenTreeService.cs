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
/// Loads the date-taken tree (grouped by year/month) from DB or broker.
/// </summary>
public sealed class DateTakenTreeService
{
    private readonly ModeManager _modeManager;
    private readonly ILogger<DateTakenTreeService> _logger;

    public DateTakenTreeService(ModeManager modeManager, ILogger<DateTakenTreeService> logger)
    {
        _modeManager = modeManager;
        _logger = logger;
    }

    public ObservableCollection<DateTakenNode> DateTakenTree { get; } = [];

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var root = new DateTakenNode { Name = "All Dates", IsExpanded = true };

        if (_modeManager.IsStandalone)
        {
            await using var db = await _modeManager.CreateDbContextAsync(ct).ConfigureAwait(false);
            var dateGroups = await db.MetadataProfiles
                .Where(mp => mp.DateTaken.HasValue)
                .GroupBy(mp => new { mp.DateTaken!.Value.Year, mp.DateTaken.Value.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync(ct).ConfigureAwait(false);

            var byYear = dateGroups
                .GroupBy(g => g.Year)
                .OrderByDescending(g => g.Key)
                .ToList();

            foreach (var yearGroup in byYear)
            {
                var yearNode = new DateTakenNode
                {
                    Name = yearGroup.Key.ToString(),
                    Year = yearGroup.Key,
                    AssetCount = yearGroup.Sum(g => g.Count),
                    IsExpanded = false
                };

                foreach (var monthGroup in yearGroup.OrderByDescending(g => g.Month))
                {
                    var monthNode = new DateTakenNode
                    {
                        Name = new DateTime(yearGroup.Key, monthGroup.Month, 1).ToString("MMMM"),
                        Year = yearGroup.Key,
                        Month = monthGroup.Month,
                        AssetCount = monthGroup.Count
                    };
                    yearNode.Children.Add(monthNode);
                }

                root.Children.Add(yearNode);
            }

            root.AssetCount = byYear.Sum(g => g.Sum(x => x.Count));
        }
        else if (_modeManager.BrokerClient != null)
        {
            var req = new Envelope
            {
                MessageType = MessageTypeCode.ListDateTakenTreeRequest,
                Payload = ByteString.CopyFrom(ProtoHelper.Serialize(new ListDateTakenTreeRequest()))
            };
            var resp = await _modeManager.BrokerClient.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.StatusCode == 0)
            {
                var data = ProtoHelper.Deserialize<ListDateTakenTreeResponse>(resp.Payload.ToByteArray());
                foreach (var yearInfo in data.Years)
                {
                    var yearNode = new DateTakenNode
                    {
                        Name = yearInfo.Year.ToString(),
                        Year = yearInfo.Year,
                        AssetCount = yearInfo.AssetCount,
                        IsExpanded = false
                    };

                    foreach (var monthInfo in yearInfo.Months)
                    {
                        var monthNode = new DateTakenNode
                        {
                            Name = monthInfo.MonthName,
                            Year = yearInfo.Year,
                            Month = monthInfo.Month,
                            AssetCount = monthInfo.AssetCount
                        };
                        yearNode.Children.Add(monthNode);
                    }

                    root.Children.Add(yearNode);
                }

                root.AssetCount = data.Years.Sum(y => y.AssetCount);
                _logger.LogInformation("[DateTakenTreeService] Loaded {Count} years from broker", data.Years.Count);
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DateTakenTree.Clear();
            DateTakenTree.Add(root);
        });
    }
}
