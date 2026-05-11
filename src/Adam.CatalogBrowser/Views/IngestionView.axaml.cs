using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class IngestionView : UserControl
{
    public IngestionView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { } files) return;
        if (DataContext is not IngestionViewModel vm) return;

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
        if (VisualRoot is not Window window) return;
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select files to ingest"
        });
        if (DataContext is not IngestionViewModel vm) return;
        vm.AddFiles(files.Select(f => f.Path.LocalPath));
    }

    private async void OnSelectFolderClick(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is not Window window) return;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
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
