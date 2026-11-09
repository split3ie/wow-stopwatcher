namespace WowStopwatcher.Core.Tests;

public class RestoredShortcutTests
{
    private static StopwatchDefinition Def(string id, string? shortcut) => new(id, id, "#111111", null, shortcut, 0);

    [Fact]
    public void FreeShortcut_IsKept() =>
        Assert.Equal("Ctrl+F5", RestoredShortcut.Resolve("Ctrl+F5", [Def("a", "Ctrl+F1")]));

    [Fact]
    public void TakenShortcut_GetsNextFreeDefault() =>
        Assert.Equal("Ctrl+F2", RestoredShortcut.Resolve("Ctrl+F1", [Def("a", "Ctrl+F1")]));

    [Fact]
    public void TakenShortcut_ComparedCaseInsensitiveAndTrimmed() =>
        Assert.Equal("Ctrl+F2", RestoredShortcut.Resolve(" ctrl+f1 ", [Def("a", "Ctrl+F1")]));

    [Fact]
    public void NoShortcut_StaysNull() =>
        Assert.Null(RestoredShortcut.Resolve(null, [Def("a", "Ctrl+F1")]));

    [Fact]
    public void BlankShortcut_IsKeptAsIs() =>
        Assert.Equal("  ", RestoredShortcut.Resolve("  ", [Def("a", null)]));
}
