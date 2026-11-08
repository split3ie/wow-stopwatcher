using WowStopwatcher.Core;

namespace WowStopwatcher.Core.Tests;

public class DefaultShortcutsTests
{
    private static IEnumerable<string> All(string prefix) => Enumerable.Range(1, 12).Select(n => $"{prefix}+F{n}");

    [Fact]
    public void Next_Empty_ReturnsCtrlF1() =>
        Assert.Equal("Ctrl+F1", DefaultShortcuts.Next([]));

    [Fact]
    public void Next_FillsGaps() =>
        Assert.Equal("Ctrl+F2", DefaultShortcuts.Next(["Ctrl+F1", "Ctrl+F3"]));

    [Fact]
    public void Next_AllCtrlUsed_ReturnsCtrlShiftF1() =>
        Assert.Equal("Ctrl+Shift+F1", DefaultShortcuts.Next(All("Ctrl")));

    [Fact]
    public void Next_AllCtrlAndCtrlShiftUsed_ReturnsCtrlAltF1() =>
        Assert.Equal("Ctrl+Alt+F1", DefaultShortcuts.Next(All("Ctrl").Concat(All("Ctrl+Shift"))));

    [Fact]
    public void Next_ThreeGroupsUsed_ReturnsCtrlAltShiftF1() =>
        Assert.Equal(
            "Ctrl+Alt+Shift+F1",
            DefaultShortcuts.Next(All("Ctrl").Concat(All("Ctrl+Shift")).Concat(All("Ctrl+Alt"))));

    [Fact]
    public void Next_IsCaseInsensitive() =>
        Assert.Equal("Ctrl+F3", DefaultShortcuts.Next(["ctrl+f1", "CTRL+F2"]));

    [Fact]
    public void Next_IgnoresNullsAndEmpty() =>
        Assert.Equal("Ctrl+F1", DefaultShortcuts.Next([null, "", null]));

    [Fact]
    public void Next_AllUsed_ReturnsNull()
    {
        var used = All("Ctrl").Concat(All("Ctrl+Shift")).Concat(All("Ctrl+Alt")).Concat(All("Ctrl+Alt+Shift")).ToList();
        Assert.Equal(48, used.Count);
        Assert.Null(DefaultShortcuts.Next(used));
    }
}
