using Avalonia;
using Avalonia.Controls;
using Adam.CatalogBrowser.Controls;
using Adam.CatalogBrowser.ViewModels;
using System.ComponentModel;

namespace Adam.CatalogBrowser.Views;

public partial class CompareView : UserControl
{
    private CompareViewModel? _vm;
    private bool _isUpdating;

    public CompareView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _vm = DataContext as CompareViewModel;
        if (_vm == null) return;

        if (LeftZoomBorder != null && RightZoomBorder != null)
        {
            LeftZoomBorder.ZoomChanged += OnLeftZoomChanged;
            LeftZoomBorder.PanChanged += OnLeftPanChanged;
            RightZoomBorder.ZoomChanged += OnRightZoomChanged;
            RightZoomBorder.PanChanged += OnRightPanChanged;

            _vm.SyncState.PropertyChanged += OnSyncStateChanged;
        }
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_vm == null) return;

        if (LeftZoomBorder != null)
        {
            LeftZoomBorder.ZoomChanged -= OnLeftZoomChanged;
            LeftZoomBorder.PanChanged -= OnLeftPanChanged;
        }
        if (RightZoomBorder != null)
        {
            RightZoomBorder.ZoomChanged -= OnRightZoomChanged;
            RightZoomBorder.PanChanged -= OnRightPanChanged;
        }
        _vm.SyncState.PropertyChanged -= OnSyncStateChanged;
    }

    private void OnLeftZoomChanged(object? sender, ZoomChangedEventArgs e)
    {
        if (_isUpdating || _vm == null || !_vm.IsSyncEnabled) return;
        _isUpdating = true;
        _vm.SyncState.ZoomLevel = e.ZoomLevel;
        _isUpdating = false;

        if (RightZoomBorder != null && _vm.IsSyncEnabled)
        {
            RightZoomBorder.BeginBatchUpdate();
            RightZoomBorder.ZoomLevel = e.ZoomLevel;
            RightZoomBorder.EndBatchUpdate();
        }
    }

    private void OnLeftPanChanged(object? sender, PanChangedEventArgs e)
    {
        if (_isUpdating || _vm == null || !_vm.IsSyncEnabled) return;
        _isUpdating = true;
        _vm.SyncState.PanOffset = e.PanOffset;
        _isUpdating = false;

        if (RightZoomBorder != null && _vm.IsSyncEnabled)
        {
            RightZoomBorder.BeginBatchUpdate();
            RightZoomBorder.PanOffset = e.PanOffset;
            RightZoomBorder.EndBatchUpdate();
        }
    }

    private void OnRightZoomChanged(object? sender, ZoomChangedEventArgs e)
    {
        if (_isUpdating || _vm == null || !_vm.IsSyncEnabled) return;
        _isUpdating = true;
        _vm.SyncState.ZoomLevel = e.ZoomLevel;
        _isUpdating = false;

        if (LeftZoomBorder != null && _vm.IsSyncEnabled)
        {
            LeftZoomBorder.BeginBatchUpdate();
            LeftZoomBorder.ZoomLevel = e.ZoomLevel;
            LeftZoomBorder.EndBatchUpdate();
        }
    }

    private void OnRightPanChanged(object? sender, PanChangedEventArgs e)
    {
        if (_isUpdating || _vm == null || !_vm.IsSyncEnabled) return;
        _isUpdating = true;
        _vm.SyncState.PanOffset = e.PanOffset;
        _isUpdating = false;

        if (LeftZoomBorder != null && _vm.IsSyncEnabled)
        {
            LeftZoomBorder.BeginBatchUpdate();
            LeftZoomBorder.PanOffset = e.PanOffset;
            LeftZoomBorder.EndBatchUpdate();
        }
    }

    private void OnSyncStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isUpdating || _vm == null || !_vm.IsSyncEnabled) return;
        _isUpdating = true;

        if (_vm.SyncState != null)
        {
            if (LeftZoomBorder != null)
            {
                LeftZoomBorder.ZoomLevel = _vm.SyncState.ZoomLevel;
                LeftZoomBorder.PanOffset = _vm.SyncState.PanOffset;
            }
            if (RightZoomBorder != null)
            {
                RightZoomBorder.ZoomLevel = _vm.SyncState.ZoomLevel;
                RightZoomBorder.PanOffset = _vm.SyncState.PanOffset;
            }
        }

        _isUpdating = false;
    }
}
