using System.Diagnostics;
using WowStopwatcher.App.Features.Invoicing;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Tests.Invoicing;

public class JintScriptEvaluatorTests
{
    private static ScriptContext Context(Func<string, string?>? placeholder = null) => new(
        Hours: 3.5m,
        Minutes: 210,
        Rate: 40m,
        Amount: 140m,
        Currency: "EUR",
        PeriodMonth: "2026-11",
        PeriodStart: new DateOnly(2026, 11, 1),
        PeriodEnd: new DateOnly(2026, 11, 30),
        PeriodName: "November 2026",
        InvoiceNumber: 7,
        Lines:
        [
            new InvoiceLine(new DateOnly(2026, 11, 2), 90, "first", [new InvoiceLineSource("a", "English"), new InvoiceLineSource("b", "Walking")]),
            new InvoiceLine(new DateOnly(2026, 11, 3), 120, string.Empty, [new InvoiceLineSource("a", "English")]),
        ],
        Placeholder: placeholder ?? (_ => null));

    private static string Eval(string script, ScriptContext? context = null) =>
        new JintScriptEvaluator().Evaluate(script, context ?? Context());

    [Fact]
    public void Exposes_numbers_and_text()
    {
        Assert.Equal("3.5|210|40|140|EUR", Eval("return [hours, minutes, rate, amount, currency].join('|')"));
        Assert.Equal("2026-11|2026-11-01|2026-11-30|November 2026|7", Eval("return [period.month, period.start, period.end, period.name, invoice.number].join('|')"));
    }

    [Fact]
    public void Exposes_lines()
    {
        Assert.Equal("2", Eval("return lines.length"));
        Assert.Equal("2026-11-02|English, Walking|90|1.5|first", Eval("var l = lines[0]; return [l.date, l.stopwatch, l.minutes, l.hours, l.note].join('|')"));
    }

    [Fact]
    public void Missing_invoice_number_is_null()
    {
        var context = Context() with { InvoiceNumber = null };
        Assert.Equal("true", Eval("return invoice.number === null", context));
    }

    [Fact]
    public void Format_without_pattern_uses_company_date_format_or_culture_short_date()
    {
        var uk = new System.Globalization.CultureInfo("uk-UA");
        Assert.Equal("10/12/2026", Eval("return format('2026-12-10')", Context() with { Culture = uk, DateFormat = "dd/MM/yyyy" }));
        Assert.Equal("10.12.2026", Eval("return format('2026-12-10')", Context() with { Culture = uk, DateFormat = null }));
        Assert.Equal("10 Dec 2026", Eval("return format('2026-12-10', 'dd MMM yyyy')", Context() with { Culture = uk, DateFormat = "dd/MM/yyyy" }));
        Assert.Equal("abc", Eval("return format('abc')"));
        Assert.Equal("1.5", Eval("return format(1.5)"));
    }

    [Fact]
    public void Format_formats_numbers_and_dates_with_invariant_culture()
    {
        Assert.Equal("140.00", Eval("return format(amount, '0.00')"));
        Assert.Equal("1,234.50", Eval("return format(1234.5, '#,##0.00')"));
        Assert.Equal("02 Nov 2026", Eval("return format(lines[0].date, 'dd MMM yyyy')"));
    }

    [Fact]
    public void Ph_returns_other_placeholder_or_null()
    {
        var context = Context(alias => alias == "client" ? "ACME" : null);
        Assert.Equal("ACME|true", Eval("return ph('client') + '|' + (ph('nope') === null)", context));
    }

    [Fact]
    public void Null_and_undefined_become_empty_text()
    {
        Assert.Equal(string.Empty, Eval("return null"));
        Assert.Equal(string.Empty, Eval("var x = 1;"));
    }

    [Fact]
    public void Infinite_loop_is_stopped()
    {
        var watch = Stopwatch.StartNew();
        var ex = Assert.Throws<ScriptEvaluationException>(() => Eval("while (true) {}"));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.True(ex.Code is ScriptErrorCode.Timeout or ScriptErrorCode.Limit);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Deep_recursion_is_stopped()
    {
        Assert.Equal(ScriptErrorCode.Limit, Assert.Throws<ScriptEvaluationException>(() => Eval("function f() { return f(); } return f();")).Code);
    }

    [Fact]
    public void Syntax_error_has_a_readable_message()
    {
        var ex = Assert.Throws<ScriptEvaluationException>(() => Eval("return ("));
        Assert.Equal(ScriptErrorCode.Syntax, ex.Code);
    }

    [Fact]
    public void Runtime_error_has_a_readable_message()
    {
        var ex = Assert.Throws<ScriptEvaluationException>(() => Eval("throw new Error('boom')"));
        Assert.Equal(ScriptErrorCode.Runtime, ex.Code);
        Assert.Contains("boom", ex.Message);
        var undefinedVariable = Assert.Throws<ScriptEvaluationException>(() => Eval("return missing + 1"));
        Assert.Contains("missing", undefinedVariable.Message);
    }

    [Fact]
    public void Strict_mode_rejects_undeclared_assignment()
    {
        Assert.Throws<ScriptEvaluationException>(() => Eval("x = 5; return x"));
    }

    [Fact]
    public void Dotnet_types_are_not_reachable()
    {
        Assert.Throws<ScriptEvaluationException>(() => Eval("return System.IO.File.ReadAllText('x')"));
        Assert.Equal("undefined", Eval("return typeof System"));
        Assert.Equal("undefined", Eval("return typeof importNamespace"));
    }
}
