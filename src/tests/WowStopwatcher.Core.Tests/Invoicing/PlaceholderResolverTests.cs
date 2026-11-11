using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class PlaceholderResolverTests
{
    private static readonly ScriptContext Context = new(
        1.5m, 90, 20m, 30m, "EUR", "2026-11", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "November 2026", null, [], _ => "ignored");

    private static PlaceholderDefinition Static(string alias, string value) =>
        new(0, "c", alias, PlaceholderKind.Static, value, false, 0);

    private static PlaceholderDefinition Script(string alias, string value) =>
        new(0, "c", alias, PlaceholderKind.Script, value, false, 0);

    /// <summary>Script grammar for tests: "ref:a" returns placeholder a ("null" when missing), "fail:msg" throws, "lit:x" returns x, "ctx" returns minutes.</summary>
    private sealed class FakeEvaluator : IScriptEvaluator
    {
        public List<string> Calls { get; } = [];

        public string Evaluate(string script, ScriptContext context)
        {
            Calls.Add(script);
            var parts = script.Split(':', 2);
            var argument = parts.Length > 1 ? parts[1] : string.Empty;
            return parts[0] switch
            {
                "ref" => context.Placeholder(argument) ?? "null",
                "fail" => throw new ScriptEvaluationException(argument),
                "lit" => argument,
                "ctx" => context.Minutes.ToString(),
                _ => throw new ScriptEvaluationException("bad"),
            };
        }
    }

    [Fact]
    public void Static_IsUsedAsIs()
    {
        var result = PlaceholderResolver.Resolve([Static("s", "text")], Context, new FakeEvaluator());

        Assert.Equal(new PlaceholderResult("text", null), result["s"]);
    }

    [Fact]
    public void Script_GetsBaseContext()
    {
        var result = PlaceholderResolver.Resolve([Script("x", "ctx")], Context, new FakeEvaluator());

        Assert.Equal("90", result["x"].Value);
    }

    [Fact]
    public void Dependency_ScriptReadsAnotherAlias_InAnyDefinitionOrder()
    {
        var result = PlaceholderResolver.Resolve(
            [Script("a", "ref:b"), Script("b", "ref:c"), Static("c", "deep")], Context, new FakeEvaluator());

        Assert.Equal("deep", result["a"].Value);
        Assert.Equal("deep", result["b"].Value);
        Assert.Null(result["a"].Error);
    }

    [Fact]
    public void Memoized_EachScriptRunsOnce()
    {
        var evaluator = new FakeEvaluator();

        PlaceholderResolver.Resolve([Script("a", "ref:c"), Script("b", "ref:c"), Script("c", "lit:v")], Context, evaluator);

        Assert.Equal(1, evaluator.Calls.Count(c => c == "lit:v"));
        Assert.Equal(3, evaluator.Calls.Count);
    }

    [Fact]
    public void Cycle_BothAliasesGetErrorNamingTheChain()
    {
        var result = PlaceholderResolver.Resolve([Script("a", "ref:b"), Script("b", "ref:a")], Context, new FakeEvaluator());

        Assert.Equal("Cycle: a → b → a", result["a"].Error);
        Assert.Equal("Cycle: a → b → a", result["b"].Error);
        Assert.Null(result["a"].Value);
    }

    [Fact]
    public void SelfCycle_IsAnError()
    {
        var result = PlaceholderResolver.Resolve([Script("a", "ref:a")], Context, new FakeEvaluator());

        Assert.Equal("Cycle: a → a", result["a"].Error);
    }

    [Fact]
    public void Cycle_OnlyMembersGetTheError_ADependentJustSeesNull()
    {
        var result = PlaceholderResolver.Resolve(
            [Script("d", "ref:a"), Script("a", "ref:b"), Script("b", "ref:a")], Context, new FakeEvaluator());

        Assert.Equal("Cycle: a → b → a", result["a"].Error);
        Assert.Equal("Cycle: a → b → a", result["b"].Error);
        Assert.Null(result["d"].Error);
        Assert.Equal("null", result["d"].Value);
    }

    [Fact]
    public void EvaluationError_BecomesErrorWithMessage()
    {
        var result = PlaceholderResolver.Resolve([Script("a", "fail:boom")], Context, new FakeEvaluator());

        Assert.Equal(new PlaceholderResult(null, "boom", ScriptErrorCode.Runtime), result["a"]);
        Assert.False(result["a"].IsOk);
    }

    [Fact]
    public void FailingDependency_GivesNullToTheDependent()
    {
        var result = PlaceholderResolver.Resolve([Script("a", "ref:b"), Script("b", "fail:x")], Context, new FakeEvaluator());

        Assert.Equal("null", result["a"].Value);
        Assert.Equal("x", result["b"].Error);
    }

    [Fact]
    public void UnknownAlias_ReturnsNullToTheScript()
    {
        var result = PlaceholderResolver.Resolve([Script("a", "ref:nope")], Context, new FakeEvaluator());

        Assert.Equal("null", result["a"].Value);
    }
}
