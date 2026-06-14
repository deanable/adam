using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Data;
using Adam.Shared.Services;

namespace Adam.CatalogBrowser.ViewModels;

public class ImportViewModel : INotifyPropertyChanged
{
    private readonly ModeManager _modeManager;
    private readonly CsvMetadataService _csvService;
    private string? _selectedFilePath;
    private bool _isImporting;
    private bool _isPreviewMode = true;
    private double _progressValue;
    private string _progressText = string.Empty;
    private int _selectedConflictIndex;
    private List<CsvMetadataRow>? _parsedRows;
    private int _matchedCount;
    private bool _hasPreview;

    public ImportViewModel(ModeManager modeManager)
    {
        _modeManager = modeManager;
        _csvService = new CsvMetadataService();

        BrowseFileCommand = new RelayCommand(async _ => await BrowseFileAsync());
        PreviewCommand = new RelayCommand(async _ => await LoadPreviewAsync(), _ => CanPreview);
        ImportCommand = new RelayCommand(async _ => await ImportAsync(), _ => CanImport);
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke());
    }

    public string? SelectedFilePath
    {
        get => _selectedFilePath;
        set
        {
            _selectedFilePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanPreview));
            OnPropertyChanged(nameof(CanImport));
        }
    }

    public bool IsImporting
    {
        get => _isImporting;
        set { _isImporting = value; OnPropertyChanged(); }
    }

    public bool IsPreviewMode
    {
        get => _isPreviewMode;
        set
        {
            _isPreviewMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowPreview));
            OnPropertyChanged(nameof(ShowImport));
        }
    }

    public bool ShowPreview => IsPreviewMode;
    public bool ShowImport => !IsPreviewMode;

    public double ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    public string ProgressText
    {
        get => _progressText;
        set { _progressText = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 0 = Overwrite, 1 = SkipIfEmpty, 2 = AppendKeywords
    /// </summary>
    public int SelectedConflictIndex
    {
        get => _selectedConflictIndex;
        set { _selectedConflictIndex = value; OnPropertyChanged(); }
    }

    public ConflictMode SelectedConflictMode => SelectedConflictIndex switch
    {
        1 => ConflictMode.SkipIfEmpty,
        2 => ConflictMode.AppendKeywords,
        _ => ConflictMode.Overwrite
    };

    public bool HasPreview
    {
        get => _hasPreview;
        set { _hasPreview = value; OnPropertyChanged(); }
    }

    public int MatchedCount
    {
        get => _matchedCount;
        set { _matchedCount = value; OnPropertyChanged(); }
    }

    /// <summary>Preview lines (change descriptions).</summary>
    public ObservableCollection<string> PreviewLines { get; } = [];

    /// <summary>Total rows in parsed CSV.</summary>
    public int TotalRows { get; set; }

    public ICommand BrowseFileCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>Fired when the import completes successfully.</summary>
    public event Action? ImportCompleted;
    /// <summary>Fired when the user cancels.</summary>
    public event Action? RequestClose;

    public Func<Task<string?>>? BrowseFileFunc { get; set; }

    public bool CanPreview => !string.IsNullOrWhiteSpace(SelectedFilePath) && File.Exists(SelectedFilePath) && !IsImporting;

    public bool CanImport => _parsedRows != null && _parsedRows.Count > 0 && !IsImporting;

    private async Task BrowseFileAsync()
    {
        if (BrowseFileFunc == null) return;
        var path = await BrowseFileFunc();
        if (!string.IsNullOrEmpty(path))
        {
            SelectedFilePath = path;
            await LoadPreviewAsync();
        }
    }

    public async Task LoadPreviewAsync()
    {
        if (!CanPreview) return;

        try
        {
            IsImporting = true;
            ProgressText = "Parsing CSV...";

            _parsedRows = await _csvService.ReadCsvAsync(SelectedFilePath!);
            TotalRows = _parsedRows.Count;

            await using var db = await _modeManager.CreateDbContextAsync();
            var preview = await _csvService.PreviewImportAsync(_parsedRows, db);

            PreviewLines.Clear();
            foreach (var line in preview)
                PreviewLines.Add(line);

            MatchedCount = preview.Count(l => l.StartsWith("✓"));
            HasPreview = true;
            IsPreviewMode = true;

            ProgressText = $"Parsed {TotalRows} rows, {MatchedCount} matched in database";
        }
        catch (Exception ex)
        {
            ProgressText = $"Failed to parse CSV: {ex.Message}";
            PreviewLines.Clear();
            PreviewLines.Add($"Error: {ex.Message}");
        }
        finally
        {
            IsImporting = false;
        }
    }

    private async Task ImportAsync()
    {
        if (!CanImport) return;

        IsImporting = true;
        IsPreviewMode = false;
        ProgressValue = 0;
        ProgressText = "Importing...";

        var progress = new Progress<(int processed, int total)>(p =>
        {
            ProgressValue = p.total > 0 ? (double)p.processed / p.total * 100 : 0;
            ProgressText = $"Imported {p.processed} of {p.total}";
        });

        try
        {
            await using var db = await _modeManager.CreateDbContextAsync().ConfigureAwait(false);
            var updated = await _csvService.ImportFromCsvAsync(_parsedRows!, db, SelectedConflictMode, progress);

            ProgressText = $"Import complete — {updated} asset(s) updated";
            ImportCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Import cancelled";
        }
        catch (Exception ex)
        {
            ProgressText = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
