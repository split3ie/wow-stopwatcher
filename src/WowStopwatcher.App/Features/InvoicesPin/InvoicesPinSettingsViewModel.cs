using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>The "Invoices PIN" card in the General section of Settings.</summary>
public sealed partial class InvoicesPinSettingsViewModel : ObservableObject, ISettingsGeneralItem
{
    private readonly InvoicesPinService _service;
    private readonly InvoicesPinPrompts _prompts;
    private bool _reverting;

    public InvoicesPinSettingsViewModel(InvoicesPinService service, InvoicesPinPrompts prompts)
    {
        _service = service;
        _prompts = prompts;
        _isEnabled = service.IsEnabled;
    }

    public int Order => 20;

    /// <summary>The toggle. Turning it on may ask for a new PIN; turning it off asks for the current one.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_reverting)
        {
            _ = ApplyToggleAsync(value);
        }
    }

    /// <summary>Applies a toggle change; the toggle goes back when the user cancels.</summary>
    public async Task ApplyToggleAsync(bool turnOn)
    {
        bool done;
        if (turnOn)
        {
            done = await _prompts.SetNewAsync();
        }
        else
        {
            done = await _prompts.ConfirmCurrentAsync(Loc.T("invoicing.pin.off.title"), Loc.T("invoicing.pin.off.confirm"));
            if (done)
            {
                _service.Disable();
            }
        }

        SetToggle(done ? turnOn : !turnOn);
    }

    [RelayCommand]
    private Task ChangeAsync() => _prompts.ChangeAsync();

    [RelayCommand]
    private Task ForgotAsync() => _prompts.ForgotAsync();

    private void SetToggle(bool value)
    {
        _reverting = true;
        IsEnabled = value;
        _reverting = false;
    }
}
