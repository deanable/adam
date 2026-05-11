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
        var paths = files.OfType<IStorageFile>().Select(f => f.Path.LocalPath);
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
        var paths = files.Select(f => f.Path.LocalPath);
        vm.AddFiles(paths);
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
