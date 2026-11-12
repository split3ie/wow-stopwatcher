using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.ViewModels;

/// <summary>State of the in-window dialog with fields (hosted by the main window). The decisions are made by the caller's <see cref="PromptDialogRequest.Validate"/>.</summary>
public sealed partial class PromptDialogViewModel : ObservableObject, IPromptDialogService
{
    private PromptDialogRequest? _request;
    private TaskCompletionSource<bool>? _completion;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBody))]
    private string? _body;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubtitle))]
    private string? _subtitle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string? _note;

    [ObservableProperty]
    private string _confirmLabel = string.Empty;

    [ObservableProperty]
    private bool _isDestructive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFirst))]
    private string? _firstLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSecond))]
    private string? _secondLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThird))]
    private string? _thirdLabel;

    /// <summary>The character shown for typed text in the fields; <c>'\0'</c> shows the text itself.</summary>
    [ObservableProperty]
    private char _maskChar;

    [ObservableProperty]
    private char _secondMaskChar;

    [ObservableProperty]
    private char _thirdMaskChar;

    [ObservableProperty]
    private bool _isMono;

    [ObservableProperty]
    private bool _isSecondMono;

    [ObservableProperty]
    private bool _isThirdMono;

    [ObservableProperty]
    private string _first = string.Empty;

    [ObservableProperty]
    private string _second = string.Empty;

    [ObservableProperty]
    private string _third = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    private bool _isChecking;

    public bool HasBody => !string.IsNullOrEmpty(Body);

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public bool HasFirst => FirstLabel is not null;

    public bool HasSecond => SecondLabel is not null;

    public bool HasThird => ThirdLabel is not null;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public Task<bool> ShowAsync(PromptDialogRequest request)
    {
        Finish(accepted: false);

        _request = request;
        Title = request.Title;
        Body = request.Body;
        Subtitle = request.Subtitle;
        Note = request.Note;
        ConfirmLabel = request.ConfirmLabel;
        IsDestructive = request.IsDestructive;
        FirstLabel = request.First?.Label;
        SecondLabel = request.Second?.Label;
        MaskChar = request.First?.Masked == true ? '●' : '\0';
        IsMono = request.First?.Mono == true;
        First = string.Empty;
        Second = string.Empty;
        Error = null;
        IsChecking = false;

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;
        IsOpen = true;
        return completion.Task;
    }

    partial void OnFirstChanged(string value) => Error = null;

    partial void OnSecondChanged(string value) => Error = null;

    partial void OnThirdChanged(string value) => Error = null;

    partial void OnIsCheckingChanged(bool value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private bool CanAct() => !IsChecking;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ConfirmAsync()
    {
        if (_request is not { } request)
        {
            return;
        }

        IsChecking = true;
        string? error = null;
        try
        {
            var first = request.First is null ? string.Empty : First;
            var second = request.Second is null ? string.Empty : Second;
            var third = request.Third is null ? string.Empty : Third;
            if (request.ValidateAll is { } validateAll)
            {
                error = await validateAll(first, second, third);
            }
            else if (request.Validate is { } validate)
            {
                error = await validate(first, second);
            }
        }
        finally
        {
            IsChecking = false;
        }

        if (!ReferenceEquals(_request, request))
        {
            return; // another dialog replaced this one while it was checking
        }

        if (error is null)
        {
            Finish(accepted: true);
        }
        else
        {
            Error = error;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Cancel() => Finish(accepted: false);

    private void Finish(bool accepted)
    {
        var completion = _completion;
        _completion = null;
        _request = null;
        First = string.Empty;
        Second = string.Empty;
        Third = string.Empty;
        Error = null;
        IsOpen = false;
        completion?.TrySetResult(accepted);
    }
}
