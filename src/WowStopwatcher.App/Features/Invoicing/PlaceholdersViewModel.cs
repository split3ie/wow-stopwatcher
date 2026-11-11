using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>
/// The placeholders table of one company. Edits are saved shortly after typing, but only while every alias is valid and unique;
/// results come from the sample invoice and are re-evaluated after each save.
/// </summary>
public sealed partial class PlaceholdersViewModel : ObservableObject
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan CopiedNoteDuration = TimeSpan.FromSeconds(2);

    private readonly InvoicingDependencies _d;
    private readonly string _companyId;
    private readonly Func<decimal> _sampleHours;
    private int _copyVersion;
    private readonly InvoicingSaveDebouncer _saver;
    private bool _loading;
    private int _version;

    internal PlaceholdersViewModel(InvoicingDependencies dependencies, string companyId, Func<decimal> sampleHours, bool amountsHidden)
    {
        _d = dependencies;
        _companyId = companyId;
        _sampleHours = sampleHours;
        _amountsHidden = amountsHidden;
        _saver = new InvoicingSaveDebouncer(() => SaveNow(), dependencies.Dispatcher, SaveDelay);

        _loading = true;
        foreach (var definition in _d.Companies.GetPlaceholders(companyId))
        {
            Rows.Add(new PlaceholderRowViewModel(this, definition));
        }

        _loading = false;
        _ = RunAsync();
    }

    public ObservableCollection<PlaceholderRowViewModel> Rows { get; } = [];

    /// <summary>Warnings about the template, for example a marker nothing defines.</summary>
    public ObservableCollection<string> Warnings { get; } = [];

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasRows => Rows.Count > 0;

    /// <summary>The built-in names, shown as chips.</summary>
    public IReadOnlyList<string> BuiltinNames { get; } = InvoiceBuiltins.All;

    [ObservableProperty]
    private bool _amountsHidden;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>A short note that a built-in name was copied; empty when there is nothing to show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCopiedNote))]
    private string _copiedNote = string.Empty;

    public bool HasCopiedNote => CopiedNote.Length > 0;

    [ObservableProperty]
    private bool _hasErrors;

    partial void OnAmountsHiddenChanged(bool value)
    {
        foreach (var row in Rows)
        {
            row.RefreshMask();
        }
    }

    internal void SetAmountsHidden(bool hidden) => AmountsHidden = hidden;

    internal void Flush() => _saver.Flush();

    internal void RowEdited()
    {
        if (!_loading)
        {
            _saver.Schedule();
        }
    }

    internal void Remove(PlaceholderRowViewModel row)
    {
        Rows.Remove(row);
        OnPropertyChanged(nameof(HasRows));
        _saver.Schedule();
    }

    /// <summary>Copies the marker (<c>{{name}}</c>) of a built-in name to the clipboard and shows a note for a moment.</summary>
    [RelayCommand]
    private async Task CopyBuiltinAsync(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var marker = "{{" + name + "}}";
        if (!await _d.Clipboard.SetTextAsync(marker))
        {
            return;
        }

        CopiedNote = Loc.T("invoicing.placeholders.copied", ("name", marker));
        var version = ++_copyVersion;
        _ = Task.Delay(CopiedNoteDuration, _d.Time).ContinueWith(
            _ => _d.Dispatcher.Post(() =>
            {
                if (version == _copyVersion)
                {
                    CopiedNote = string.Empty;
                }
            }),
            TaskScheduler.Default);
    }

    [RelayCommand]
    private void Add()
    {
        var used = Rows.Select(r => r.Alias.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = 1;
        while (used.Contains("placeholder" + number))
        {
            number++;
        }

        Rows.Add(new PlaceholderRowViewModel(this, new PlaceholderDefinition(0, _companyId, "placeholder" + number, PlaceholderKind.Static, string.Empty, false, Rows.Count)));
        OnPropertyChanged(nameof(HasRows));
        _saver.Schedule();
    }

    /// <summary>
    /// Adds a row for every marker that is not defined yet (ignoring case) and has a valid alias, then saves:
    /// an automatic row for an invoice-level built-in (not a line marker), an empty static row for any other marker.
    /// </summary>
    internal bool AddMissing(IEnumerable<string> markers)
    {
        var used = Rows.Select(r => r.NormalizedAlias).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = false;
        foreach (var marker in markers)
        {
            var isBuiltin = InvoiceBuiltins.IsBuiltin(marker);
            if (PlaceholderAlias.Validate(marker) is not null
                || (!isBuiltin && InvoiceBuiltins.Canonical(marker) is not null)
                || !used.Add(marker))
            {
                continue;
            }

            var kind = isBuiltin ? PlaceholderKind.Automatic : PlaceholderKind.Static;
            Rows.Add(new PlaceholderRowViewModel(this, new PlaceholderDefinition(0, _companyId, marker, kind, string.Empty, false, Rows.Count)));
            added = true;
        }

        if (added)
        {
            OnPropertyChanged(nameof(HasRows));
            SaveNow();
        }

        return added;
    }

    /// <summary>Saves the rows when every alias is valid and unique; otherwise shows the errors and saves nothing. Returns whether it saved.</summary>
    public bool SaveNow()
    {
        if (!Validate())
        {
            return false;
        }

        if (_d.Companies.Get(_companyId) is null)
        {
            return false;
        }

        _d.Companies.SavePlaceholders(_companyId, Rows.Select((r, i) => r.ToDefinition(_companyId, i)).ToList());
        _ = RunAsync();
        return true;
    }

    private bool Validate()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var valid = true;
        foreach (var row in Rows)
        {
            var code = PlaceholderAlias.Validate(row.Alias);
            if (code is not null)
            {
                row.AliasError = InvoicingMessages.Alias(code);
            }
            else if (!seen.Add(row.NormalizedAlias))
            {
                row.AliasError = Loc.T("invoicing.alias.duplicate");
            }
            else
            {
                row.AliasError = string.Empty;
            }

            valid &= row.AliasError.Length == 0;
        }

        HasErrors = !valid;
        return valid;
    }

    /// <summary>Saves pending edits, then evaluates every placeholder for the sample invoice (off the UI thread) and checks the template for unknown markers.</summary>
    internal async Task RunAsync()
    {
        _saver.Flush();
        var version = ++_version;
        var hours = _sampleHours();
        IsBusy = true;
        try
        {
            var (results, warnings) = await Task.Run(() => (
                _d.Service.ResolvePlaceholdersForSample(_companyId, hours),
                _d.Service.GetTemplateWarnings(_companyId)));
            if (version != _version)
            {
                return;
            }

            // Every valid undefined marker gets a row. A successful save starts a newer run that replaces this one.
            if (AddMissing(warnings.Markers) && version != _version)
            {
                return;
            }

            foreach (var row in Rows)
            {
                if (!results.TryGetValue(row.Alias.Trim(), out var result))
                {
                    row.SetResult(string.Empty, false);
                }
                else if (result.IsOk)
                {
                    row.SetResult(result.Value ?? string.Empty, false);
                }
                else
                {
                    row.SetResult(InvoicingMessages.Script(result.ErrorCode, result.Error), true);
                }
            }

            Warnings.Clear();
            var defined = Rows.Select(r => r.Alias.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var marker in warnings.UnknownMarkers.Where(m => !defined.Contains(m)))
            {
                Warnings.Add(Loc.T("invoicing.placeholders.unknown", ("name", marker)));
            }

            OnPropertyChanged(nameof(HasWarnings));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // The company was deleted or the template cannot be read: leave the results as they were.
        }
        finally
        {
            if (version == _version)
            {
                IsBusy = false;
            }
        }
    }
}
