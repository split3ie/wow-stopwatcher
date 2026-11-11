using System.Text.Json;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>
/// The application layer of Invoicing: lists months and days, edits days, resolves placeholders, makes previews and files,
/// and marks invoices as sent or pending. Sessions are never changed. View models call this and format nothing themselves.
/// </summary>
public sealed class InvoicingService
{
    private const int PreviewCacheSize = 8;

    private readonly ICompanyRepository _companies;
    private readonly IInvoiceRepository _invoices;
    private readonly ISessionRepository _sessions;
    private readonly IStopwatchRepository _stopwatches;
    private readonly IScriptEvaluator _evaluator;
    private readonly DocxTemplateScanner _scanner;
    private readonly InvoicePdfBuilder _builder;
    private readonly InvoiceTemplateStore _templates;
    private readonly PdfPreviewRenderer _renderer;
    private readonly PdfConverterSelector _converter;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, byte[]> _previewCache = [];
    private readonly object _previewLock = new();

    public InvoicingService(
        ICompanyRepository companies,
        IInvoiceRepository invoices,
        ISessionRepository sessions,
        IStopwatchRepository stopwatches,
        IScriptEvaluator evaluator,
        DocxTemplateScanner scanner,
        InvoicePdfBuilder builder,
        InvoiceTemplateStore templates,
        PdfPreviewRenderer renderer,
        PdfConverterSelector converter,
        TimeProvider time)
    {
        _companies = companies;
        _invoices = invoices;
        _sessions = sessions;
        _stopwatches = stopwatches;
        _evaluator = evaluator;
        _scanner = scanner;
        _builder = builder;
        _templates = templates;
        _renderer = renderer;
        _converter = converter;
        _time = time;
    }

    /// <summary>
    /// One row per month with billed time or an invoice row, newest first.
    /// </summary>
    public IReadOnlyList<InvoiceMonthRow> GetMonths(string companyId)
    {
        var company = Require(companyId);
        var zone = _time.LocalTimeZone;
        var current = InvoicePeriod.From(Today());
        var invoices = _invoices.GetForCompany(companyId).ToDictionary(i => i.Period);
        var periods = _sessions.GetLocalDays(zone).Select(InvoicePeriod.From).Concat(invoices.Keys).Distinct().ToList();

        var rows = new List<InvoiceMonthRow>();
        foreach (var period in periods)
        {
            invoices.TryGetValue(period, out var invoice);
            var days = DaysFor(company, period, invoice);
            var minutes = InvoiceBilling.BilledMinutes(days);
            var unallocated = InvoiceBilling.UnallocatedMinutes(days);
            if (minutes == 0 && unallocated == 0 && invoice is null)
            {
                continue;
            }

            var snapshot = invoice?.Status == InvoiceStatus.Sent ? ParseSnapshot(invoice.SnapshotJson) : null;
            var amount = snapshot is not null ? snapshot.Amount : InvoiceMath.Amount(minutes, company.Rate);
            var status = invoice is null || (invoice.Status == InvoiceStatus.Pending && invoice.FilePath is null)
                ? InvoiceMonthStatus.Draft
                : invoice.Status == InvoiceStatus.Sent ? InvoiceMonthStatus.Sent : InvoiceMonthStatus.Pending;
            var fileExists = invoice?.FilePath is { } path && File.Exists(path);
            rows.Add(new InvoiceMonthRow(period, invoice, minutes, unallocated, amount, snapshot?.Currency ?? company.Currency, status, fileExists, period == current));
        }

        return rows.OrderByDescending(r => r.Period.Year).ThenByDescending(r => r.Period.Month).ToList();
    }

    /// <summary>
    /// The days of the invoice: pending shows live days combined with stored ones; sent shows only the stored days
    /// (their tracked time still comes from sessions, for display).
    /// </summary>
    public IReadOnlyList<InvoiceDay> GetDays(string companyId, InvoicePeriod period) =>
        DaysFor(Require(companyId), period, _invoices.Get(companyId, period));

