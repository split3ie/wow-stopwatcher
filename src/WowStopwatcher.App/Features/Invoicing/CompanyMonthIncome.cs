namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>What one company earned in the current month, in its own currency.</summary>
public sealed record CompanyMonthIncome(string Name, string Color, decimal Amount, string Currency);
