using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Localization;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.ViewModels;

/// <summary>
/// The "Protect your data" window: an optional PIN step, then the recovery code and the confirmation. The actual work is
/// <see cref="ProtectDataRequest.Commit"/>; this view model only collects input and shows the progress state.
/// </summary>
public sealed partial class ProtectDataViewModel : ObservableObject
{
    private readonly ProtectDataRequest _request;
    /// <summary>How long the recovery code may stay on the clipboard after Copy.</summary>
    public static readonly TimeSpan ClipboardLifetime = TimeSpan.FromSeconds(60);

    private readonly IClipboardService _clipboard;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _clearTimer;
    private bool _codeCopied;

    public ProtectDataViewModel(ProtectDataRequest request, IClipboardService clipboard, TimeProvider? time = null)
    {
        _request = request;
        _clipboard = clipboard;
        _time = time ?? TimeProvider.System;
        _isPinStep = request.PinAvailable;
    }

    /// <summary>Raised when the work finished (null) or failed (the exception).</summary>
    public event EventHandler<Exception?>? Finished;

    public string Title => Loc.T("protect.title");

    public string Intro => Loc.T(_request.IsNewDatabase ? "protect.introNew" : "protect.intro");

    public string RecoveryCode => _request.RecoveryCode;

    public string WorkingText => Loc.T(_request.IsNewDatabase ? "protect.workingNew" : "protect.working");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCodeStep))]
    private bool _isPinStep;

    public bool IsCodeStep => !IsPinStep;

    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private string _confirmPin = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinEnabled))]
    private bool _noPin;

    public bool PinEnabled => !NoPin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPinError))]
    private string? _pinError;

    public bool HasPinError => !string.IsNullOrEmpty(PinError);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EncryptCommand))]
    private bool _saved;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EncryptCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditCode))]
    private bool _isWorking;

    public bool CanEditCode => !IsWorking;

    [ObservableProperty]
    private string _copyText = Loc.T("protect.copy");

    [RelayCommand]
    private void Continue()
    {
        if (!NoPin)
        {
            if (!PinPolicy.IsValid(Pin))
            {
                PinError = Loc.T("protect.pinInvalid");
                return;
            }

            if (Pin != ConfirmPin)
            {
                PinError = Loc.T("protect.pinMismatch");
                return;
            }
        }

        PinError = null;
        IsPinStep = false;
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (await _clipboard.SetTextAsync(RecoveryCode))
        {
            CopyText = Loc.T("protect.copied");
            _codeCopied = true;
            _clearTimer?.Cancel();
            _clearTimer = new CancellationTokenSource();
            _ = ClearAfterDelayAsync(_clearTimer.Token);
        }
    }

    /// <summary>
    /// Empties the clipboard if it still holds the recovery code that Copy put there. If the clipboard cannot be read back,
    /// it is left alone. Called when the window closes; also runs once after <see cref="ClipboardLifetime"/>.
    /// </summary>
    public async Task ClearCopiedCodeAsync()
    {
        _clearTimer?.Cancel();
        _clearTimer = null;
        if (!_codeCopied)
        {
            return;
        }

        _codeCopied = false;
        if (await _clipboard.TryGetTextAsync() == RecoveryCode)
        {
            await _clipboard.ClearAsync();
        }
    }

    private async Task ClearAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ClipboardLifetime, _time, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ClearCopiedCodeAsync();
    }

    private bool CanEncrypt() => Saved && !IsWorking;

    [RelayCommand(CanExecute = nameof(CanEncrypt))]
    private async Task EncryptAsync()
    {
        IsWorking = true;
        Exception? error = null;
        try
        {
            await _request.Commit(_request.PinAvailable && !NoPin ? Pin : null);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        Finished?.Invoke(this, error);
    }
}
