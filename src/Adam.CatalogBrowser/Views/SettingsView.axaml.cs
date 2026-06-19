using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnResetMetadataPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner == null)
            return;

        var confirmed = await ConfirmationDialog.ShowAsync(
            owner,
            "Reset Metadata Defaults",
            "This will reset all metadata editor panels to their default layout and " +
            "restore the 'Panels Open by Default' setting.\n\nContinue?",
            confirmText: "Reset",
            cancelText: "Cancel",
            isDestructive: false);

        if (confirmed && vm.ResetMetadataCommand.CanExecute(null))
        {
            vm.ResetMetadataCommand.Execute(null);
        }
    }

    private void OnCategoryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is SettingCategory category)
        {
            if (DataContext is SettingsViewModel vm && vm.SelectCategoryCommand.CanExecute(category))
            {
                vm.SelectCategoryCommand.Execute(category);
            }
        }
    }

    private void OnAccentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is string accentName)
        {
            if (DataContext is SettingsViewModel vm)
            {
                var index = System.Array.IndexOf(vm.AccentOptions, accentName);
                if (index >= 0)
                    vm.SelectedAccentIndex = index;
            }
        }
    }

    private void OnOpenLogPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var logPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                "adam-catalog.log");
            if (System.IO.File.Exists(logPath))
            {
                Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
            }
        }
        catch { }
    }

    private void OnOpenDataFolderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var dataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Adam", "CatalogBrowser");
            if (System.IO.Directory.Exists(dataDir))
            {
                Process.Start(new ProcessStartInfo(dataDir) { UseShellExecute = true });
            }
        }
        catch { }
    }
}
