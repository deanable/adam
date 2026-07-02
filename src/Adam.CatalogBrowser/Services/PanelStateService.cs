using System.ComponentModel;
using System.Runtime.CompilerServices;
using Adam.CatalogBrowser.ViewModels;
using Adam.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Adam.CatalogBrowser.Services;

/// <summary>
/// Manages sidebar and right-panel expander states with persistence to
/// <see cref="IUserPreferenceService"/>. All 15 expander booleans
/// fire <see cref="PropertyChanged"/> for Avalonia XAML binding.
/// </summary>
public sealed class PanelStateService : INotifyPropertyChanged
{
    private readonly IUserPreferenceService? _prefs;
    private readonly ILogger<PanelStateService>? _logger;

    // Sidebar left-panel expanders (12)
    private bool _isSidebarFoldersExpanded = true;
    private bool _isSidebarCollectionsExpanded = true;
    private bool _isSidebarSavedSearchesExpanded = true;
    private bool _isSidebarRecentSearchesExpanded = true;
    private bool _isSidebarKeywordsExpanded = true;
    private bool _isSidebarMediaFormatExpanded = true;
    private bool _isSidebarCategoriesExpanded = true;
    private bool _isSidebarDateTakenExpanded = true;
    private bool _isSidebarRatingExpanded;
    private bool _isSidebarLabelExpanded;
    private bool _isSidebarFlagExpanded;
    private bool _isSidebarAiModelExpanded = true;

    // Right-panel expanders (3)
    private bool _isRightMetadataExpanded = true;
    private bool _isRightCommentsExpanded = true;
    private bool _isRightTagsExpanded = true;

    // Suppresses individual saves during batch restore to avoid race conditions
    private bool _isRestoring;

    public PanelStateService(IUserPreferenceService? prefs = null, ILogger<PanelStateService>? logger = null)
    {
        _prefs = prefs;
        _logger = logger;
    }

    // ── Sidebar left-panel expanders (12) ──

