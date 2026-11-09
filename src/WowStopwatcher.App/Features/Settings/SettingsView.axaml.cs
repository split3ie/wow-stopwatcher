using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace WowStopwatcher.App.Features.Settings;

public partial class SettingsView : UserControl
{
    private SettingsStopwatchViewModel? _dragged;
    private List<Control> _containers = [];
    private double[] _tops = [];
    private double[] _heights = [];
    private int _fromIndex;
    private int _targetIndex;
    private double _pressY;
    private double _minDy;
    private double _maxDy;

    public SettingsView()
    {
        InitializeComponent();

        // Same pointer-capture drag as on Today: it starts on a card's grip button, which would otherwise
        // swallow the press, so the handlers run in the tunnel phase.
        ItemsList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        ItemsList.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        ItemsList.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        ItemsList.AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost);
    }

    private async void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string url }
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || TopLevel.GetTopLevel(this)?.Launcher is not { } launcher)
        {
            return;
        }

        try
        {
            await launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // No browser available; nothing else to do.
        }
    }

    private const double RowSpacing = 16;
    private static readonly TimeSpan ShiftDuration = TimeSpan.FromMilliseconds(150);

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ItemsList).Properties.IsLeftButtonPressed
            || e.Source is not Visual source
            || source.FindAncestorOfType<Button>(includeSelf: true) is not { } button
            || !button.Classes.Contains("grip")
            || button.DataContext is not SettingsStopwatchViewModel row
            || ItemsList.ItemsPanelRoot is not Panel panel)
        {
            return;
        }

        _containers = panel.Children.OfType<Control>().ToList();
        _fromIndex = _containers.FindIndex(c => c.DataContext == row);
        if (_fromIndex < 0)
        {
            _containers = [];
            return;
        }

        _tops = _containers.Select(c => c.Bounds.Top).ToArray();
        _heights = _containers.Select(c => c.Bounds.Height).ToArray();
        _targetIndex = _fromIndex;
        _pressY = e.GetPosition(panel).Y;
        _minDy = _tops[0] - _tops[_fromIndex];
        _maxDy = _tops[^1] + _heights[^1] - (_tops[_fromIndex] + _heights[_fromIndex]);

        // Other rows animate when shifted; the dragged row must follow the pointer without delay.
        for (var i = 0; i < _containers.Count; i++)
        {
            if (i != _fromIndex)
            {
                _containers[i].Transitions =
                [
                    new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = ShiftDuration },
                ];
            }
        }

        _containers[_fromIndex].ZIndex = 1;
        _dragged = row;
        row.IsDragging = true;
        button.Focus();
        e.Pointer.Capture(ItemsList);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is null || ItemsList.ItemsPanelRoot is not Panel panel)
        {
            return;
        }

        var dy = Math.Clamp(e.GetPosition(panel).Y - _pressY, _minDy, _maxDy);
        SetOffset(_containers[_fromIndex], dy);

        // The slot is decided by the dragged row's leading edge passing other rows' original midpoints.
        var draggedTop = _tops[_fromIndex] + dy;
        var draggedBottom = draggedTop + _heights[_fromIndex];
        var target = _fromIndex;
        for (var i = _fromIndex + 1; i < _containers.Count; i++)
        {
            if (_tops[i] + _heights[i] / 2 < draggedBottom)
            {
                target++;
            }
        }

        for (var i = 0; i < _fromIndex; i++)
        {
            if (_tops[i] + _heights[i] / 2 > draggedTop)
            {
                target--;
            }
        }

        if (target == _targetIndex)
        {
            return;
        }

        _targetIndex = target;
        var shift = _heights[_fromIndex] + RowSpacing;
        for (var i = 0; i < _containers.Count; i++)
        {
            if (i == _fromIndex)
            {
                continue;
            }

            var offset = 0.0;
            if (i > _fromIndex && i <= target)
            {
                offset = -shift;
            }
            else if (i < _fromIndex && i >= target)
            {
                offset = shift;
            }

            SetOffset(_containers[i], offset);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragged is null)
        {
            return;
        }

        var dragged = _dragged;
        var targetItem = _targetIndex != _fromIndex ? _containers[_targetIndex].DataContext as SettingsStopwatchViewModel : null;
        EndDrag();
        e.Pointer.Capture(null);

        if (targetItem is not null && DataContext is SettingsViewModel settings)
        {
            settings.MoveTo(dragged, targetItem);
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private static void SetOffset(Control container, double y) =>
        container.RenderTransform = TransformOperations.Parse(
            string.Create(CultureInfo.InvariantCulture, $"translate(0px, {y}px)"));

    private void EndDrag()
    {
        // Transitions go first so clearing the transforms snaps instantly instead of animating back.
        foreach (var container in _containers)
        {
            container.Transitions = null;
            container.RenderTransform = null;
            container.ZIndex = 0;
        }

        _containers = [];
        if (_dragged is not null)
        {
            _dragged.IsDragging = false;
            _dragged = null;
        }
    }
}
