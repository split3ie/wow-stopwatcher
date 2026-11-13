using Avalonia.Controls.ApplicationLifetimes;
using WowStopwatcher.App.ViewModels;
using WowStopwatcher.App.Views;

namespace WowStopwatcher.App.Shell;

/// <summary>
/// <see cref="IStartupPrompts"/> with the real windows. Each window becomes <c>desktop.MainWindow</c> while it is shown, because the
/// file picker and the clipboard find their window there. The startup code keeps the app alive between windows.
/// </summary>
public sealed class AvaloniaStartupPrompts(
    IClassicDesktopStyleApplicationLifetime desktop,
    IClipboardService clipboard,
    TimeProvider time,
    DatabaseRecovery recovery) : IStartupPrompts
{
    public Task<ProtectDataOutcome> ShowProtectDataAsync(ProtectDataRequest request)
    {
        var viewModel = new ProtectDataViewModel(request, clipboard, time);
        var window = new ProtectDataWindow(viewModel);
        var completion = new TaskCompletionSource<ProtectDataOutcome>();
        Exception? failure = null;
        var done = false;

        viewModel.Finished += (_, error) =>
        {
            failure = error;
            done = error is null;
            viewModel.IsWorking = false; // closing is refused while the work runs
            window.Close();
        };
        window.Closed += (_, _) =>
        {
            if (failure is not null)
            {
                completion.SetException(failure);
            }
            else
            {
                completion.SetResult(done ? ProtectDataOutcome.Done : ProtectDataOutcome.Cancelled);
            }
        };

        desktop.MainWindow = window;
        window.Show();
        return completion.Task;
    }

    public Task<RecoveryPromptOutcome> ShowRecoveryPromptAsync(RecoveryPromptRequest request)
    {
        var viewModel = new RecoveryPromptViewModel(request, recovery);
        var window = new RecoveryPromptWindow(viewModel);
        var completion = new TaskCompletionSource<RecoveryPromptOutcome>();
        var outcome = RecoveryPromptOutcome.Quit;

        viewModel.Unlocked += (_, _) =>
        {
            outcome = RecoveryPromptOutcome.Unlocked;
            window.Close();
        };
        viewModel.QuitRequested += (_, _) => window.Close();
        window.Closed += (_, _) => completion.SetResult(outcome);

        desktop.MainWindow = window;
        window.Show();
        return completion.Task;
    }
}
