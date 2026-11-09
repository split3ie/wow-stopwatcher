using Avalonia.Threading;

namespace WowStopwatcher.App.Shell;

/// <summary><see cref="IUiDispatcher"/> over <see cref="Dispatcher.UIThread"/>.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();
}
