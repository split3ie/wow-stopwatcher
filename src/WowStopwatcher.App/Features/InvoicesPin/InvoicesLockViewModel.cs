using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>The lock card that replaces the Invoices page while a PIN is active and the page is locked.</summary>
public sealed partial class InvoicesLockViewModel : ObservableObject, IDisposable
{
    private readonly InvoicesPinService _service;
    private readonly InvoicesPinPrompts _prompts;
    private readonly TimeProvider _time;
    private readonly IUiDispatcher _dispatcher;
    private ITimer? _countdown;

    public InvoicesLockViewModel(InvoicesPinService service, InvoicesPinPrompts prompts, TimeProvider time, IUiDispatcher dispatcher)
    {
        _service = service;
        _prompts = prompts;
        _time = time;
        _dispatcher = dispatcher;
        service.Changed += OnStateChanged;
        service.LockState.Changed += OnStateChanged;
        RefreshLockout();
    }

    /// <summary>True while a PIN is active and the page is locked.</summary>
    public bool IsLocked => _service.IsEnabled && _service.LockState.IsLocked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _pin = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool _isLockedOut;

    [ObservableProperty]
    private string _lockoutText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool _isChecking;

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>Restarts the idle timer; called for user input while the page is shown.</summary>
    public void RecordActivity() => _service.LockState.RecordActivity();

    /// <summary>Updates the lockout text from the time left; the countdown timer calls it every second.</summary>
    public void RefreshLockout()
    {
        var remaining = _service.Limiter.RemainingLockout;
        if (remaining > TimeSpan.Zero)
        {
            LockoutText = InvoicesPinPrompts.LockoutText(remaining);
            IsLockedOut = true;
            _countdown ??= _time.CreateTimer(_ => _dispatcher.Post(RefreshLockout), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            return;
        }

        IsLockedOut = false;
        LockoutText = string.Empty;
        _countdown?.Dispose();
        _countdown = null;
    }

    public void Dispose()
    {
        _service.Changed -= OnStateChanged;
        _service.LockState.Changed -= OnStateChanged;
        _countdown?.Dispose();
        _countdown = null;
    }

    partial void OnPinChanged(string value) => Error = null;

    private bool CanUnlock() => !IsChecking && !IsLockedOut && Pin.Length > 0;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        IsChecking = true;
        PinCheckResult result;
        try
        {
            result = await _service.VerifyAsync(Pin);
        }
        finally
        {
            IsChecking = false;
        }

        switch (result.Status)
        {
            case PinCheckStatus.Success:
                Pin = string.Empty;
                _service.LockState.Unlock();
                OnPropertyChanged(nameof(IsLocked));
                break;
            case PinCheckStatus.Wrong:
                Error = Loc.Plural("invoicing.pin.wrong", result.RemainingTries);
                break;
            default:
                Pin = string.Empty;
                RefreshLockout();
                break;
        }
    }

    [RelayCommand]
    private async Task ForgotAsync()
    {
        if (await _prompts.ForgotAsync())
        {
            // The recovery code proved who it is, so the page opens with the new PIN.
            Pin = string.Empty;
            _service.LockState.Unlock();
            OnPropertyChanged(nameof(IsLocked));
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (IsLocked)
        {
            Pin = string.Empty;
        }

        OnPropertyChanged(nameof(IsLocked));
    }
}