    /// <summary>The lines of one day plus the time <paramref name="stopwatchId"/> was tracked that day and whether the invoice is sent.</summary>
    public InvoiceTimeDay GetStopwatchDay(string companyId, InvoicePeriod period, DateOnly date, string stopwatchId)
    {
        var company = Require(companyId);
        var invoice = _invoices.Get(companyId, period);
        var lines = DaysFor(company, period, invoice).FirstOrDefault(d => d.Date == date)?.Lines ?? [];
        var tracked = LiveDays(company, period).FirstOrDefault(d => d.Date == date)?.Lines
            .Where(l => l.Sources.Count == 1 && l.Sources[0].StopwatchId == stopwatchId)
            .Sum(l => l.Minutes) ?? 0;
        return new InvoiceTimeDay(lines, tracked, invoice?.Status == InvoiceStatus.Sent);
    }

    /// <summary>Billed and unallocated minutes and the amount of the invoice.</summary>
    public InvoiceSummary GetSummary(string companyId, InvoicePeriod period)
    {
        var company = Require(companyId);
        var invoice = _invoices.Get(companyId, period);
        var days = DaysFor(company, period, invoice);
        var billed = InvoiceBilling.BilledMinutes(days);
        var frozen = invoice?.Status == InvoiceStatus.Sent ? ParseSnapshot(invoice.SnapshotJson) : null;
        var rate = frozen?.Rate ?? company.Rate;
        return new InvoiceSummary(billed, InvoiceBilling.UnallocatedMinutes(days), InvoiceMath.Amount(billed, rate), rate, frozen?.Currency ?? company.Currency);
    }

    /// <summary>The invoice row of the month; null until the first edit, generation or mark as sent.</summary>
    public Invoice? GetInvoice(string companyId, InvoicePeriod period)
    {
        Require(companyId);
        return _invoices.Get(companyId, period);
    }

