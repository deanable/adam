using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.Shared.Services;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// ViewModel for the near-duplicate review view.
/// Manages navigation through duplicate groups and actions (keep primary, trash duplicates, skip).
/// </summary>
public sealed class DuplicateReviewViewModel : INotifyPropertyChanged
{
    private readonly NearDuplicateService _dupService;
    private readonly Adam.CatalogBrowser.Services.DeleteService? _deleteService;
    private IReadOnlyList<DuplicateGroup> _groups = [];
    private int _currentIndex;
    private bool _isScanning;
    private string _progressText = string.Empty;
    private DuplicateStats? _stats;

    public DuplicateReviewViewModel(
        NearDuplicateService dupService,
        Adam.CatalogBrowser.Services.DeleteService? deleteService = null)
    {
        _dupService = dupService;
        _deleteService = deleteService;

        NextGroupCommand = new RelayCommand(_ => NextGroup(), _ => CanNavigateNext);
        PreviousGroupCommand = new RelayCommand(_ => PreviousGroup(), _ => CanNavigatePrevious);
        KeepPrimaryCommand = new RelayCommand(async _ => await KeepPrimaryAsync(), _ => HasCurrentGroup);
        TrashAllDuplicatesCommand = new RelayCommand(async _ => await TrashAllDuplicatesAsync(), _ => HasCurrentGroup);
        SkipGroupCommand = new RelayCommand(_ => NextGroup(), _ => HasCurrentGroup);
        CloseCommand = new RelayCommand(_ => Close());
    }

    /// <summary>
    /// All duplicate groups found during scanning.
    /// </summary>
    public IReadOnlyList<DuplicateGroup> Groups
    {
        get => _groups;
        set { _groups = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalGroups)); OnPropertyChanged(nameof(HasGroups)); }
    }

    /// <summary>
    /// Current group index being viewed.
    /// </summary>
    public int CurrentGroupIndex
    {
        get => _currentIndex;
        set
        {
            _currentIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentGroup));
            OnPropertyChanged(nameof(GroupProgressText));
            OnPropertyChanged(nameof(CanNavigateNext));
            OnPropertyChanged(nameof(CanNavigatePrevious));
            OnPropertyChanged(nameof(HasCurrentGroup));
            (NextGroupCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PreviousGroupCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (KeepPrimaryCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (TrashAllDuplicatesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SkipGroupCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Current duplicate group.
    /// </summary>
    public DuplicateGroup? CurrentGroup => _currentIndex >= 0 && _currentIndex < _groups.Count ? _groups[_currentIndex] : null;

    /// <summary>
    /// Progress text for the current scan: "Group X of Y".
    /// </summary>
    public string GroupProgressText => _groups.Count > 0
        ? $"Group {_currentIndex + 1} of {_groups.Count}"
        : string.Empty;

    /// <summary>
    /// Total number of groups found.
    /// </summary>
    public int TotalGroups => _groups.Count;

    /// <summary>
    /// Whether any groups exist.
    /// </summary>
    public bool HasGroups => _groups.Count > 0;

    /// <summary>
    /// Whether there is a current group to view.
    /// </summary>
    public bool HasCurrentGroup => CurrentGroup != null;

    /// <summary>
    /// Whether a scan is in progress.
    /// </summary>
    public bool IsScanning
    {
        get => _isScanning;
        set { _isScanning = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Progress text shown during scanning.
    /// </summary>
    public string ProgressText
    {
        get => _progressText;
        set { _progressText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Statistics about the duplicate scan result.
    /// </summary>
    public DuplicateStats? Stats
    {
        get => _stats;
        set { _stats = value; OnPropertyChanged(); OnPropertyChanged(nameof(PotentialSavingsText)); }
    }

    /// <summary>
    /// Human-readable potential savings text.
    /// </summary>
    public string PotentialSavingsText => _stats != null
        ? $"Potential savings: {FormatBytes(_stats.PotentialSavingsBytes)} across {_stats.DuplicateGroups} groups"
        : string.Empty;

    public ICommand NextGroupCommand { get; }
    public ICommand PreviousGroupCommand { get; }
    public ICommand KeepPrimaryCommand { get; }
    public ICommand TrashAllDuplicatesCommand { get; }
    public ICommand SkipGroupCommand { get; }
    public ICommand CloseCommand { get; }

    public bool CanNavigateNext => _currentIndex < _groups.Count - 1;
    public bool CanNavigatePrevious => _currentIndex > 0;

    /// <summary>
    /// Scans the entire catalog for near-duplicates.
    /// </summary>
    public async Task ScanAllAsync(CancellationToken ct = default)
    {
        IsScanning = true;
        ProgressText = "Scanning for duplicates...";

        try
        {
            var progress = new Progress<(int completed, int total)>(p =>
            {
                ProgressText = $"Scanning... ({p.completed}/{p.total})";
            });

            var groups = await _dupService.ScanAllAsync(progress, ct);
            Groups = groups;
            CurrentGroupIndex = 0;

            var stats = await _dupService.GetStatsAsync(ct);
            Stats = stats;
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// Finds duplicates for a specific asset.
    /// </summary>
    public async Task FindForAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        IsScanning = true;
        ProgressText = "Finding duplicates...";

        try
        {
            var groups = await _dupService.FindForAssetAsync(assetId, ct);
            Groups = groups;
            CurrentGroupIndex = 0;
            Stats = new DuplicateStats
            {
                TotalAssets = groups.Sum(g => 1 + g.Duplicates.Count),
                AssetsWithDuplicates = groups.Sum(g => 1 + g.Duplicates.Count),
                DuplicateGroups = groups.Count,
                PotentialSavingsBytes = groups.Sum(g => g.Duplicates.Sum(d => d.FileSize))
            };
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void NextGroup()
    {
        if (CanNavigateNext)
            CurrentGroupIndex++;
    }

    private void PreviousGroup()
    {
        if (CanNavigatePrevious)
            CurrentGroupIndex--;
    }

    private async Task KeepPrimaryAsync()
    {
        var group = CurrentGroup;
        if (group == null || _deleteService == null) return;

        // Trash all duplicates but keep the primary
        var dupIds = group.Duplicates.Select(d => d.AssetId).ToList();
        if (dupIds.Count > 0)
        {
            await _deleteService.BulkSoftDeleteAsync(dupIds);
        }

        NextGroup();
    }

    private async Task TrashAllDuplicatesAsync()
    {
        var group = CurrentGroup;
        if (group == null || _deleteService == null) return;

        // Trash all including primary
        var allIds = new[] { group.Primary.AssetId }
            .Concat(group.Duplicates.Select(d => d.AssetId))
            .ToList();

        await _deleteService.BulkSoftDeleteAsync(allIds);

        NextGroup();
    }

    /// <summary>
    /// Fired when the user closes the duplicate review view.
    /// </summary>
    public event Action? CloseRequested;

    private void Close() => CloseRequested?.Invoke();

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
