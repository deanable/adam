using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class IngestionView : UserControl
{
    private static readonly IBrush HighlightBorderBrush = new SolidColorBrush(Color.FromRgb(0, 90, 158));
    private static readonly IBrush HighlightBackgroundBrush = new SolidColorBrush(Color.FromRgb(230, 242, 255));
    private static readonly Thickness HighlightBorderThickness = new(2);
    private IBrush? _restoreBorderBrush;
    private IBrush? _restoreBackground;

    public IngestionView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

        if (e.DataTransfer.Contains(DataFormat.File))
        {
            // Save original values once
            _restoreBorderBrush ??= DropZoneBorder.BorderBrush;
            _restoreBackground ??= DropZoneBorder.Background;

            DropZoneBorder.BorderBrush = HighlightBorderBrush;
            DropZoneBorder.BorderThickness = HighlightBorderThickness;
            DropZoneBorder.Background = HighlightBackgroundBrush;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // Restore original appearance
        DropZoneBorder.BorderThickness = new Thickness(1);
        if (_restoreBorderBrush != null) DropZoneBorder.BorderBrush = _restoreBorderBrush;
        if (_restoreBackground != null) DropZoneBorder.Background = _restoreBackground;
        _restoreBorderBrush = null;
        _restoreBackground = null;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        // Reset visual state immediately
        OnDragLeave(sender, e);

        if (e.DataTransfer.TryGetFiles() is not { } files) return;
        if (DataContext is not IngestionViewModel vm) return;

        // Phase 7: defense-in-depth — reject drops when ingestion permission is not granted.
        // The file picker Border's IsEnabled gate prevents Click events, but DragDrop
        // events are registered at the UserControl level and can bypass IsEnabled inheritance.
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.DataContext is global::Adam.CatalogBrowser.ViewModels.MainWindowViewModel mwvm && !mwvm.CanIngest)
            return;

        var paths = new List<string>();
        foreach (var item in files)
        {
            if (item is IStorageFile file)
                paths.Add(file.Path.LocalPath);
            else if (item is IStorageFolder folder)
                paths.AddRange(await CollectFilesRecursive(folder));
        }
        vm.AddFiles(paths);
    }

    private async void OnSelectFilesClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select files to ingest"
        });
        if (DataContext is not IngestionViewModel vm) return;
        vm.AddFiles(files.Select(f => f.Path.LocalPath));
    }

    private async void OnSelectFolderClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select a folder to ingest"
        });
        if (DataContext is not IngestionViewModel vm) return;
        if (folders.Count == 0) return;

        var paths = await CollectFilesRecursive(folders[0]);
        vm.AddFiles(paths);
    }

    private static async Task<string[]> CollectFilesRecursive(IStorageFolder folder)
    {
        var result = new List<string>();
        await CollectRecursive(folder, result);
        return result.ToArray();
    }

    private static async Task CollectRecursive(IStorageFolder folder, List<string> results)
    {
        await foreach (var item in folder.GetItemsAsync())
        {
            if (item is IStorageFile file)
                results.Add(file.Path.LocalPath);
            else if (item is IStorageFolder subFolder)
                await CollectRecursive(subFolder, results);
        }
    }
}

public class StringNotEmptyConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly StringNotEmptyConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is string s && s.Length > 0;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
