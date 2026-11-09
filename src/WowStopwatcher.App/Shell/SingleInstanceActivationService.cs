using WowStopwatcher.App.Composition;

namespace WowStopwatcher.App.Shell;

/// <summary>Shows the main window when a second instance of the app is started.</summary>
public sealed class SingleInstanceActivationService(IUiDispatcher dispatcher, IMainWindowService mainWindow) : IAppService
{
    public void Start() => Program.Instance?.StartListening(() => dispatcher.Post(mainWindow.Show));

    public void Dispose()
    {
    }
}
