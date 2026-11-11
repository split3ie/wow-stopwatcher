namespace WowStopwatcher.Core.Invoicing;

/// <summary>Storage of companies, their billed stopwatches and placeholders.</summary>
public interface ICompanyRepository
{
    /// <summary>All companies ordered by sort order; deleted ones only when <paramref name="includeDeleted"/>.</summary>
    IReadOnlyList<Company> GetAll(bool includeDeleted);

    /// <summary>The company with <paramref name="id"/>, or null.</summary>
    Company? Get(string id);

    /// <summary>Adds a company.</summary>
    void Insert(Company company);

    /// <summary>Updates all fields of the company with the same id.</summary>
    void Update(Company company);

    /// <summary>Marks the company as deleted and releases its assigned stopwatches (so another company can bill them); its invoices and placeholders are kept.</summary>
    void SoftDelete(string id);

    /// <summary>Ids of the stopwatches billed to <paramref name="companyId"/>.</summary>
    IReadOnlySet<string> GetAssignedStopwatchIds(string companyId);

    /// <summary>For every assigned stopwatch, the company it belongs to (a stopwatch belongs to at most one).</summary>
    IReadOnlyDictionary<string, string> GetStopwatchOwners();

    /// <summary>Replaces the company's assigned stopwatches.</summary>
    /// <exception cref="InvalidOperationException">A stopwatch already belongs to another company; nothing is changed.</exception>
    void SetAssignedStopwatches(string companyId, IReadOnlyCollection<string> stopwatchIds);

    /// <summary>The company's placeholders ordered by sort order.</summary>
    IReadOnlyList<PlaceholderDefinition> GetPlaceholders(string companyId);

    /// <summary>Replaces all placeholders of the company.</summary>
    void SavePlaceholders(string companyId, IReadOnlyList<PlaceholderDefinition> placeholders);
}
