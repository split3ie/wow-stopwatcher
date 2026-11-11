using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>
/// The lines of one company's invoice for one month, grouped by day. Notes are edited in place; split, merge and adjust
/// work on the selected lines. Every change goes through <see cref="InvoicingService.EditDay"/> and <see cref="InvoiceLineEditor"/>,
/// then the details are read again. A sent invoice is read-only.
/// </summary>
public sealed partial class InvoiceDetailsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

    private readonly InvoicingService _service;
    private readonly IInvoicingSettings _settings;
    private readonly IDialogService _dialog;
    private readonly TimeProvider _time;
    private readonly string _companyId;
    private readonly InvoicePeriod _period;
    private readonly Action _close;
    private readonly InvoicingSaveDebouncer _saver;
    private readonly Dictionary<(DateOnly Date, int Index), string> _pendingNotes = [];
    private readonly Dictionary<string, string> _colors;
    private InvoiceSummary _summary = new(0, 0, 0, 0, null, 0m, 0m, string.Empty);
    private int _trackedMinutes;
    private string _reportedDefault = string.Empty;
    private bool _loading;
    private bool _disposed;

    public InvoiceDetailsViewModel(
        InvoicingService service,
        ICompanyRepository companies,
        IStopwatchRepository stopwatches,
        IInvoicingSettings settings,
        IDialogService dialog,
        IUiDispatcher dispatcher,
        TimeProvider time,
        string companyId,
        InvoicePeriod period,
        Action close)
    {
        _service = service;
        _settings = settings;
        _dialog = dialog;
        _time = time;
        _companyId = companyId;
        _period = period;
        _close = close;
        _saver = new InvoicingSaveDebouncer(SaveNotes, dispatcher, SaveDelay);
        _colors = stopwatches.GetAll(includeDeleted: true).ToDictionary(s => s.Id, s => s.Color);

        var company = companies.Get(companyId) ?? throw new InvalidOperationException($"Company {companyId} does not exist.");
        Title = company.Name + " · " + InvoicingFormat.Month(period);
        CompanyBrush = new SolidColorBrush(InvoicingColors.Parse(company.Color));
        FormDays = Enumerable.Range(0, period.LastDay.Day)
            .Select(i => period.FirstDay.AddDays(i).ToString(Loc.T("invoicing.details.dayFormat"), Loc.Culture))
            .ToList();
        _amountsHidden = settings.HideAmounts;
        settings.HideAmountsChanged += OnHideAmountsChanged;
        Reload(rebuild: true);
        if (_summary.ReportedMinutes is { } reported)
        {
            ReportedInput = InvoiceTimeText.FormatPadded(reported);
        }
    }

    public string Title { get; }

    public IBrush CompanyBrush { get; }

    public ObservableCollection<InvoiceDetailsDayViewModel> Days { get; } = [];

    public bool IsEditable => !IsSent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable), nameof(SentText), nameof(ShowReportedClear), nameof(CanSaveReported))]
    private bool _isSent;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isPendingStatus;

    [ObservableProperty]
    private string _sentDateText = string.Empty;

    public string SentText => Loc.T("invoicing.details.sentBanner", ("date", SentDateText));

    [ObservableProperty]
    private string _trackedText = string.Empty;

    [ObservableProperty]
    private string _invoicedText = string.Empty;

    [ObservableProperty]
    private string _unallocatedText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AmountText), nameof(AmountLabel), nameof(HideButtonText))]
    private bool _amountsHidden;

    /// <summary>The amount, or the mask while amounts are hidden.</summary>
    public string AmountText => MoneyMask.Apply(InvoicingFormat.Money(_summary.Amount, _summary.Currency), AmountsHidden);

    /// <summary>"Amount · 29 USD/h", with the rate masked while amounts are hidden.</summary>
    public string AmountLabel => Loc.T(
        "invoicing.details.amountLabel",
        ("rate", MoneyMask.Apply(Loc.T("invoicing.details.rate", ("rate", InvoicingFormat.Rate(_summary.Rate)), ("currency", _summary.Currency)), AmountsHidden)));

    public string HideButtonText => AmountsHidden ? Loc.T("invoicing.showAmounts") : Loc.T("invoicing.hideAmounts");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    [ObservableProperty]
    private string _selectionText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReportedInvalid), nameof(CanSaveReported), nameof(ReportedHint))]
    private string _reportedInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReportedClear), nameof(ReportedValueText))]
    private bool _hasReported;

    [ObservableProperty]
    private string _reportedDifferenceText = string.Empty;

    /// <summary>The stored reported time as "h:mm", for the read-only view of a sent invoice.</summary>
    public string ReportedValueText => _summary.ReportedMinutes is { } reported ? InvoiceTimeText.Format(reported) : string.Empty;

    public bool ShowReportedClear => HasReported && IsEditable;

    /// <summary>True when the typed text is not empty and not a valid reported time.</summary>
    public bool ReportedInvalid => ReportedInput.Trim().Length > 0 && !TryParseReported(out _);

    public bool CanSaveReported => IsEditable && ReportedInput.Trim().Length > 0 && !ReportedInvalid;

    public string ReportedHint => ReportedInvalid
        ? Loc.T("invoicing.details.reported.invalid")
        : Loc.T("invoicing.details.reported.hint");

    private bool TryParseReported(out int minutes) =>
        InvoiceTimeText.TryParse(ReportedInput, out minutes) && InvoiceBilling.IsValidReportedMinutes(minutes);

    [RelayCommand]
    private void SaveReported()
    {
        if (!CanSaveReported || !TryParseReported(out var minutes))
        {
            return;
        }

        ApplyReported(minutes);
    }

    [RelayCommand]
    private void ClearReported() => ApplyReported(null);

    private void ApplyReported(int? minutes)
    {
        FlushNotes();
        if (_service.SetReportedMinutes(_companyId, _period, minutes))
        {
            ErrorText = string.Empty;
            Reload(rebuild: false);
            ReportedInput = minutes is { } value ? InvoiceTimeText.FormatPadded(value) : _reportedDefault;
            return;
        }

        ErrorText = Loc.T(IsSent ? "invoicing.details.error.sent" : "invoicing.details.error.failed");
        Reload(rebuild: true);
    }

    [ObservableProperty]
    private bool _canChangeOne;

    [ObservableProperty]
    private bool _canMerge;

    [ObservableProperty]
    private string _mergeHint = string.Empty;

    [ObservableProperty]
    private bool _canDelete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFormOpen), nameof(IsSplitForm), nameof(IsAddForm), nameof(IsMergeDaysForm), nameof(IsTimeForm))]
    private InvoiceDetailsFormKind _formKind;

    public bool IsFormOpen => FormKind != InvoiceDetailsFormKind.None;

    public bool IsSplitForm => FormKind == InvoiceDetailsFormKind.Split;

    public bool IsAddForm => FormKind == InvoiceDetailsFormKind.Add;

    public bool IsMergeDaysForm => FormKind == InvoiceDetailsFormKind.MergeDays;

    /// <summary>True when the open form asks for a time (all forms but merging across days).</summary>
    public bool IsTimeForm => IsFormOpen && !IsMergeDaysForm;

    /// <summary>The days offered by the merge-across-days form, as labels.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _mergeTargets = [];

    /// <summary>Index into <see cref="MergeTargets"/> of the day the merged line goes to.</summary>
    [ObservableProperty]
    private int _mergeTargetIndex;

    private List<DateOnly> _mergeDates = [];

    partial void OnMergeTargetIndexChanged(int value) => RefreshForm();

    /// <summary>The days of the month offered by the add form, as labels.</summary>
    public IReadOnlyList<string> FormDays { get; }

    /// <summary>Index into <see cref="FormDays"/> of the day the custom line is added to.</summary>
    [ObservableProperty]
    private int _formDayIndex;

    /// <summary>The note of the custom line being added.</summary>
    [ObservableProperty]
    private string _formNote = string.Empty;

    [ObservableProperty]
    private string _formTitle = string.Empty;

    [ObservableProperty]
    private string _formLabel = string.Empty;

    [ObservableProperty]
    private string _formInput = string.Empty;

    [ObservableProperty]
    private string _formRest = string.Empty;

    [ObservableProperty]
    private bool _formInvalid = true;

    [ObservableProperty]
    private string _applyText = string.Empty;

    partial void OnFormInputChanged(string value) => RefreshForm();

    partial void OnFormDayIndexChanged(int value) => RefreshForm();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.HideAmountsChanged -= OnHideAmountsChanged;
        _saver.Flush();
    }

    /// <summary>Saves a note that is waiting for the delay to pass (called when a note box loses focus).</summary>
    public void FlushNotes() => _saver.Flush();

    [RelayCommand]
    private void Back()
    {
        _saver.Flush();
        _close();
    }

    [RelayCommand]
    private void ToggleAmounts() => _settings.HideAmounts = !_settings.HideAmounts;

    [RelayCommand]
    private void MarkPending()
    {
        if (_service.MarkPending(_companyId, _period))
        {
            ClearSelection();
            ErrorText = string.Empty;
            Reload(rebuild: true);
        }
    }

    [RelayCommand]
    private void OpenSplit() => OpenForm(InvoiceDetailsFormKind.Split);

    [RelayCommand]
    private void OpenAdjust() => OpenForm(InvoiceDetailsFormKind.Adjust);

    [RelayCommand]
    private void OpenAdd()
    {
        if (!IsEditable)
        {
            return;
        }

        var today = LocalDays.ToLocalDate(_time.GetUtcNow(), _time.LocalTimeZone);
        FormKind = InvoiceDetailsFormKind.Add;
        FormDayIndex = (_period.Contains(today) ? today : _period.FirstDay).Day - 1;
        FormNote = string.Empty;
        FormInput = InvoiceTimeText.FormatShort(0);
        RefreshForm();
    }

    [RelayCommand]
    private void CancelForm() => FormKind = InvoiceDetailsFormKind.None;

    [RelayCommand]
    private void DeleteCustom()
    {
        var selected = SelectedLines();
        if (!CanDelete || selected.Count == 0)
        {
            return;
        }

        _saver.Flush();
        var result = EditDayResult.Saved;
        foreach (var day in selected.GroupBy(l => l.Date))
        {
            var indices = day.Select(l => l.Index).OrderDescending().ToList();
            try
            {
                var dayResult = _service.EditDay(
                    _companyId,
                    _period,
                    day.Key,
                    lines => indices.Aggregate(lines, InvoiceLineEditor.DeleteCustom));
                if (dayResult != EditDayResult.Saved)
                {
                    result = dayResult;
                }
            }
            catch (ArgumentException)
            {
                result = EditDayResult.Unchanged;
            }
        }

        Report(result);
        ClearSelection();
        Reload(rebuild: true);
    }

    [RelayCommand]
    private void ApplyForm()
    {
        if (FormKind == InvoiceDetailsFormKind.MergeDays)
        {
            ApplyMergeDays();
            return;
        }

        if (FormInvalid || !InvoiceTimeText.TryParse(FormInput, out var minutes))
        {
            return;
        }

        if (FormKind == InvoiceDetailsFormKind.Add)
        {
            var date = _period.FirstDay.AddDays(FormDayIndex);
            var note = FormNote;
            Edit(date, lines => InvoiceLineEditor.AddCustom(lines, date, minutes, note));
            return;
        }

        if (SelectedLines().SingleOrDefault() is not { } line)
        {
            return;
        }

        if (FormKind == InvoiceDetailsFormKind.Split)
        {
            Edit(line.Date, lines => InvoiceLineEditor.Split(lines, line.Index, minutes));
        }
        else if (FormKind == InvoiceDetailsFormKind.Adjust)
        {
            Edit(line.Date, lines => InvoiceLineEditor.Adjust(lines, line.Index, minutes));
        }
    }

    [RelayCommand]
    private void Merge()
    {
        var selected = SelectedLines();
        if (!CanMerge || selected.Count < 2)
        {
            return;
        }

        if (selected.Any(l => l.Date != selected[0].Date))
        {
            _mergeDates = selected.Select(l => l.Date).Distinct().Order().ToList();
            MergeTargets = _mergeDates
                .Select(d => Days.First(day => day.Date == d).Label)
                .ToList();
            FormKind = InvoiceDetailsFormKind.MergeDays;
            MergeTargetIndex = 0;
            RefreshForm();
            return;
        }

        var indices = selected.Select(l => l.Index).ToList();
        Edit(selected[0].Date, lines => InvoiceLineEditor.Merge(lines, indices));
    }

    private void ApplyMergeDays()
    {
        var selected = SelectedLines();
        if (FormInvalid || MergeTargetIndex < 0 || MergeTargetIndex >= _mergeDates.Count)
        {
            return;
        }

        _saver.Flush();
        var result = _service.MergeDays(
            _companyId,
            _period,
            selected.Select(l => (l.Date, l.Index)).ToList(),
            _mergeDates[MergeTargetIndex]);
        Report(result);
        ClearSelection();
        Reload(rebuild: true);
    }

    internal void ApplyDrift(InvoiceDetailsDayViewModel day)
    {
        _saver.Flush();
        Report(_service.ApplyDrift(_companyId, _period, day.Date));
        ClearSelection();
        Reload(rebuild: true);
    }

    internal void ResetDay(InvoiceDetailsDayViewModel day)
    {
        _saver.Flush();
        _dialog.Show(
            Loc.T("invoicing.details.reset.title"),
            Loc.T("invoicing.details.reset.body", ("day", day.Label)),
            Loc.T("invoicing.details.reset.confirm"),
            () =>
            {
                Report(_service.ResetDay(_companyId, _period, day.Date));
                ClearSelection();
                Reload(rebuild: true);
            });
    }

    internal void NoteEdited(InvoiceDetailsLineViewModel line)
    {
        if (_loading || !IsEditable)
        {
            return;
        }

        _pendingNotes[(line.Date, line.Index)] = line.Note;
        _saver.Schedule();
    }

    internal void SelectionChanged()
    {
        if (_loading)
        {
            return;
        }

        if (FormKind != InvoiceDetailsFormKind.Add)
        {
            FormKind = InvoiceDetailsFormKind.None;
        }

        RefreshToolbar();
    }

    private List<InvoiceDetailsLineViewModel> SelectedLines() =>
        Days.SelectMany(d => d.Lines).Where(l => l.IsSelected).ToList();

    private void ClearSelection()
    {
        _loading = true;
        foreach (var line in Days.SelectMany(d => d.Lines))
        {
            line.IsSelected = false;
        }

        _loading = false;
        FormKind = InvoiceDetailsFormKind.None;
        RefreshToolbar();
    }

    private void RefreshToolbar()
    {
        var selected = SelectedLines();
        SelectionText = selected.Count > 0
            ? Loc.T("invoicing.details.selected", ("count", selected.Count))
            : Loc.T("invoicing.details.selectLines");
        CanChangeOne = IsEditable && selected.Count == 1;
        var neighbours = selected.Count > 0 && InvoiceLineEditor.AreNeighbouringDays(selected.Select(l => l.Date));
        var anyCustom = selected.Any(l => l.IsCustom);
        CanMerge = IsEditable && selected.Count >= 2 && neighbours && !anyCustom;
        CanDelete = IsEditable && selected.Count > 0 && selected.All(l => l.IsCustom);
        MergeHint = selected.Count >= 2 && anyCustom
            ? Loc.T("invoicing.details.mergeCustom")
            : selected.Count >= 2 && !neighbours
                ? Loc.T("invoicing.details.mergeNeighbours")
                : Loc.T("invoicing.details.mergeSelect");
        RefreshForm();
    }

    private void OpenForm(InvoiceDetailsFormKind kind)
    {
        if (!CanChangeOne || SelectedLines().SingleOrDefault() is not { } line)
        {
            return;
        }

        FormKind = kind;
        FormInput = InvoiceTimeText.FormatShort(kind == InvoiceDetailsFormKind.Split ? line.Minutes / 2 : line.Minutes);
        RefreshForm();
    }

    private void RefreshForm()
    {
        if (FormKind == InvoiceDetailsFormKind.Add)
        {
            FormInvalid = !InvoiceTimeText.TryParse(FormInput, out var added) || added <= 0
                || FormDayIndex < 0 || FormDayIndex >= FormDays.Count;
            FormTitle = Loc.T("invoicing.details.add.title");
            FormLabel = Loc.T("invoicing.details.add.label");
            ApplyText = Loc.T("invoicing.details.add.apply");
            FormRest = FormInvalid ? Loc.T("invoicing.details.adjust.invalid") : string.Empty;
            return;
        }

        if (FormKind == InvoiceDetailsFormKind.MergeDays)
        {
            FormInvalid = MergeTargetIndex < 0 || MergeTargetIndex >= _mergeDates.Count;
            FormTitle = Loc.T("invoicing.details.mergeDays.title");
            FormLabel = Loc.T("invoicing.details.mergeDays.label");
            ApplyText = Loc.T("invoicing.details.mergeDays.apply");
            FormRest = Loc.T("invoicing.details.mergeDays.hint");
            return;
        }

        if (FormKind == InvoiceDetailsFormKind.None || SelectedLines().SingleOrDefault() is not { } line)
        {
            FormInvalid = true;
            return;
        }

        var parsed = InvoiceTimeText.TryParse(FormInput, out var minutes);
        var time = InvoiceTimeText.Format(line.Minutes);
        if (FormKind == InvoiceDetailsFormKind.Split)
        {
            FormInvalid = !parsed || minutes <= 0 || minutes >= line.Minutes;
            FormTitle = Loc.T("invoicing.details.split.title", ("time", time));
            FormLabel = Loc.T("invoicing.details.split.label");
            ApplyText = Loc.T("invoicing.details.split.apply");
            FormRest = FormInvalid
                ? Loc.T("invoicing.details.split.invalid", ("time", time))
                : Loc.T("invoicing.details.split.second", ("time", InvoiceTimeText.Format(line.Minutes - minutes)));
        }
        else
        {
            FormInvalid = !parsed || minutes <= 0;
            FormTitle = Loc.T("invoicing.details.adjust.title", ("time", time));
            FormLabel = Loc.T("invoicing.details.adjust.label");
            ApplyText = Loc.T("invoicing.details.adjust.apply");
            var tracked = Days.FirstOrDefault(d => d.Date == line.Date)?.TrackedMinutes ?? 0;
            FormRest = FormInvalid
                ? Loc.T("invoicing.details.adjust.invalid")
                : Loc.T("invoicing.details.adjust.tracked", ("time", InvoiceTimeText.Format(tracked)));
        }
    }

    private void Edit(DateOnly date, Func<IReadOnlyList<InvoiceLine>, IReadOnlyList<InvoiceLine>> edit)
    {
        _saver.Flush();
        EditDayResult result;
        try
        {
            result = _service.EditDay(_companyId, _period, date, edit);
        }
        catch (ArgumentException)
        {
            result = EditDayResult.Unchanged;
        }

        Report(result);
        ClearSelection();
        Reload(rebuild: true);
    }

    private void Report(EditDayResult result) => ErrorText = result switch
    {
        EditDayResult.Saved => string.Empty,
        EditDayResult.InvoiceSent => Loc.T("invoicing.details.error.sent"),
        _ => Loc.T("invoicing.details.error.failed"),
    };

    private void SaveNotes()
    {
        if (_pendingNotes.Count == 0)
        {
            return;
        }

        var pending = _pendingNotes.ToList();
        _pendingNotes.Clear();
        var rebuild = false;
        foreach (var day in pending.GroupBy(p => p.Key.Date))
        {
            try
            {
                var result = _service.EditDay(
                    _companyId,
                    _period,
                    day.Key,
                    lines => day.Aggregate(lines, (current, p) => InvoiceLineEditor.SetNote(current, p.Key.Index, p.Value)));
                if (result != EditDayResult.Saved)
                {
                    Report(result);
                    rebuild = true;
                }
            }
            catch (ArgumentException)
            {
                Report(EditDayResult.Unchanged);
                rebuild = true;
            }
        }

        Reload(rebuild);
    }

    private void OnHideAmountsChanged(object? sender, EventArgs e) => AmountsHidden = _settings.HideAmounts;

    /// <summary>Reads the invoice again. Without <paramref name="rebuild"/> and when the lines are the same, only values change, so typing is not interrupted.</summary>
    private void Reload(bool rebuild)
    {
        _loading = true;
        try
        {
            var invoice = _service.GetInvoice(_companyId, _period);
            IsSent = invoice?.Status == InvoiceStatus.Sent;
            IsPendingStatus = !IsSent && invoice?.FilePath is not null;
            StatusText = IsSent
                ? Loc.T("invoicing.status.sent")
                : Loc.T(IsPendingStatus ? "invoicing.status.pending" : "invoicing.status.draft");
            SentDateText = invoice?.SentUtc is { } sent
                ? TimeZoneInfo.ConvertTime(sent, _time.LocalTimeZone).ToString("d MMMM yyyy", Loc.Culture)
                : string.Empty;
            OnPropertyChanged(nameof(SentText));

            _summary = _service.GetSummary(_companyId, _period);
            var days = _service.GetDays(_companyId, _period);
            var ranges = _service.GetDayRanges(_companyId, _period).ToDictionary(r => r.Date);
            _trackedMinutes = days.Sum(d => d.TrackedMinutes);
            TrackedText = InvoicingFormat.Duration(_trackedMinutes);
            InvoicedText = InvoicingFormat.Duration(_summary.InvoicedMinutes);
            if (_summary.ReportedMinutes is null)
            {
                var invoicedDefault = InvoiceBilling.IsValidReportedMinutes(_summary.InvoicedMinutes)
                    ? InvoiceTimeText.FormatPadded(_summary.InvoicedMinutes)
                    : string.Empty;
                if (ReportedInput.Length == 0 || ReportedInput == _reportedDefault)
                {
                    ReportedInput = invoicedDefault;
                }

                _reportedDefault = invoicedDefault;
            }

            HasReported = _summary.ReportedMinutes is not null;
            ReportedDifferenceText = _summary.ReportedMinutes is { } reportedMinutes
                ? InvoicingFormat.ReportedDifference(reportedMinutes, _trackedMinutes)
                : string.Empty;
            OnPropertyChanged(nameof(ReportedValueText));
            UnallocatedText = InvoicingFormat.Duration(_summary.UnallocatedMinutes);
            OnPropertyChanged(nameof(AmountText));
            OnPropertyChanged(nameof(AmountLabel));

            if (rebuild || !SameShape(days))
            {
                Rebuild(days, ranges);
            }
            else
            {
                for (var i = 0; i < days.Count; i++)
                {
                    Days[i].Apply(days[i], ranges.GetValueOrDefault(days[i].Date), RunningNames(days[i]), IsEditable);
                    for (var j = 0; j < days[i].Lines.Count; j++)
                    {
                        Days[i].Lines[j].UpdateTime(days[i].Lines[j].Minutes);
                    }
                }
            }
        }
        finally
        {
            _loading = false;
        }

        RefreshToolbar();
    }

    private bool SameShape(IReadOnlyList<InvoiceDay> days) =>
        days.Count == Days.Count
        && days.Select((d, i) => d.Date == Days[i].Date
            && d.Lines.Count == Days[i].Lines.Count
            && d.Lines.Select((l, j) => Days[i].Lines[j].Matches(l)).All(match => match)).All(same => same);

    private void Rebuild(IReadOnlyList<InvoiceDay> days, IReadOnlyDictionary<DateOnly, InvoiceDayRange> ranges)
    {
        Days.Clear();
        var today = LocalDays.ToLocalDate(_time.GetUtcNow(), _time.LocalTimeZone);
        foreach (var day in days)
        {
            var label = DayLabel(day.Date, today);
            var dayViewModel = new InvoiceDetailsDayViewModel(this, day.Date, label);
            for (var i = 0; i < day.Lines.Count; i++)
            {
                dayViewModel.Lines.Add(new InvoiceDetailsLineViewModel(this, day.Lines[i], i, label, DotBrush(day.Lines[i]), IsEditable));
            }

            dayViewModel.Apply(day, ranges.GetValueOrDefault(day.Date), RunningNames(day), IsEditable);
            Days.Add(dayViewModel);
        }
    }

    private IReadOnlyList<string> RunningNames(InvoiceDay day) =>
        day.HasRunningSession ? _service.GetRunningStopwatchNames(_companyId, day.Date) : [];

    private static string DayLabel(DateOnly date, DateOnly today)
    {
        var text = date.ToString(Loc.T("invoicing.details.dayFormat"), Loc.Culture);
        text = text.Length == 0 ? text : char.ToUpper(text[0], Loc.Culture) + text[1..];
        return date == today ? Loc.T("invoicing.details.today", ("day", text)) : text;
    }

    private IBrush DotBrush(InvoiceLine line)
    {
        var colors = line.Sources.Select(s => InvoicingColors.Parse(_colors.GetValueOrDefault(s.StopwatchId))).ToList();
        if (colors.Count < 2)
        {
            return new SolidColorBrush(colors.Count == 0 ? InvoicingColors.Parse(null) : colors[0]);
        }

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(colors[0], 0),
                new GradientStop(colors[0], 0.5),
                new GradientStop(colors[1], 0.5),
                new GradientStop(colors[1], 1),
            },
        };
    }
}
