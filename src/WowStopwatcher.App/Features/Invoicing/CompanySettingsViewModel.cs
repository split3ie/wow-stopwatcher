using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml.Packaging;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>
/// The "Company settings" card of one company: name, color, rate, currency, billed stopwatches, template, file name, destination and
/// the PDF converter status. Text edits are saved shortly after typing; everything else at once.
/// </summary>
public sealed partial class CompanySettingsViewModel : ObservableObject
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly InvoicingDependencies _d;
    private readonly string _companyId;
    private readonly Action _saved;
    private readonly Action _deleted;
    private readonly InvoicingSaveDebouncer _saver;
    private bool _loading;

    internal CompanySettingsViewModel(InvoicingDependencies dependencies, Company company, bool amountsHidden, Action saved, Action deleted)
    {
        _d = dependencies;
        _companyId = company.Id;
        _saved = saved;
        _deleted = deleted;
        _amountsHidden = amountsHidden;
        _saver = new InvoicingSaveDebouncer(SaveNow, dependencies.Dispatcher, SaveDelay);

        _loading = true;
        _name = company.Name;
        _colorHex = company.Color;
        _rateText = InvoicingFormat.Rate(company.Rate);
        _currency = company.Currency;
        _fileNamePattern = company.FileNamePattern;
        _askDestination = company.AskDestination;
        _outputFolder = company.OutputFolder ?? string.Empty;
        _loading = false;

        LoadSwatches();
        LoadChips();
        LoadTemplate(company);
        LoadConverter();
        UpdateFileExample();
    }

    /// <summary>The currencies offered in the list; any other code can be typed.</summary>
    public IReadOnlyList<string> Currencies { get; } = ["USD", "EUR", "GBP", "UAH", "PLN", "CHF"];

    public ObservableCollection<InvoicingSwatchViewModel> Swatches { get; } = [];

    public ObservableCollection<StopwatchChipViewModel> Chips { get; } = [];

    public bool HasChips => Chips.Count > 0;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorName))]
    [NotifyPropertyChangedFor(nameof(ColorBrush))]
    private string _colorHex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateDisplay))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _rateText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _currency;

    [ObservableProperty]
    private string _fileNamePattern;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolderEnabled))]
    private bool _askDestination;

    [ObservableProperty]
    private string _outputFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateDisplay))]
    [NotifyPropertyChangedFor(nameof(IsRateReadOnly))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _amountsHidden;

    [ObservableProperty]
    private bool _hasRateError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTemplate))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _templateName = string.Empty;

    [ObservableProperty]
    private string _templateInfo = string.Empty;

    [ObservableProperty]
    private string _fileExample = string.Empty;

    [ObservableProperty]
    private string _converterText = string.Empty;

    [ObservableProperty]
    private bool _isConverterFound;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _messageText = string.Empty;

    public IBrush ColorBrush => new SolidColorBrush(InvoicingColors.Parse(ColorHex));

    public bool HasMessage => MessageText.Length > 0;

    public bool IsFolderEnabled => !AskDestination;

    public bool IsRateReadOnly => AmountsHidden;

    public bool HasTemplate => TemplateName.Length > 0;

    public string ColorName => Swatches.FirstOrDefault(s => s.IsSelected)?.Name ?? ColorHex;

    /// <summary>The rate field text: the mask while amounts are hidden; typing is only possible while they are shown.</summary>
    public string RateDisplay
    {
        get => AmountsHidden ? MoneyMask.Mask : RateText;
        set
        {
            if (!AmountsHidden)
            {
                RateText = value;
            }
        }
    }

    /// <summary>One line shown when the card is collapsed: stopwatch count, rate and template.</summary>
    public string Summary => string.Join(
        " · ",
        Loc.Plural("invoicing.settings.stopwatchCount", Chips.Count(c => c.IsAssigned)),
        AmountsHidden ? MoneyMask.Mask : $"{RateText} {Currency}/h",
        HasTemplate ? TemplateName : Loc.T("invoicing.settings.noTemplate"));

    /// <summary>Called when the global "hide amounts" switch changes.</summary>
    internal void SetAmountsHidden(bool hidden) => AmountsHidden = hidden;

    /// <summary>Saves an edit that is still waiting.</summary>
    internal void Flush() => _saver.Flush();

    /// <summary>Writes the fields to the company now (the template fields stay as stored).</summary>
    public void SaveNow()
    {
        if (_d.Companies.Get(_companyId) is not { } existing)
        {
            return;
        }

        var rate = existing.Rate;
        HasRateError = false;
        if (!AmountsHidden)
        {
            if (InvoicingFormat.TryParseDecimal(RateText, out var parsed))
            {
                rate = parsed;
            }
            else
            {
                HasRateError = true;
            }
        }

        _d.Companies.Update(existing with
        {
            Name = string.IsNullOrWhiteSpace(Name) ? existing.Name : Name.Trim(),
            Color = ColorHex,
            Rate = rate,
            Currency = string.IsNullOrWhiteSpace(Currency) ? existing.Currency : Currency.Trim().ToUpperInvariant(),
            FileNamePattern = FileNamePattern,
            AskDestination = AskDestination,
            OutputFolder = string.IsNullOrWhiteSpace(OutputFolder) ? null : OutputFolder.Trim(),
        });
        UpdateFileExample();
        _saved();
    }

    partial void OnNameChanged(string value) => Edited();

    partial void OnRateTextChanged(string value) => Edited();

    partial void OnCurrencyChanged(string value) => Edited();

    partial void OnFileNamePatternChanged(string value) => Edited();

    partial void OnAskDestinationChanged(bool value) => Edited();

    partial void OnOutputFolderChanged(string value) => Edited();

    private void Edited()
    {
        if (!_loading)
        {
            _saver.Schedule();
        }
    }

    internal void PickColor(string hex)
    {
        ColorHex = hex;
        foreach (var swatch in Swatches)
        {
            swatch.IsSelected = string.Equals(swatch.Hex, hex, StringComparison.OrdinalIgnoreCase);
        }

        OnPropertyChanged(nameof(ColorName));
        _saver.Schedule();
        _saver.Flush();
    }

    private void LoadSwatches()
    {
        Swatches.Clear();
        foreach (var (name, hex) in InvoicingColors.Palette)
        {
            Swatches.Add(new InvoicingSwatchViewModel(name, hex, string.Equals(hex, ColorHex, StringComparison.OrdinalIgnoreCase), PickColor));
        }

        OnPropertyChanged(nameof(ColorName));
    }

    private void LoadChips()
    {
        var owners = _d.Companies.GetStopwatchOwners();
        var assigned = _d.Companies.GetAssignedStopwatchIds(_companyId);
        var companyNames = _d.Companies.GetAll(includeDeleted: true).ToDictionary(c => c.Id, c => c.Name);

        _loading = true;
        Chips.Clear();
        foreach (var stopwatch in _d.Stopwatches.GetAll(includeDeleted: true))
        {
            var isAssigned = assigned.Contains(stopwatch.Id);
            if (stopwatch.IsDeleted && !owners.ContainsKey(stopwatch.Id))
            {
                continue;
            }

            string? other = null;
            if (!isAssigned && owners.TryGetValue(stopwatch.Id, out var ownerId))
            {
                other = companyNames.GetValueOrDefault(ownerId, ownerId);
            }

            Chips.Add(new StopwatchChipViewModel(stopwatch, isAssigned, other, OnChipChanged));
        }

        _loading = false;
        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(Summary));
    }

    private void OnChipChanged(StopwatchChipViewModel chip)
    {
        if (_loading)
        {
            return;
        }

        var ids = Chips.Where(c => c.IsAssigned).Select(c => c.StopwatchId).ToList();
        try
        {
            _d.Companies.SetAssignedStopwatches(_companyId, ids);
            MessageText = string.Empty;
        }
        catch (InvalidOperationException)
        {
            // Another company took the stopwatch in the meantime: show the stored state again.
            LoadChips();
            MessageText = Loc.T("invoicing.settings.stopwatchTaken");
            return;
        }

        OnPropertyChanged(nameof(Summary));
        _saved();
    }

    private void LoadTemplate(Company company)
    {
        TemplateName = company.TemplateName ?? string.Empty;
        TemplateInfo = string.Empty;
        if (company.TemplateFile is not { Length: > 0 } file)
        {
            return;
        }

        var path = _d.Templates.Resolve(file);
        if (!File.Exists(path))
        {
            TemplateInfo = Loc.T("invoicing.template.missing");
            return;
        }

        _ = LoadTemplateInfoAsync(path);
    }

    private async Task LoadTemplateInfoAsync(string path)
    {
        try
        {
            var count = await Task.Run(() => _d.Scanner.Scan(path).Count);
            TemplateInfo = TemplateInfoText(count);
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            TemplateInfo = Loc.T("invoicing.template.unreadable");
        }
    }

    private static string TemplateInfoText(int count) =>
        Loc.T("invoicing.template.copyKept") + " · " + Loc.Plural("invoicing.template.found", count);

    private static bool IsUnreadable(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException or OpenXmlPackageException;

    private void LoadConverter()
    {
        var name = _d.Converter.Name;
        IsConverterFound = name is not null;
        ConverterText = name is null
            ? Loc.T("invoicing.converter.none")
            : Loc.T("invoicing.converter.found", ("name", name));
    }

    private void UpdateFileExample()
    {
        var period = InvoicePeriod.From(LocalDays.ToLocalDate(_d.Time.GetUtcNow(), _d.Time.LocalTimeZone));
        try
        {
            FileExample = Loc.T(
                "invoicing.settings.fileExample",
                ("month", InvoicingFormat.Month(period)),
                ("name", _d.Service.SuggestFileName(_companyId, period)));
        }
        catch (InvalidOperationException)
        {
            FileExample = string.Empty;
        }
    }

    [RelayCommand]
    private async Task PickTemplateAsync()
    {
        var path = await _d.Picker.PickOpenFileAsync(
            Loc.T("invoicing.template.pickTitle"),
            Loc.T("invoicing.template.filter"),
            ["docx"]);
        if (path is null)
        {
            return;
        }

        int count;
        StoredTemplate stored;
        try
        {
            count = await Task.Run(() => _d.Scanner.Scan(path).Count);
            stored = _d.Templates.Import(_companyId, path);
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            MessageText = Loc.T("invoicing.template.unreadable");
            return;
        }

        if (_d.Companies.Get(_companyId) is not { } company)
        {
            return;
        }

        _d.Companies.Update(company with { TemplateFile = stored.FileName, TemplateName = stored.OriginalName, TemplateSha256 = stored.Sha256 });
        TemplateName = stored.OriginalName;
        TemplateInfo = TemplateInfoText(count);
        MessageText = string.Empty;
        _saved();
    }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        var folder = await _d.Picker.PickFolderAsync(Loc.T("invoicing.settings.folderPickTitle"));
        if (folder is not null)
        {
            OutputFolder = folder;
            _saver.Flush();
        }
    }

    [RelayCommand]
    private void Delete() => _d.Dialog.Show(
        Loc.T("invoicing.company.deleteTitle", ("name", Name)),
        Loc.T("invoicing.company.deleteBody"),
        Loc.T("invoicing.company.deleteConfirm"),
        () =>
        {
            _d.Companies.SoftDelete(_companyId);
            _deleted();
        });
}
