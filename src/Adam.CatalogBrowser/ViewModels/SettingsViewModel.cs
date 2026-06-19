using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Services;
using Avalonia;
using Avalonia.Styling;
using Microsoft.Extensions.Logging;
using RelayCommand = Adam.Shared.Services.RelayCommand;

namespace Adam.CatalogBrowser.ViewModels;

/// <summary>
/// ViewModel for the Settings tab. Each category exposes preference-backed
/// properties that save on change. Startup hydration is handled in App.axaml.cs.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IUserPreferenceService _prefs;
    private readonly AdamConfig _config;
    private readonly ModeManager _modeManager;
    private readonly ILogger<SettingsViewModel> _logger;
    private SettingCategory? _selectedCategory;
    private string _searchText = string.Empty;

    // ── Theme / Appearance ───────────────────────────────────

    private int _selectedThemeIndex;
    private int _selectedAccentIndex;
    private int _selectedDensityIndex;
    private double _fontScale;

    // ── Ingestion ────────────────────────────────────────────

    private bool _aiAutoTagEnabled;

    // ── Gallery / Search ─────────────────────────────────────

    private int _defaultSortIndex;
    private int _resultsPerPage;
    private int _thumbnailSize;

    // ── Metadata ─────────────────────────────────────────────

    private bool _panelsOpenByDefault;

    public SettingsViewModel(
        IUserPreferenceService prefs,
        AdamConfig config,
        ModeManager modeManager,
        ILogger<SettingsViewModel> logger)
    {
        _prefs = prefs;
        _config = config;
        _modeManager = modeManager;
        _logger = logger;

        ThemeOptions = ["Light", "Dark", "System"];
        AccentOptions = ["Teal", "Amber", "Rose", "Violet", "Emerald"];
        DensityOptions = ["Compact", "Normal", "Comfortable"];
        DefaultSortOptions = ["File name", "Date added", "File type", "File size"];
        ResultsPerPageOptions = [24, 48, 96, 192, 500];
        ThumbnailSizeOptions = [100, 150, 200, 250, 300];

        ScriptingOptions = ["None", "JavaScript", "Python (embedded)", "Lua"];

        SelectCategoryCommand = new RelayCommand(param =>
        {
            if (param is SettingCategory category)
                SelectedCategory = category;
        });

        ResetAppearanceCommand = new RelayCommand(async _ => await ResetCategoryAsync("appearance"));
        ResetConnectionCommand = new RelayCommand(async _ => await ResetCategoryAsync("connection"));
        ResetIngestionCommand = new RelayCommand(async _ => await ResetCategoryAsync("ingestion"));
        ResetMetadataCommand = new RelayCommand(async _ => await ResetCategoryAsync("metadata"));
        ResetSearchCommand = new RelayCommand(async _ => await ResetCategoryAsync("search"));
        ResetAiCommand = new RelayCommand(async _ => await ResetCategoryAsync("ai"));

        ApplyThemeCommand = new RelayCommand(async _ => await ApplyThemePreferenceAsync());

        // Build categories
        Categories =
        [
            new SettingCategory("General / Appearance", "Theme, accent, density, font scale, locale, defaults", "appearance"),
            new SettingCategory("Catalog & Storage", "DB location, cache config, backup, integrity", "catalog"),
            new SettingCategory("Database & Connection", "Mode toggle, broker host/port/TLS, test connection", "connection"),
            new SettingCategory("Ingestion", "Watched folders, import presets, rename templates, AI auto-tag", "ingestion"),
            new SettingCategory("Metadata & Schemas", "Panel order, presets, writeback policy, controlled vocabularies", "metadata"),
            new SettingCategory("AI / LiquidVision", "Model selection, execution provider, precision, thresholds", "ai"),
            new SettingCategory("Search", "FTS options, default sort, results per page", "search"),
            new SettingCategory("Keyboard Shortcuts", "View and remap keyboard shortcuts", "keyboard"),
            new SettingCategory("Security & Session", "Session timeout, TLS cert trust, sign-out", "security"),
            new SettingCategory("Audit & Activity", "Audit log, access logs, activity history", "audit"),
            new SettingCategory("About & Updates", "Version, check for updates, release notes, diagnostics", "about"),
        ];

        if (Categories.Count > 0)
            _selectedCategory = Categories[0];
    }

    public ObservableCollection<SettingCategory> Categories { get; }

    // ── Category selection ───────────────────────────────────

    public SettingCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            _selectedCategory = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAppearanceSelected));
            OnPropertyChanged(nameof(IsCatalogSelected));
            OnPropertyChanged(nameof(IsConnectionSelected));
            OnPropertyChanged(nameof(IsIngestionSelected));
            OnPropertyChanged(nameof(IsMetadataSelected));
            OnPropertyChanged(nameof(IsAiSelected));
            OnPropertyChanged(nameof(IsSearchSelected));
            OnPropertyChanged(nameof(IsKeyboardSelected));
            OnPropertyChanged(nameof(IsSecuritySelected));
            OnPropertyChanged(nameof(IsAuditSelected));
            OnPropertyChanged(nameof(IsAboutSelected));
        }
    }

    public bool IsAppearanceSelected => SelectedCategory?.Key == "appearance";
    public bool IsCatalogSelected => SelectedCategory?.Key == "catalog";
    public bool IsConnectionSelected => SelectedCategory?.Key == "connection";
    public bool IsIngestionSelected => SelectedCategory?.Key == "ingestion";
    public bool IsMetadataSelected => SelectedCategory?.Key == "metadata";
    public bool IsAiSelected => SelectedCategory?.Key == "ai";
    public bool IsSearchSelected => SelectedCategory?.Key == "search";
    public bool IsKeyboardSelected => SelectedCategory?.Key == "keyboard";
    public bool IsSecuritySelected => SelectedCategory?.Key == "security";
    public bool IsAuditSelected => SelectedCategory?.Key == "audit";
    public bool IsAboutSelected => SelectedCategory?.Key == "about";

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            FilterCategories();
        }
    }

    public ICommand SelectCategoryCommand { get; }

    // ── Reset commands ───────────────────────────────────────

    public ICommand ResetAppearanceCommand { get; }
    public ICommand ResetConnectionCommand { get; }
    public ICommand ResetIngestionCommand { get; }
    public ICommand ResetMetadataCommand { get; }
    public ICommand ResetSearchCommand { get; }
    public ICommand ResetAiCommand { get; }
    public ICommand ApplyThemeCommand { get; }

    // ── Appearance properties ────────────────────────────────

    public string[] ThemeOptions { get; }
    public string[] AccentOptions { get; }
    public string[] DensityOptions { get; }

    public int SelectedThemeIndex
    {
        get => _selectedThemeIndex;
        set
        {
            if (_selectedThemeIndex == value) return;
            _selectedThemeIndex = value;
            OnPropertyChanged();
            _ = SaveAndApplyAppearanceAsync();
        }
    }

    public int SelectedAccentIndex
    {
        get => _selectedAccentIndex;
        set
        {
            if (_selectedAccentIndex == value) return;
            _selectedAccentIndex = value;
            OnPropertyChanged();
            _ = SaveAndApplyAppearanceAsync();
        }
    }

    public int SelectedDensityIndex
    {
        get => _selectedDensityIndex;
        set
        {
            if (_selectedDensityIndex == value) return;
            _selectedDensityIndex = value;
            OnPropertyChanged();
            _ = SaveAndApplyAppearanceAsync();
        }
    }

    public double FontScale
    {
        get => _fontScale;
        set
        {
            _fontScale = Math.Clamp(value, 0.5, 2.0);
            OnPropertyChanged();
            _ = SaveAndApplyAppearanceAsync();
        }
    }

    public string DbLocation => _modeManager.DbPath;
    public string? ServiceHostPort => _modeManager.IsMultiUser
        ? $"{_config.ServiceHost}:{_config.ServicePort}"
        : null;
    public bool UseTls => _config.UseTls;
    public string ModeLabel => _modeManager.IsStandalone ? "Local SQLite" : "Multi-user service";
    public string ConfigVersion => "1.0";

    /// <summary>Expose config for XAML binding in Connection section.</summary>
    public AdamConfig Config => _config;

    // ── Ingestion properties ─────────────────────────────────

    public bool AiAutoTagEnabled
    {
        get => _aiAutoTagEnabled;
        set
        {
            _aiAutoTagEnabled = value;
            OnPropertyChanged();
            _ = _prefs.SetAsync("ingestion.aiAutoTag", value);
        }
    }

    // ── Search / Gallery properties ──────────────────────────

    public string[] DefaultSortOptions { get; }
    public int[] ResultsPerPageOptions { get; }
    public int[] ThumbnailSizeOptions { get; }

    public int DefaultSortIndex
    {
        get => _defaultSortIndex;
        set
        {
            _defaultSortIndex = value;
            OnPropertyChanged();
            _ = _prefs.SetAsync("gallery.defaultSort", value);
        }
    }

    public int ResultsPerPage
    {
        get => _resultsPerPage;
        set
        {
            _resultsPerPage = value;
            OnPropertyChanged();
            _ = _prefs.SetAsync("gallery.resultsPerPage", value);
        }
    }

    public int ThumbnailSize
    {
        get => _thumbnailSize;
        set
        {
            _thumbnailSize = value;
            OnPropertyChanged();
            _ = _prefs.SetAsync("gallery.thumbnailSize", value);
        }
    }

    // ── Metadata properties ──────────────────────────────────

    public bool PanelsOpenByDefault
    {
        get => _panelsOpenByDefault;
        set
        {
            _panelsOpenByDefault = value;
            OnPropertyChanged();
            _ = _prefs.SetAsync("metadata.panelsOpenByDefault", value);
        }
    }

    public string[] ScriptingOptions { get; }

    // ── About ────────────────────────────────────────────────

    public string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
    public string RuntimeVersion => Environment.Version.ToString();
    public string OsDescription => Environment.OSVersion.ToString();
    public string ProcessArch => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
    public string WorkingSet => $"{Environment.WorkingSet / (1024 * 1024)} MB";

    // ── Public methods ───────────────────────────────────────

    /// <summary>
    /// Loads preference values from the service into the ViewModel properties.
    /// Called once at startup after DI container is built.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            await _prefs.LoadAsync(ct);

            _selectedThemeIndex = await _prefs.GetOrDefaultAsync("appearance.theme", 0);
            _selectedAccentIndex = await _prefs.GetOrDefaultAsync("appearance.accent", 0);
            _selectedDensityIndex = await _prefs.GetOrDefaultAsync("appearance.density", 1);
            _fontScale = await _prefs.GetOrDefaultAsync("appearance.fontScale", 1.0);
            _aiAutoTagEnabled = await _prefs.GetOrDefaultAsync("ingestion.aiAutoTag", false);
            _defaultSortIndex = await _prefs.GetOrDefaultAsync("gallery.defaultSort", 1);
            _resultsPerPage = await _prefs.GetOrDefaultAsync("gallery.resultsPerPage", 48);
            _thumbnailSize = await _prefs.GetOrDefaultAsync("gallery.thumbnailSize", 150);
            _panelsOpenByDefault = await _prefs.GetOrDefaultAsync("metadata.panelsOpenByDefault", true);

            // Notify UI of loaded values
            OnPropertyChanged(nameof(SelectedThemeIndex));
            OnPropertyChanged(nameof(SelectedAccentIndex));
            OnPropertyChanged(nameof(SelectedDensityIndex));
            OnPropertyChanged(nameof(FontScale));
            OnPropertyChanged(nameof(AiAutoTagEnabled));
            OnPropertyChanged(nameof(DefaultSortIndex));
            OnPropertyChanged(nameof(ResultsPerPage));
            OnPropertyChanged(nameof(ThumbnailSize));
            OnPropertyChanged(nameof(PanelsOpenByDefault));

            _logger.LogInformation("Settings loaded from preferences");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load preferences, using defaults");
        }
    }

    // ── Private helpers ──────────────────────────────────────

    private async Task SaveAndApplyAppearanceAsync()
    {
        // Persist
        await _prefs.SetAsync("appearance.theme", SelectedThemeIndex);
        await _prefs.SetAsync("appearance.accent", SelectedAccentIndex);
        await _prefs.SetAsync("appearance.density", SelectedDensityIndex);
        await _prefs.SetAsync("appearance.fontScale", FontScale);

        // Apply theme variant immediately
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = SelectedThemeIndex switch
            {
                0 => ThemeVariant.Light,
                1 => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
    }

    private async Task ApplyThemePreferenceAsync()
    {
        await SaveAndApplyAppearanceAsync();
    }

    private async Task ResetCategoryAsync(string key)
    {
        switch (key)
        {
            case "appearance":
                SelectedThemeIndex = 0;
                SelectedAccentIndex = 0;
                SelectedDensityIndex = 1;
                FontScale = 1.0;
                await _prefs.ResetAsync("appearance.theme");
                await _prefs.ResetAsync("appearance.accent");
                await _prefs.ResetAsync("appearance.density");
                await _prefs.ResetAsync("appearance.fontScale");
                break;
            case "connection":
                await _prefs.ResetAsync("connection");
                break;
            case "ingestion":
                AiAutoTagEnabled = false;
                await _prefs.ResetAsync("ingestion");
                break;
            case "metadata":
                PanelsOpenByDefault = true;
                await _prefs.ResetAsync("metadata");
                await _prefs.ResetAsync("metadata.expandedPanels");
                break;
            case "search":
                DefaultSortIndex = 1;
                ResultsPerPage = 48;
                ThumbnailSize = 150;
                await _prefs.ResetAsync("gallery");
                break;
            case "ai":
                await _prefs.ResetAsync("ai");
                break;
        }
    }

    private void FilterCategories()
    {
        // Simple client-side filter — hides categories not matching search text
        // For now, this is a pass-through since ItemsControl doesn't support live filtering
        // in all Avalonia configurations. The search box is available for future use.
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Represents a settings category in the left rail with a logical key for content binding.
/// </summary>
public sealed record SettingCategory
{
    public string Name { get; }
    public string Description { get; }
    public string Key { get; }

    public SettingCategory(string name, string description, string key)
    {
        Name = name;
        Description = description;
        Key = key;
    }
}
