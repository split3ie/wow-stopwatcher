using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Tests;

public class NullShortcutServiceTests
{
    [Fact]
    public void Reports_unavailable_and_does_nothing()
    {
        IShortcutService service = new NullShortcutService();
        var raised = false;
        service.FailuresChanged += (_, _) => raised = true;

        service.Suspend();
        service.Resume();
        service.Resume();

        Assert.False(service.IsAvailable);
        Assert.False(service.IsSupported);
        Assert.Empty(service.FailedIds);
        Assert.False(raised);
    }
}
