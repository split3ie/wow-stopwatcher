using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Views;

public partial class TodayView : UserControl
{
    private StopwatchRowViewModel? _dragged;
    private List<Control> _containers = [];
    private double[] _tops = [];
    private double[] _heights = [];
    private int _fromIndex;
    private int _targetIndex;
    private double _pressY;
    private double _minDy;
    private double _maxDy;

    public TodayView()
    {
        InitializeComponent();

        // Reordering is a pointer drag that starts on a row's grip button. The grip would otherwise swallow
        // the press, so the handlers run in the tunnel phase.
        RowsList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        RowsList.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        RowsList.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        RowsList.AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost);
    }

    private const double RowSpacing = 14;
    private static readonly TimeSpan ShiftDuration = TimeSpan.FromMilliseconds(150);

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(RowsList).Properties.IsLeftButtonPressed
            || e.Source is not Visual source
            || source.FindAncestorOfType<Button>(includeSelf: true) is not { } button
            || !button.Classes.Contains("grip")
            || button.DataContext is not StopwatchRowViewModel row
            || RowsList.ItemsPanelRoot is not Panel panel)
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
        e.Pointer.Capture(RowsList);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is null || RowsList.ItemsPanelRoot is not Panel panel)
        {
            return;
        }

        var dy = Math.Clamp(e.GetPosition(panel).Y - _pressY, _minDy, _maxDy);
        SetOffset(_containers[_fromIndex], dy);

        // The slot is decided by where the dragged row's center sits among the other rows' original midpoints.
        var center = _tops[_fromIndex] + _heights[_fromIndex] / 2 + dy;
        var target = 0;
        for (var i = 0; i < _containers.Count; i++)
        {
            if (i != _fromIndex && _tops[i] + _heights[i] / 2 < center)
            {
                target++;
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
        var targetRow = _targetIndex != _fromIndex ? _containers[_targetIndex].DataContext as StopwatchRowViewModel : null;
        EndDrag();
        e.Pointer.Capture(null);

        if (targetRow is not null && DataContext is TodayViewModel today)
        {
            today.MoveTo(dragged.Id, targetRow.Id);
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
