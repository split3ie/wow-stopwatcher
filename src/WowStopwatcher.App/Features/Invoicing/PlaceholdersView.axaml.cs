using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace WowStopwatcher.App.Features.Invoicing;

public partial class PlaceholdersView : UserControl
{
    private const double ScriptMinHeight = 64;

    private PlaceholdersViewModel? _viewModel;
    private bool _canExpand;
    private TextBox? _resizingScript;
    private double _resizeStartY;
    private double _resizeStartHeight;
    private Panel? _resizingPanel;
    private double _resizeStartX;
    private double _resizeStartExtra;
    private double _resizeMaxExtra;

    public PlaceholdersView()
    {
        InitializeComponent();
        Chips.LayoutUpdated += (_, _) => UpdateBuiltins();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as PlaceholdersViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateBuiltins();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaceholdersViewModel.IsBuiltinsExpanded))
        {
            UpdateBuiltins();
        }
    }

    private void OnBuiltinsRowTapped(object? sender, TappedEventArgs e)
    {
        // A tap on a chip copies its name; only the rest of the row toggles.
        if (_viewModel is null || !_canExpand || (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        _viewModel.ToggleBuiltinsCommand.Execute(null);
    }

    private void OnScriptGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Parent: Panel { Children: [TextBox script, ..] } panel } grip ||
            !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _resizingScript = script;
        _resizeStartY = e.GetPosition(this).Y;
        _resizeStartHeight = script.Bounds.Height;
        _resizingPanel = panel;
        _resizeStartX = e.GetPosition(this).X;
        _resizeStartExtra = -panel.Margin.Right;
        // Widening overlays the columns to the right, up to the row's right edge.
        _resizeMaxExtra = panel.Parent is Control { Parent: Grid row } column ? Math.Max(0, row.Bounds.Width - column.Bounds.Right) : 0;
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    private void OnScriptGripMoved(object? sender, PointerEventArgs e)
    {
        if (_resizingScript is null)
        {
            return;
        }

        // The dragged height acts as a minimum, so long scripts still grow beyond it.
        var height = _resizeStartHeight + e.GetPosition(this).Y - _resizeStartY;
        _resizingScript.MinHeight = Math.Max(ScriptMinHeight, height);
        if (_resizingPanel is not null)
        {
            var extra = Math.Clamp(_resizeStartExtra + e.GetPosition(this).X - _resizeStartX, 0, _resizeMaxExtra);
            _resizingPanel.Margin = new Thickness(0, 0, -extra, 0);
        }

        e.Handled = true;
    }

    private void OnScriptGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        _resizingScript = null;
        _resizingPanel = null;
        e.Pointer.Capture(null);
    }

    private void OnScriptGripCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _resizingScript = null;
        _resizingPanel = null;
    }

    /// <summary>Limits the chips to one row when collapsed (the height of the first chip) and shows the chevron only when more rows exist.</summary>
    private void UpdateBuiltins()
    {
        if (Chips.ItemsPanelRoot is not Panel panel || panel.Children.Count == 0)
        {
            return;
        }

        var first = panel.Children[0];
        var rowHeight = first.Bounds.Height + first.Margin.Bottom;
        if (rowHeight <= 0)
        {
            return;
        }

        var expanded = _viewModel?.IsBuiltinsExpanded == true;
        // DesiredSize is clamped by MaxHeight, so look at where the wrapped chips are arranged instead.
        var firstTop = first.Bounds.Y;
        var hasMoreRows = false;
        for (var i = 1; i < panel.Children.Count; i++)
        {
            if (panel.Children[i].Bounds.Y > firstTop + 0.5)
            {
                hasMoreRows = true;
                break;
            }
        }

        var canExpand = hasMoreRows || expanded;
        if (canExpand != _canExpand)
        {
            _canExpand = canExpand;
            BuiltinsChevron.IsVisible = canExpand;
            BuiltinsRow.Cursor = canExpand ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }

        var maxHeight = expanded ? double.PositiveInfinity : rowHeight;
        if (ChipsHost.MaxHeight != maxHeight)
        {
            ChipsHost.MaxHeight = maxHeight;
        }
    }
}
