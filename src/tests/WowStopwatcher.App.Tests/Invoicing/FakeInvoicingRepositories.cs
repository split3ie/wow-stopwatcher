using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Tests.Invoicing;

internal sealed class FakeSettingsRepository : ISettingsRepository
{
    private readonly Dictionary<string, string> _values = [];

    public string? Get(string key) => _values.GetValueOrDefault(key);

    public void Set(string key, string value) => _values[key] = value;
}

internal sealed class FakeCompanyRepository : ICompanyRepository
{
    public List<Company> Companies { get; } = [];

    public Dictionary<string, HashSet<string>> Assigned { get; } = [];

    public Dictionary<string, List<PlaceholderDefinition>> Placeholders { get; } = [];

    public IReadOnlyList<Company> GetAll(bool includeDeleted) => Companies.Where(c => includeDeleted || !c.IsDeleted).ToList();

    public Company? Get(string id) => Companies.FirstOrDefault(c => c.Id == id);

    public void Insert(Company company) => Companies.Add(company);

    public void Update(Company company) => Companies[Companies.FindIndex(c => c.Id == company.Id)] = company;

    public void SoftDelete(string id) => Update(Companies.First(c => c.Id == id) with { IsDeleted = true });

    public IReadOnlySet<string> GetAssignedStopwatchIds(string companyId) => Assigned.GetValueOrDefault(companyId) ?? [];

    public IReadOnlyDictionary<string, string> GetStopwatchOwners() =>
        Assigned.SelectMany(p => p.Value.Select(s => (Stopwatch: s, Company: p.Key))).ToDictionary(x => x.Stopwatch, x => x.Company);

    public void SetAssignedStopwatches(string companyId, IReadOnlyCollection<string> stopwatchIds) =>
        Assigned[companyId] = stopwatchIds.ToHashSet();

    public IReadOnlyList<PlaceholderDefinition> GetPlaceholders(string companyId) =>
        Placeholders.GetValueOrDefault(companyId) ?? [];

    public void SavePlaceholders(string companyId, IReadOnlyList<PlaceholderDefinition> placeholders) =>
        Placeholders[companyId] = placeholders.ToList();
}

internal sealed class FakeInvoiceRepository : IInvoiceRepository
{
    private readonly FakeCompanyRepository _companies;
    private readonly List<Invoice> _invoices = [];
    private readonly Dictionary<long, List<StoredInvoiceDay>> _days = [];
    private long _nextId = 1;

    public FakeInvoiceRepository(FakeCompanyRepository companies) => _companies = companies;

    public IReadOnlyList<Invoice> GetForCompany(string companyId) => _invoices.Where(i => i.CompanyId == companyId).ToList();

    public Invoice? Get(string companyId, InvoicePeriod period) =>
        _invoices.FirstOrDefault(i => i.CompanyId == companyId && i.Period == period);

    public Invoice GetOrCreate(string companyId, InvoicePeriod period)
    {
        var existing = Get(companyId, period);
        if (existing is not null)
        {
            return existing;
        }

        var created = new Invoice(_nextId++, companyId, period, InvoiceStatus.Pending, null, null, null, null, null, null);
        _invoices.Add(created);
        _days[created.Id] = [];
        return created;
    }

    public IReadOnlyList<StoredInvoiceDay> GetStoredDays(long invoiceId) => _days.GetValueOrDefault(invoiceId)?.ToList() ?? [];

    public void ReplaceDay(long invoiceId, StoredInvoiceDay day)
    {
        var days = _days[invoiceId];
        days.RemoveAll(d => d.Date == day.Date);
        days.Add(day);
    }

    public void ReplaceDays(long invoiceId, IReadOnlyList<StoredInvoiceDay> days)
    {
        foreach (var day in days)
        {
            ReplaceDay(invoiceId, day);
        }
    }

    public void DeleteDay(long invoiceId, DateOnly date) => _days[invoiceId].RemoveAll(d => d.Date == date);

    public void MarkGenerated(long invoiceId, string filePath, DateTimeOffset generatedUtc, string inputHash) =>
        Replace(Find(invoiceId) with { FilePath = filePath, GeneratedUtc = generatedUtc, GeneratedInputHash = inputHash });

    public void MarkSent(long invoiceId, DateTimeOffset sentUtc, string snapshotJson, IReadOnlyList<StoredInvoiceDay> daysToFreeze)
    {
        foreach (var day in daysToFreeze)
        {
            ReplaceDay(invoiceId, day);
        }

        var invoice = Find(invoiceId);
        var number = invoice.Number;
        if (number is null)
        {
            var company = _companies.Get(invoice.CompanyId)!;
            number = company.NextInvoiceNumber;
            _companies.Update(company with { NextInvoiceNumber = number.Value + 1 });
        }

        Replace(invoice with { Number = number, Status = InvoiceStatus.Sent, SentUtc = sentUtc, SnapshotJson = snapshotJson });
    }

    public void SetReportedMinutes(long invoiceId, int? minutes) => Replace(Find(invoiceId) with { ReportedMinutes = minutes });

    public void MarkPending(long invoiceId)
    {
        _days[invoiceId].RemoveAll(d => !d.Edited);
        Replace(Find(invoiceId) with { Status = InvoiceStatus.Pending, SentUtc = null, SnapshotJson = null });
    }

    private Invoice Find(long id) => _invoices.First(i => i.Id == id);

    private void Replace(Invoice invoice) => _invoices[_invoices.FindIndex(i => i.Id == invoice.Id)] = invoice;
}
