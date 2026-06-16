using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Adam.CatalogBrowser.Controls;
using Adam.CatalogBrowser.ViewModels;

namespace Adam.CatalogBrowser.Views;

public partial class LoupeView : UserControl
{
    private LoupeViewModel? _vm;

    public LoupeView()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _vm = DataContext as LoupeViewModel;
        if (_vm != null)
        {
            _vm.RequestZoomIn += OnRequestZoomIn;
            _vm.RequestZoomOut += OnRequestZoomOut;
            _vm.InfoChanged += OnInfoChanged;
        }
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.RequestZoomIn -= OnRequestZoomIn;
            _vm.RequestZoomOut -= OnRequestZoomOut;
            _vm.InfoChanged -= OnInfoChanged;
        }
    }

    private void OnRequestZoomIn()
    {
        var zb = ImageZoomBorder;
        if (zb != null)
            zb.ZoomTo(zb.ZoomLevel * 1.25, new Point(zb.Bounds.Width / 2, zb.Bounds.Height / 2));
    }

    private void OnRequestZoomOut()
    {
        var zb = ImageZoomBorder;
        if (zb != null)
            zb.ZoomTo(zb.ZoomLevel / 1.25, new Point(zb.Bounds.Width / 2, zb.Bounds.Height / 2));
    }

    private void OnInfoChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ImageZoomBorder?.FitToWindow();
        }, DispatcherPriority.Background);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm == null) return;

        switch (e.Key)
        {
            case Key.Left:
                if (_vm.PrevCommand.CanExecute(null))
                    _vm.PrevCommand.Execute(null);
                break;
            case Key.Right:
                if (_vm.NextCommand.CanExecute(null))
                    _vm.NextCommand.Execute(null);
                break;
            case Key.OemPlus:
            case Key.Add:
                OnRequestZoomIn();
                break;
            case Key.OemMinus:
            case Key.Subtract:
                OnRequestZoomOut();
                break;
            case Key.D0 when e.KeyModifiers == KeyModifiers.Control:
                ImageZoomBorder?.FitToWindow();
                break;
            case Key.I:
                _vm.ToggleInfoOverlayCommand.Execute(null);
                break;
            case Key.Escape:
                _vm.CloseCommand.Execute(null);
                break;
            case Key.F:
                _vm.ToggleFitFillCommand.Execute(null);
                break;
        }
    }

    private void OnFilmstripItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is FilmstripItem item)
        {
            _ = _vm?.NavigateToAsync(item.AssetId);
        }
    }
}
