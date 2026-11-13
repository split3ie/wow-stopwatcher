using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.ViewModels;

/// <summary>The recovery code window: one text field, a wrong-code message and the choices Unlock, Quit and another folder.</summary>
public sealed partial class RecoveryPromptViewModel(RecoveryPromptRequest request, DatabaseRecovery recovery) : ObservableObject
{
    public string DatabasePath => request.DatabasePath;

    /// <summary>Raised when a correct code was entered.</summary>
    public event EventHandler? Unlocked;

    /// <summary>Raised when the user chose Quit.</summary>
    public event EventHandler? QuitRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => !string.IsNullOrEmpty(Error);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool _isChecking;

    partial void OnCodeChanged(string value) => Error = null;

    private bool CanUnlock() => !IsChecking && !string.IsNullOrWhiteSpace(Code);

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        IsChecking = true;
        try
        {
            if (await request.TryCode(Code))
            {
                Unlocked?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Error = Loc.T("recovery.wrong");
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Picks another database folder; the app restarts when one was chosen.</summary>
    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        var failure = await recovery.ChooseFolderAsync();
        if (failure is not null)
        {
            Error = failure;
        }
    }
}
