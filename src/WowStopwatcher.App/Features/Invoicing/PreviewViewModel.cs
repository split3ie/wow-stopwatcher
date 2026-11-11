using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>
/// The preview of a sample invoice (current month, one line of the sample hours). The conversion runs off the UI thread.
/// While amounts are hidden the preview is not made and the page shows a cover instead, because the invoice contains the rate and amounts.
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject
{
    private readonly InvoicingDependencies _d;
    private readonly string _companyId;
    private readonly Action _refreshed;
    private readonly Action _showAmounts;
    private CancellationTokenSource? _cancel;

    internal PreviewViewModel(InvoicingDependencies dependencies, string companyId, bool amountsHidden, Action refreshed, Action showAmounts)
    {
        _d = dependencies;
        _companyId = companyId;
        _amountsHidden = amountsHidden;
        _refreshed = refreshed;
        _showAmounts = showAmounts;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private string _sampleHoursText = "1";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCoverVisible))]
    [NotifyPropertyChangedFor(nameof(IsImageVisible))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _amountsHidden;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHintVisible))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(IsImageVisible))]
    [NotifyPropertyChangedFor(nameof(IsHintVisible))]
    private Bitmap? _image;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsHintVisible))]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private string _info = string.Empty;

    public bool HasImage => Image is not null;

    public bool HasError => ErrorText.Length > 0;

    /// <summary>True while amounts are hidden: the page covers the preview.</summary>
    public bool IsCoverVisible => AmountsHidden;

    public bool IsImageVisible => !AmountsHidden && HasImage;

    /// <summary>True when there is nothing to show yet and no error: the page says to press Refresh.</summary>
    public bool IsHintVisible => !AmountsHidden && !HasImage && !HasError && !IsBusy;

    /// <summary>The sample hours; 1 when the text is not a number.</summary>
    public decimal SampleHours => InvoicingFormat.TryParseDecimal(SampleHoursText, out var hours) ? hours : 1m;

    internal void SetAmountsHidden(bool hidden)
    {
        AmountsHidden = hidden;
        if (hidden)
        {
            _cancel?.Cancel();
            Image = null;
            Info = string.Empty;
        }
    }

    /// <summary>Drops the shown preview after the company changed; Refresh makes a new one.</summary>
    internal void ClearForCompanyChange()
    {
        if (Image is not null)
        {
            Image = null;
            Info = string.Empty;
        }
    }

    internal void Cancel() => _cancel?.Cancel();

    [RelayCommand]
    private void ShowAmounts() => _showAmounts();

    private bool CanRefresh() => !AmountsHidden && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!InvoicingFormat.TryParseDecimal(SampleHoursText, out var hours))
        {
            ErrorText = Loc.T("invoicing.preview.badHours");
            return;
        }

        _refreshed();
        _cancel?.Cancel();
        var cancel = _cancel = new CancellationTokenSource();
        ErrorText = string.Empty;
        Info = string.Empty;
        IsBusy = true;
        try
        {
            var clock = Stopwatch.StartNew();
            var converter = _d.Converter.Name;
            var result = await Task.Run(() => _d.Service.BuildPreviewAsync(_companyId, hours, cancel.Token), cancel.Token);
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            if (result.Png is { } png)
            {
                using var stream = new MemoryStream(png);
                Image = new Bitmap(stream);
                Info = converter is null
                    ? string.Empty
                    : Loc.T("invoicing.preview.converted", ("name", converter), ("seconds", (clock.Elapsed.TotalSeconds).ToString("0.0", Loc.Culture)));
            }
            else
            {
                Image = null;
                ErrorText = result.Blocks is { Count: > 0 } blocks
                    ? InvoicingMessages.Blocks(blocks)
                    : InvoicingMessages.Pdf(result.PdfError, result.Detail);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer refresh or leaving the page cancelled this one.
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Image = null;
            ErrorText = ex.Message;
        }
        finally
        {
            if (cancel == _cancel)
            {
                IsBusy = false;
            }
        }
    }
}
