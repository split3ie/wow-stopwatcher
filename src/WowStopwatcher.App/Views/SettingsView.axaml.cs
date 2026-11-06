using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Views;

public partial class SettingsView : UserControl
{
    private SettingsStopwatchViewModel? _dragged;
    private SettingsStopwatchViewModel? _dropTarget;

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

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ItemsList).Properties.IsLeftButtonPressed
            || e.Source is not Visual source
            || source.FindAncestorOfType<Button>(includeSelf: true) is not { } button
            || !button.Classes.Contains("grip")
            || button.DataContext is not SettingsStopwatchViewModel item)
        {
            return;
        }

        _dragged = item;
        item.IsDragging = true;
        button.Focus();
        e.Pointer.Capture(ItemsList);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is null)
        {
            return;
        }

        var target = ItemAt(e.GetPosition(ItemsList));
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

        if (target is not null && DataContext is SettingsViewModel settings)
        {
            settings.MoveTo(dragged, target);
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private SettingsStopwatchViewModel? ItemAt(Point position) =>
        (ItemsList.InputHitTest(position) as Visual)?.FindAncestorOfType<SettingsStopwatchView>(includeSelf: true)?.DataContext
            as SettingsStopwatchViewModel;

    private void SetDropTarget(SettingsStopwatchViewModel? target)
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
