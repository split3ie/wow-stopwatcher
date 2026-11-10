using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.ViewModels;

/// <summary>State of the in-window confirmation dialog (hosted by the main window, shared by pages).</summary>
public sealed partial class ConfirmDialogViewModel : ObservableObject, IDialogService
{
    private Action? _onConfirm;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private string _confirmLabel = Loc.T("common.confirm");

    /// <summary>Opens the dialog; <paramref name="onConfirm"/> runs only if the user confirms.</summary>
    public void Show(string title, string body, string confirmLabel, Action onConfirm)
    {
        Title = title;
        Body = body;
        ConfirmLabel = confirmLabel;
        _onConfirm = onConfirm;
        IsOpen = true;
    }

    /// <summary>Closes the dialog without confirming.</summary>
    [RelayCommand]
    public void Cancel()
    {
        _onConfirm = null;
        IsOpen = false;
    }

    [RelayCommand]
    private void Confirm()
    {
        var action = _onConfirm;
        Cancel();
        action?.Invoke();
    }
}
