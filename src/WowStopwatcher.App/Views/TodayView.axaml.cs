using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Views;

public partial class TodayView : UserControl
{
    private StopwatchRowViewModel? _dragged;
    private StopwatchRowViewModel? _dropTarget;

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

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(RowsList).Properties.IsLeftButtonPressed
            || e.Source is not Visual source
            || source.FindAncestorOfType<Button>(includeSelf: true) is not { } button
            || !button.Classes.Contains("grip")
            || button.DataContext is not StopwatchRowViewModel row)
        {
            return;
        }

        _dragged = row;
        row.IsDragging = true;
        button.Focus();
        e.Pointer.Capture(RowsList);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is null)
        {
            return;
        }

        var target = RowAt(e.GetPosition(RowsList));
        if (target == _dropTarget)
        {
            return;
        }

        SetDropTarget(target is not null && target != _dragged ? target : null);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragged is null)
        {
            return;
        }

        var dragged = _dragged;
        var target = _dropTarget;
        EndDrag();
        e.Pointer.Capture(null);

        if (target is not null && DataContext is TodayViewModel today)
        {
            today.MoveTo(dragged.Id, target.Id);
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private StopwatchRowViewModel? RowAt(Point position) =>
        (RowsList.InputHitTest(position) as Visual)?.FindAncestorOfType<StopwatchRowView>(includeSelf: true)?.DataContext
            as StopwatchRowViewModel;

    private void SetDropTarget(StopwatchRowViewModel? target)
    {
        if (_dropTarget is not null)
        {
            _dropTarget.IsDropTarget = false;
        }

        _dropTarget = target;
        if (target is not null)
        {
            target.IsDropTarget = true;
        }
    }

    private void EndDrag()
    {
        SetDropTarget(null);
        if (_dragged is not null)
        {
            _dragged.IsDragging = false;
            _dragged = null;
        }
    }
}
