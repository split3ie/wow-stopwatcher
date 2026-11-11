using System.Globalization;
using Jint;
using Jint.Native;
using Jint.Native.Json;
using Jint.Runtime;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>
/// Runs placeholder scripts with Jint. The script is the body of a function; its return value becomes the text.
/// No .NET types are reachable from the script. Time, memory, statements and recursion are limited.
/// </summary>
public sealed class JintScriptEvaluator : IScriptEvaluator
{
    private const string FunctionName = "__placeholder";

    /// <inheritdoc />
    public string Evaluate(string script, ScriptContext context)
    {
        try
        {
            var engine = new Engine(options =>
            {
                options.Strict();
                options.TimeoutInterval(TimeSpan.FromSeconds(1));
                options.LimitMemory(16 * 1024 * 1024);
                options.MaxStatements(100_000);
                options.LimitRecursion(64);
            });

            Expose(engine, context);
            engine.Execute($"function {FunctionName}() {{{script}\n}}");
            var result = engine.Invoke(FunctionName);
            return result.IsNull() || result.IsUndefined() ? string.Empty : result.ToString();
        }
        catch (JavaScriptException ex)
        {
            var isSyntax = ex.Error.IsObject() && ex.Error.AsObject().Get("name").ToString() == "SyntaxError";
            throw new ScriptEvaluationException(isSyntax ? ScriptErrorCode.Syntax : ScriptErrorCode.Runtime, ex.Message, ex);
        }
        catch (TimeoutException ex)
        {
            throw new ScriptEvaluationException(ScriptErrorCode.Timeout, "The script ran longer than 1 second.", ex);
        }
        catch (StatementsCountOverflowException ex)
        {
            throw new ScriptEvaluationException(ScriptErrorCode.Limit, "The script ran too many statements.", ex);
        }
        catch (MemoryLimitExceededException ex)
        {
            throw new ScriptEvaluationException(ScriptErrorCode.Limit, "The script used too much memory.", ex);
        }
        catch (RecursionDepthOverflowException ex)
        {
            throw new ScriptEvaluationException(ScriptErrorCode.Limit, "The script recursed too deeply.", ex);
        }
        catch (Acornima.ParseErrorException ex)
        {
            throw new ScriptEvaluationException(ScriptErrorCode.Syntax, ex.Description, ex);
        }
        catch (Exception ex) when (ex is not ScriptEvaluationException and not OutOfMemoryException)
        {
            throw new ScriptEvaluationException(ScriptErrorCode.Runtime, ex.Message, ex);
        }
    }

    private static void Expose(Engine engine, ScriptContext context)
    {
        var inv = CultureInfo.InvariantCulture;
        var data = new
        {
            hours = (double)context.Hours,
            minutes = context.Minutes,
            rate = (double)context.Rate,
            amount = (double)context.Amount,
            currency = context.Currency,
            period = new
            {
                month = context.PeriodMonth,
                start = context.PeriodStart.ToString("yyyy-MM-dd", inv),
                end = context.PeriodEnd.ToString("yyyy-MM-dd", inv),
                name = context.PeriodName,
            },
            invoice = new { number = context.InvoiceNumber },
            lines = context.Lines.Select(l => new
            {
                date = l.Date.ToString("yyyy-MM-dd", inv),
                stopwatch = string.Join(", ", l.Sources.Select(s => s.StopwatchName)),
                minutes = l.Minutes,
                hours = l.Minutes / 60.0,
                note = l.Note,
            }).ToArray(),
        };

        var parsed = new JsonParser(engine).Parse(System.Text.Json.JsonSerializer.Serialize(data)).AsObject();
        foreach (var name in new[] { "hours", "minutes", "rate", "amount", "currency", "period", "invoice", "lines" })
        {
            engine.SetValue(name, parsed.Get(name));
        }

        engine.SetValue("ph", new Func<string, string?>(alias => context.Placeholder(alias)));
        engine.SetValue("format", new Func<JsValue, string?, string>((value, pattern) => Format(value, pattern, context)));
    }

    private static string Format(JsValue value, string? pattern, ScriptContext context)
    {
        var inv = CultureInfo.InvariantCulture;
        try
        {
            if (value.IsNumber())
            {
                return pattern is null ? value.AsNumber().ToString(inv) : value.AsNumber().ToString(pattern, inv);
            }

            var text = value.IsNull() || value.IsUndefined() ? string.Empty : value.ToString();
            if (DateTime.TryParse(text, inv, DateTimeStyles.None, out var date))
            {
                return pattern is null
                    ? InvoiceBuiltins.FormatDate(DateOnly.FromDateTime(date), context.Culture ?? inv, context.DateFormat)
                    : date.ToString(pattern, inv);
            }

            return text;
        }
        catch (FormatException)
        {
            throw new JavaScriptException($"format: invalid pattern '{pattern}'.");
        }
    }
}
