using System.ComponentModel;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace WowStopwatcher.App.Features.Invoicing;

public partial class PlaceholdersView : UserControl
{
    private PlaceholdersViewModel? _viewModel;
    private bool _canExpand;

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

    private void OnEditScriptClick(object? sender, RoutedEventArgs e) => EditScript(sender);

    private void EditScript(object? sender)
    {
        if ((sender as StyledElement)?.DataContext is not PlaceholderRowViewModel row)
        {
            return;
        }

        var dialog = new ScriptEditorDialog();
        dialog.DataContext = row.CreateScriptEditor(dialog.Close);
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            _ = dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }
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
