namespace WowStopwatcher.Core.Invoicing;

/// <summary>Storage of invoices and their stored days. Every method that changes data is one transaction.</summary>
public interface IInvoiceRepository
{
    /// <summary>All existing invoices of the company.</summary>
    IReadOnlyList<Invoice> GetForCompany(string companyId);

    /// <summary>The invoice for the month, or null when no row exists yet.</summary>
    Invoice? Get(string companyId, InvoicePeriod period);

    /// <summary>The invoice for the month; a new one is created as <see cref="InvoiceStatus.Pending"/>.</summary>
    Invoice GetOrCreate(string companyId, InvoicePeriod period);

    /// <summary>The stored days of the invoice with their lines.</summary>
    IReadOnlyList<StoredInvoiceDay> GetStoredDays(long invoiceId);

    /// <summary>Stores the day (replacing the day and its lines if present).</summary>
    void ReplaceDay(long invoiceId, StoredInvoiceDay day);

    /// <summary>Stores all the days in one transaction (replacing each day and its lines if present); a day without lines stays as an empty stored day.</summary>
    void ReplaceDays(long invoiceId, IReadOnlyList<StoredInvoiceDay> days);

    /// <summary>Removes the stored day and its lines, so the day is live again.</summary>
    void DeleteDay(long invoiceId, DateOnly date);

    /// <summary>Records a generated file. The invoice number and the company's counter are not touched.</summary>
    void MarkGenerated(long invoiceId, string filePath, DateTimeOffset generatedUtc, string inputHash);

    /// <summary>
    /// Marks the invoice as sent, stores <paramref name="daysToFreeze"/> and the snapshot. An invoice without a number gets the company's
    /// next invoice number and the counter is incremented, in the same transaction; an invoice with a number keeps it.
    /// </summary>
    void MarkSent(long invoiceId, DateTimeOffset sentUtc, string snapshotJson, IReadOnlyList<StoredInvoiceDay> daysToFreeze);

    /// <summary>Makes the invoice pending again: deletes stored days with Edited=false and their lines; clears the sent time and snapshot.</summary>
    void MarkPending(long invoiceId);

    /// <summary>Sets the reported time of the invoice (minutes, greater than 0), or clears it with null. Status and stored days are not touched.</summary>
    void SetReportedMinutes(long invoiceId, int? minutes);
}
