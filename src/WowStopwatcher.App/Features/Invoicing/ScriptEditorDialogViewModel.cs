using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>The script editor popup: edits a draft of one placeholder script; Save writes it back, Cancel discards it.</summary>
public sealed partial class ScriptEditorDialogViewModel : ObservableObject
{
    private readonly Func<string, Task<(string Text, bool IsError)>> _render;
    private readonly Action<string> _save;
    private readonly Action _close;
    private int _version;

    internal ScriptEditorDialogViewModel(
        string alias,
        string script,
        Func<string, Task<(string Text, bool IsError)>> render,
        Action<string> save,
        Action close)
    {
        Title = Loc.T("invoicing.scriptDialog.title", ("alias", alias));
        _draft = script;
        _render = render;
        _save = save;
        _close = close;
    }

    public string Title { get; }

    [ObservableProperty]
    private string _draft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private bool _resultIsError;

    [ObservableProperty]
    private bool _isBusy;

    public bool HasResult => ResultText.Length > 0;

    [RelayCommand]
    private async Task RenderAsync()
    {
        var version = ++_version;
        IsBusy = true;
        try
        {
            var (text, isError) = await _render(Draft);
            if (version == _version)
            {
                ResultText = text;
                ResultIsError = isError;
            }
        }
        finally
        {
            if (version == _version)
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private void Save()
    {
        _save(Draft);
        _close();
    }

    [RelayCommand]
    private void Cancel() => _close();
}
