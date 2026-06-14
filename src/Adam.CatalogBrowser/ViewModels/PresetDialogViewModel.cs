using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Adam.CatalogBrowser.Services;
using Adam.Shared.Models;
using Adam.Shared.Services;

namespace Adam.CatalogBrowser.ViewModels;

public class PresetDialogViewModel : INotifyPropertyChanged
{
    private readonly PresetManager _presetManager;
    private string _newPresetName = string.Empty;
    private MetadataPreset? _selectedPreset;
    private bool _isLoading;
    private string _statusText = string.Empty;

    public PresetDialogViewModel(PresetManager presetManager)
    {
        _presetManager = presetManager;
        SavePresetCommand = new RelayCommand(async _ => await SavePresetAsync(), _ => CanSavePreset);
        ApplyPresetCommand = new RelayCommand(async _ => await ApplyPresetAsync(), _ => CanApplyPreset);
        DeletePresetCommand = new RelayCommand(async _ => await DeletePresetAsync(), _ => CanDeletePreset);
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke());
    }

    public ObservableCollection<MetadataPreset> Presets { get; } = [];

    public string NewPresetName
    {
        get => _newPresetName;
        set
        {
            _newPresetName = value;
            OnPropertyChanged();
            (SavePresetCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public MetadataPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            _selectedPreset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedPreset));
            (ApplyPresetCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeletePresetCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelectedPreset => _selectedPreset != null;

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public bool CanSavePreset => !string.IsNullOrWhiteSpace(NewPresetName);
    public bool CanApplyPreset => SelectedPreset != null;
    public bool CanDeletePreset => SelectedPreset != null;

    /// <summary>
    /// The asset from which to capture metadata when saving a preset.
    /// Set before showing the dialog.
    /// </summary>
    public DigitalAsset? SourceAsset { get; set; }

    /// <summary>
    /// Callback invoked when the user selects a preset to apply.
    /// Returns the preset to apply, or null if cancelled.
    /// </summary>
    public Func<MetadataPreset, Task>? OnApplyPreset { get; set; }

    public ICommand SavePresetCommand { get; }
    public ICommand ApplyPresetCommand { get; }
    public ICommand DeletePresetCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>Fired when the user cancels/closes.</summary>
    public event Action? RequestClose;

    /// <summary>Fired when a preset is applied successfully.</summary>
    public event Action<string>? PresetApplied;

    /// <summary>Fired when a preset is saved.</summary>
    public event Action<string>? PresetSaved;

    /// <summary>Async initialization — loads preset list.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            Presets.Clear();
            var names = await _presetManager.ListPresetsAsync(ct);
            foreach (var name in names)
            {
                var preset = await _presetManager.LoadPresetAsync(name, ct);
                if (preset != null)
                    Presets.Add(preset);
            }
            StatusText = $"{Presets.Count} preset(s) loaded";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load presets: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SavePresetAsync()
    {
        if (!CanSavePreset || SourceAsset == null) return;

        try
        {
            IsLoading = true;
            StatusText = "Saving...";
            await _presetManager.CaptureAndSavePresetAsync(NewPresetName.Trim(), SourceAsset);
            StatusText = $"Preset \"{NewPresetName.Trim()}\" saved";
            PresetSaved?.Invoke(NewPresetName.Trim());

            // Reload list and select the new preset
            await LoadAsync();
            var match = Presets.FirstOrDefault(p =>
                string.Equals(p.Name, NewPresetName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null)
                SelectedPreset = match;
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to save preset: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ApplyPresetAsync()
    {
        if (SelectedPreset == null) return;

        try
        {
            StatusText = $"Applying \"{SelectedPreset.Name}\"...";
            if (OnApplyPreset != null)
                await OnApplyPreset(SelectedPreset);
            PresetApplied?.Invoke(SelectedPreset.Name);
            StatusText = $"Preset \"{SelectedPreset.Name}\" applied";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to apply preset: {ex.Message}";
        }
    }

    private async Task DeletePresetAsync()
    {
        if (SelectedPreset == null) return;

        try
        {
            var name = SelectedPreset.Name;
            var deleted = await _presetManager.DeletePresetAsync(name);
            if (deleted)
            {
                StatusText = $"Preset \"{name}\" deleted";
                Presets.Remove(SelectedPreset);
                SelectedPreset = null;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to delete preset: {ex.Message}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