    public bool IsSidebarFoldersExpanded
    {
        get => _isSidebarFoldersExpanded;
        set { _isSidebarFoldersExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsSidebarCollectionsExpanded
    {
        get => _isSidebarCollectionsExpanded;
        set { _isSidebarCollectionsExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsSidebarSavedSearchesExpanded
    {
        get => _isSidebarSavedSearchesExpanded;
        set { _isSidebarSavedSearchesExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsSidebarRecentSearchesExpanded
    {
        get => _isSidebarRecentSearchesExpanded;
        set { _isSidebarRecentSearchesExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsSidebarKeywordsExpanded
    {
        get => _isSidebarKeywordsExpanded;
        set { _isSidebarKeywordsExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsSidebarMediaFormatExpanded
    {
        get => _isSidebarMediaFormatExpanded;
        set { _isSidebarMediaFormatExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsSidebarCategoriesExpanded
    {
        get => _isSidebarCategoriesExpanded;
        set { _isSidebarCategoriesExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsSidebarDateTakenExpanded
    {
        get => _isSidebarDateTakenExpanded;
        set { _isSidebarDateTakenExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsSidebarRatingExpanded
    {
        get => _isSidebarRatingExpanded;
        set { _isSidebarRatingExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsSidebarLabelExpanded
    {
        get => _isSidebarLabelExpanded;
        set { _isSidebarLabelExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsSidebarFlagExpanded
    {
        get => _isSidebarFlagExpanded;
        set { _isSidebarFlagExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsSidebarAiModelExpanded
    {
        get => _isSidebarAiModelExpanded;
        set { _isSidebarAiModelExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    // ── Right-panel expanders (3) ──

    public bool IsRightMetadataExpanded
    {
        get => _isRightMetadataExpanded;
        set { _isRightMetadataExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsRightCommentsExpanded
    {
        get => _isRightCommentsExpanded;
        set { _isRightCommentsExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    public bool IsRightTagsExpanded
    {
        get => _isRightTagsExpanded;
        set { _isRightTagsExpanded = value; OnPropertyChanged(); if (!_isRestoring) _ = SaveAsync(); }
    }

    // ── Persistence ──

    private const string ExpandedPanelsKey = "metadata.expandedPanels";

    /// <summary>
    /// Persists all panel states to UserPreferenceService.
    /// Called automatically on every setter change (with batching protection).
    /// </summary>
    public async Task SaveAsync()
    {
        if (_prefs == null) return;
        try
        {
            var expanded = new HashSet<string>
            {
                "folders", "collections", "savedSearches", "recentSearches",
                "keywords", "mediaFormat", "categories", "dateTaken", "aiModel",
                "metadata", "comments", "tags"
            };

            if (!_isSidebarFoldersExpanded) expanded.Remove("folders");
            if (!_isSidebarCollectionsExpanded) expanded.Remove("collections");
            if (!_isSidebarSavedSearchesExpanded) expanded.Remove("savedSearches");
            if (!_isSidebarRecentSearchesExpanded) expanded.Remove("recentSearches");
            if (!_isSidebarKeywordsExpanded) expanded.Remove("keywords");
            if (!_isSidebarMediaFormatExpanded) expanded.Remove("mediaFormat");
            if (!_isSidebarCategoriesExpanded) expanded.Remove("categories");
            if (!_isSidebarDateTakenExpanded) expanded.Remove("dateTaken");
            if (!_isSidebarAiModelExpanded) expanded.Remove("aiModel");
            if (_isSidebarRatingExpanded) expanded.Add("rating"); else expanded.Remove("rating");
            if (_isSidebarLabelExpanded) expanded.Add("label"); else expanded.Remove("label");
            if (_isSidebarFlagExpanded) expanded.Add("flag"); else expanded.Remove("flag");
            if (!_isRightMetadataExpanded) expanded.Remove("metadata");
            if (!_isRightCommentsExpanded) expanded.Remove("comments");
            if (!_isRightTagsExpanded) expanded.Remove("tags");

            // Merge with existing saved state (metadata editor panels stored by MetadataEditorViewModel)
            var existing = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
            if (existing != null)
            {
                foreach (var p in existing)
                {
                    if (p.Length == 1 && p[0] >= 'A' && p[0] <= 'H')
                        expanded.Add(p);
                }
            }

            await _prefs.SetAsync(ExpandedPanelsKey, expanded);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to persist panel states");
        }
    }

    /// <summary>
    /// Restores saved panel states from UserPreferenceService.
    /// Suppresses auto-save during batch restore, then coalesces to a single save.
    /// </summary>
    public async Task RestoreAsync(IUiDispatcher? dispatcher = null)
    {
        if (_prefs == null) return;
        try
        {
            var expanded = await _prefs.GetAsync<HashSet<string>>(ExpandedPanelsKey);
            if (expanded == null) return;

            _isRestoring = true;

            var action = () =>
            {
                IsSidebarFoldersExpanded = expanded.Contains("folders");
                IsSidebarCollectionsExpanded = expanded.Contains("collections");
                IsSidebarSavedSearchesExpanded = expanded.Contains("savedSearches");
                IsSidebarRecentSearchesExpanded = expanded.Contains("recentSearches");
                IsSidebarKeywordsExpanded = expanded.Contains("keywords");
                IsSidebarMediaFormatExpanded = expanded.Contains("mediaFormat");
                IsSidebarCategoriesExpanded = expanded.Contains("categories");
                IsSidebarDateTakenExpanded = expanded.Contains("dateTaken");
                IsSidebarRatingExpanded = expanded.Contains("rating");
                IsSidebarLabelExpanded = expanded.Contains("label");
                IsSidebarFlagExpanded = expanded.Contains("flag");
                IsSidebarAiModelExpanded = expanded.Contains("aiModel");
                IsRightMetadataExpanded = expanded.Contains("metadata");
                IsRightCommentsExpanded = expanded.Contains("comments");
                IsRightTagsExpanded = expanded.Contains("tags");
            };

            if (dispatcher != null)
                await dispatcher.InvokeAsync(action);
            else
                action();

            _isRestoring = false;

            // Single coalesced save after all panels are restored
            _ = SaveAsync();
        }
        catch (Exception ex)
        {
            _isRestoring = false;
            _logger?.LogDebug(ex, "Failed to restore panel states");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