    /// <summary>Names of the billed stopwatches that are running now and started on <paramref name="date"/>.</summary>
    public IReadOnlyList<string> GetRunningStopwatchNames(string companyId, DateOnly date)
    {
        var assigned = _companies.GetAssignedStopwatchIds(Require(companyId).Id);
        var names = _stopwatches.GetAll(includeDeleted: true).ToDictionary(s => s.Id, s => s.Name);
        return _sessions.GetOpen()
            .Where(s => assigned.Contains(s.StopwatchId) && LocalDays.ToLocalDate(s.StartUtc, _time.LocalTimeZone) == date)
            .Select(s => names.TryGetValue(s.StopwatchId, out var name) ? name : s.StopwatchId)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Changes one day: <paramref name="edit"/> gets the day's current lines and returns the new ones. Only this day is stored
    /// (as edited). Rejected when the invoice is sent. Editor exceptions (<see cref="ArgumentException"/>) are not caught.
    /// </summary>
    public EditDayResult EditDay(
        string companyId,
        InvoicePeriod period,
        DateOnly date,
        Func<IReadOnlyList<InvoiceLine>, IReadOnlyList<InvoiceLine>> edit)
    {
        var company = Require(companyId);
        var existing = _invoices.Get(companyId, period);
        if (existing?.Status == InvoiceStatus.Sent)
        {
            return EditDayResult.InvoiceSent;
        }

        var current = DaysFor(company, period, existing).FirstOrDefault(d => d.Date == date)?.Lines ?? [];
        var updated = edit(current);
        if (updated.Any(l => l.Minutes <= 0 || l.Date != date))
        {
            return EditDayResult.Unchanged;
        }

        var invoice = existing ?? _invoices.GetOrCreate(companyId, period);
        _invoices.ReplaceDay(invoice.Id, new StoredInvoiceDay(date, true, updated));
        return EditDayResult.Saved;
    }

    /// <summary>Drops the stored lines of the day, so it follows the tracked time again.</summary>
    public EditDayResult ResetDay(string companyId, InvoicePeriod period, DateOnly date)
    {
        Require(companyId);
        var invoice = _invoices.Get(companyId, period);
        if (invoice is null)
        {
            return EditDayResult.Unchanged;
        }

        if (invoice.Status == InvoiceStatus.Sent)
        {
            return EditDayResult.InvoiceSent;
        }

        _invoices.DeleteDay(invoice.Id, date);
        return EditDayResult.Saved;
    }

    /// <summary>Adds the difference between tracked and invoiced minutes of an edited day to its last line.</summary>
    public EditDayResult ApplyDrift(string companyId, InvoicePeriod period, DateOnly date)
    {
        var company = Require(companyId);
        var invoice = _invoices.Get(companyId, period);
        if (invoice?.Status == InvoiceStatus.Sent)
        {
            return EditDayResult.InvoiceSent;
        }

        var day = DaysFor(company, period, invoice).FirstOrDefault(d => d.Date == date);
        if (invoice is null || day is null || !day.Edited || day.DriftMinutes == 0 || day.Lines.Count == 0)
        {
            return EditDayResult.Unchanged;
        }

        var lines = day.Lines.ToList();
        var last = lines[^1];
        var minutes = last.Minutes + day.DriftMinutes;
        if (minutes <= 0)
        {
            return EditDayResult.Unchanged;
        }

        lines[^1] = last with { Minutes = minutes };
        _invoices.ReplaceDay(invoice.Id, new StoredInvoiceDay(date, true, lines));
        return EditDayResult.Saved;
    }

    /// <summary>Resolves every placeholder of the company for the invoice of <paramref name="period"/> (frozen values when sent).</summary>
    public IReadOnlyDictionary<string, PlaceholderResult> ResolvePlaceholders(string companyId, InvoicePeriod period)
    {
        var company = Require(companyId);
        var invoice = _invoices.Get(companyId, period);
        return BuildInputs(company, period, invoice, DaysFor(company, period, invoice), invoice?.Number).Placeholders;
    }

    /// <summary>Resolves the placeholders for the current month with one synthetic line of <paramref name="sampleHours"/>.</summary>
    public IReadOnlyDictionary<string, PlaceholderResult> ResolvePlaceholdersForSample(string companyId, decimal sampleHours) =>
        BuildSampleInputs(Require(companyId), sampleHours).Placeholders;

    /// <summary>Compares the markers of the company's template with the built-in names and the defined placeholders.</summary>
    public TemplateWarnings GetTemplateWarnings(string companyId)
    {
        var company = Require(companyId);
        var path = TemplatePath(company);
        var defined = _companies.GetPlaceholders(companyId).Select(p => p.Alias).ToList();
        if (path is null || !File.Exists(path))
        {
            return new TemplateWarnings(false, [], [], defined);
        }

        var markers = _scanner.Scan(path);
        var unknown = markers.Where(m => !InvoiceBuiltins.IsBuiltin(m) && !defined.Contains(m)).ToList();
        var unused = defined.Where(a => !markers.Contains(a)).ToList();
        return new TemplateWarnings(true, markers, unknown, unused);
    }

    /// <summary>The suggested file name for the invoice (invoice number: the assigned one, else the next one).</summary>
    public string SuggestFileName(string companyId, InvoicePeriod period)
    {
        var company = Require(companyId);
        var invoice = _invoices.Get(companyId, period);
        var inputs = BuildInputs(company, period, invoice, DaysFor(company, period, invoice), invoice?.Number ?? company.NextInvoiceNumber);
        return InvoiceFileName.Render(company.FileNamePattern, name => inputs.Values.GetValueOrDefault(name));
    }

    /// <summary>
    /// Makes the first page of a sample invoice (current month, one line of <paramref name="sampleHours"/>) as PNG.
    /// Results are cached by input hash.
    /// </summary>
    public async Task<PreviewResult> BuildPreviewAsync(string companyId, decimal sampleHours, CancellationToken cancellationToken)
    {
        var company = Require(companyId);
        var inputs = BuildSampleInputs(company, sampleHours);
        var blocks = Blockers(company, inputs, out var templatePath);
        if (blocks.Count > 0 || templatePath is null)
        {
            return new PreviewResult(null, blocks);
        }

        var hash = Hash(inputs, company);
        lock (_previewLock)
        {
            if (_previewCache.TryGetValue(hash, out var cached))
            {
                return new PreviewResult(cached);
            }
        }

        string? pdf = null;
        try
        {
            pdf = await _builder.BuildAsync(templatePath, inputs.Values, LineValues(inputs), cancellationToken);
            var png = _renderer.RenderFirstPage(pdf);
            lock (_previewLock)
            {
                if (_previewCache.Count >= PreviewCacheSize)
                {
                    _previewCache.Clear();
                }

                _previewCache[hash] = png;
            }

            return new PreviewResult(png);
        }
        catch (PdfConversionException ex)
        {
            return new PreviewResult(null, PdfError: ex.Code, Detail: ex.Message);
        }
        finally
        {
            if (pdf is not null)
            {
                InvoicePdfBuilder.DeleteTemp(pdf);
            }
        }
    }

    /// <summary>
    /// Makes the PDF and moves it to <paramref name="destinationPath"/> (the caller already confirmed replacing an existing file),
    /// then records it with the input hash. Only allocated time (lines with a note) is billed; with none, nothing is generated. Blocked, with reasons, when a used placeholder has an error or the template or a converter is missing.
    /// </summary>
    public async Task<GenerateResult> GenerateAsync(string companyId, InvoicePeriod period, string destinationPath, CancellationToken cancellationToken)
    {
        var company = Require(companyId);
        var invoice = _invoices.Get(companyId, period);
        var inputs = BuildInputs(company, period, invoice, DaysFor(company, period, invoice), invoice?.Number ?? company.NextInvoiceNumber);
        var blocks = Blockers(company, inputs, out var templatePath);
        if (blocks.Count > 0 || templatePath is null)
        {
            return new GenerateResult(GenerateOutcome.Blocked, Blocks: blocks);
        }

        if (inputs.Minutes == 0)
        {
            return new GenerateResult(GenerateOutcome.NothingAllocated);
        }

        string? pdf = null;
        try
        {
            pdf = await _builder.BuildAsync(templatePath, inputs.Values, LineValues(inputs), cancellationToken);
            try
            {
                var folder = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                File.Move(pdf, destinationPath, overwrite: true);
            }
            catch (UnauthorizedAccessException)
            {
                return new GenerateResult(GenerateOutcome.FileInUse);
            }
            catch (IOException ex) when (File.Exists(destinationPath))
            {
                return new GenerateResult(GenerateOutcome.FileInUse, Detail: ex.Message);
            }
            catch (IOException ex)
            {
                return new GenerateResult(GenerateOutcome.Failed, Detail: ex.Message);
            }

            invoice ??= _invoices.GetOrCreate(companyId, period);
            var number = _invoices.MarkGenerated(invoice.Id, destinationPath, _time.GetUtcNow(), Hash(inputs, company));
            return new GenerateResult(GenerateOutcome.Generated, destinationPath, number);
        }
        catch (PdfConversionException ex)
        {
            return new GenerateResult(GenerateOutcome.Failed, PdfError: ex.Code, Detail: ex.Message);
        }
        finally
        {
            if (pdf is not null)
            {
                InvoicePdfBuilder.DeleteTemp(pdf);
            }
        }
    }

    /// <summary>
    /// Freezes the invoice: stores a snapshot and every live day. If lines or values changed since the file was generated and
    /// <paramref name="confirmChanged"/> is false, nothing is changed and <see cref="MarkSentResult.ChangedSinceGeneration"/> is returned.
    /// </summary>
    public MarkSentResult MarkSent(string companyId, InvoicePeriod period, bool confirmChanged)
    {
        var company = Require(companyId);
        var existing = _invoices.Get(companyId, period);
        if (existing?.Status == InvoiceStatus.Sent)
        {
            return MarkSentResult.AlreadySent;
        }

        var days = DaysFor(company, period, existing);
        var inputs = BuildInputs(company, period, existing, days, existing?.Number ?? company.NextInvoiceNumber);
        if (!confirmChanged && existing?.GeneratedInputHash is { } generated && generated != Hash(inputs, company))
        {
            return MarkSentResult.ChangedSinceGeneration;
        }

        var invoice = existing ?? _invoices.GetOrCreate(companyId, period);
        var snapshot = new InvoiceSnapshot(
            inputs.Rate,
            inputs.Currency,
            inputs.Minutes,
            InvoiceMath.Amount(inputs.Minutes, inputs.Rate),
            inputs.Values.Where(p => !InvoiceBuiltins.IsBuiltin(p.Key)).ToDictionary(p => p.Key, p => p.Value),
            invoice.FilePath is null ? null : Path.GetFileName(invoice.FilePath),
            company.TemplateSha256);
        var toFreeze = days.Where(d => !d.Edited).Select(d => new StoredInvoiceDay(d.Date, false, d.Lines)).ToList();
        _invoices.MarkSent(invoice.Id, _time.GetUtcNow(), JsonSerializer.Serialize(snapshot), toFreeze);
        return MarkSentResult.Sent;
    }

    /// <summary>Makes a sent invoice editable again; days that were not edited go live. Returns false when it was not sent.</summary>
    public bool MarkPending(string companyId, InvoicePeriod period)
    {
        Require(companyId);
        var invoice = _invoices.Get(companyId, period);
        if (invoice is null || invoice.Status != InvoiceStatus.Sent)
        {
            return false;
        }

        _invoices.MarkPending(invoice.Id);
        return true;
    }

    private Company Require(string companyId) =>
        _companies.Get(companyId) ?? throw new InvalidOperationException($"Company {companyId} does not exist.");

    private DateOnly Today() => LocalDays.ToLocalDate(_time.GetUtcNow(), _time.LocalTimeZone);

    private string? TemplatePath(Company company) =>
        company.TemplateFile is { Length: > 0 } file ? _templates.Resolve(file) : null;

    private IReadOnlyList<InvoiceDay> DaysFor(Company company, InvoicePeriod period, Invoice? invoice)
    {
        var stored = invoice is null ? [] : _invoices.GetStoredDays(invoice.Id);
        var live = LiveDays(company, period);
        var days = InvoiceReconciler.Compose(live, stored);
        if (invoice?.Status == InvoiceStatus.Sent)
        {
            var storedDates = stored.Select(d => d.Date).ToHashSet();
            days = days.Where(d => storedDates.Contains(d.Date)).ToList();
        }

        return days;
    }

    private IReadOnlyList<InvoiceDay> LiveDays(Company company, InvoicePeriod period)
    {
        var zone = _time.LocalTimeZone;
        var (from, to) = period.RangeUtc(zone);
        var names = _stopwatches.GetAll(includeDeleted: true).ToDictionary(s => s.Id, s => s.Name);
        return InvoiceLineBuilder.Build(
            _sessions.GetInRange(from, to),
            names,
            _companies.GetAssignedStopwatchIds(company.Id),
            period,
            zone,
            _time.GetUtcNow());
    }

    private Inputs BuildSampleInputs(Company company, decimal sampleHours)
    {
        var today = Today();
        var period = InvoicePeriod.From(today);
        var minutes = Math.Max(1, (int)Math.Round(sampleHours * 60m, MidpointRounding.AwayFromZero));
        var line = new InvoiceLine(today, minutes, "Sample", [new InvoiceLineSource("sample", "Sample")]);
        var day = new InvoiceDay(today, false, [line], minutes, false);
        return BuildInputs(company, period, null, [day], null);
    }

    private Inputs BuildInputs(Company company, InvoicePeriod period, Invoice? invoice, IReadOnlyList<InvoiceDay> days, int? number)
    {
        var culture = Loc.Culture;
        var lines = InvoiceBilling.BilledLines(days);
        var minutes = lines.Sum(l => l.Minutes);
        var frozen = invoice?.Status == InvoiceStatus.Sent ? ParseSnapshot(invoice.SnapshotJson) : null;
        var rate = frozen?.Rate ?? company.Rate;
        var currency = frozen?.Currency ?? company.Currency;
        var amount = InvoiceMath.Amount(minutes, rate);

        IReadOnlyDictionary<string, PlaceholderResult> placeholders;
        if (frozen is not null)
        {
            placeholders = frozen.Values.ToDictionary(p => p.Key, p => new PlaceholderResult(p.Value, null));
        }
        else
        {
            var context = new ScriptContext(
                InvoiceMath.Hours(minutes),
                minutes,
                rate,
                amount,
                currency,
                period.Key,
                period.FirstDay,
                period.LastDay,
                period.FirstDay.ToString("MMMM yyyy", culture),
                invoice?.Number,
                lines,
                _ => null);
            placeholders = PlaceholderResolver.Resolve(_companies.GetPlaceholders(company.Id), context, _evaluator);
        }

        var values = InvoiceBuiltins.Create(minutes, rate, currency, company.Name, period, number, Today(), culture);
        foreach (var (alias, result) in placeholders)
        {
            if (result.IsOk && !InvoiceBuiltins.IsBuiltin(alias))
            {
                values[alias] = result.Value ?? string.Empty;
            }
        }

        return new Inputs(lines, minutes, rate, currency, placeholders, values, culture);
    }

    private List<GenerateBlockReason> Blockers(Company company, Inputs inputs, out string? templatePath)
    {
        var blocks = new List<GenerateBlockReason>();
        templatePath = TemplatePath(company);
        if (templatePath is null)
        {
            blocks.Add(new GenerateBlockReason(GenerateBlockKind.NoTemplate));
        }
        else if (!File.Exists(templatePath))
        {
            blocks.Add(new GenerateBlockReason(GenerateBlockKind.TemplateMissing));
        }

        if (!_converter.IsAvailable)
        {
            blocks.Add(new GenerateBlockReason(GenerateBlockKind.NoConverter));
        }

        if (templatePath is not null && File.Exists(templatePath))
        {
            foreach (var marker in _scanner.Scan(templatePath))
            {
                if (inputs.Placeholders.TryGetValue(marker, out var result) && !result.IsOk)
                {
                    blocks.Add(new GenerateBlockReason(GenerateBlockKind.PlaceholderError, marker, result.Error, result.ErrorCode));
                }
            }
        }

        return blocks;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> LineValues(Inputs inputs) =>
        inputs.Lines.Select(l => (IReadOnlyDictionary<string, string>)InvoiceBuiltins.CreateLine(l, inputs.Rate, inputs.Culture)).ToList();

    // "today" is left out so a file does not count as changed just because the day passed.
    private static string Hash(Inputs inputs, Company company) =>
        InvoiceInputHash.Compute(
            inputs.Lines,
            inputs.Values.Where(p => p.Key != InvoiceBuiltins.Today).ToDictionary(p => p.Key, p => (string?)p.Value),
            inputs.Rate,
            inputs.Currency,
            company.TemplateSha256);

    private static InvoiceSnapshot? ParseSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InvoiceSnapshot>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Inputs(
        IReadOnlyList<InvoiceLine> Lines,
        int Minutes,
        decimal Rate,
        string Currency,
        IReadOnlyDictionary<string, PlaceholderResult> Placeholders,
        Dictionary<string, string> Values,
        System.Globalization.CultureInfo Culture);
}
