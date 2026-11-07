using System.IO.Pipes;
using System.Security.Principal;

namespace WowStopwatcher.App.Services;

/// <summary>
/// Allows one app instance per user (named mutex). A second launch signals the first one through a per-user
/// local named pipe so it can show its window. Works on Windows, macOS and Linux.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const PipeOptions Options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    private static string Suffix =>
        (OperatingSystem.IsWindows() ? WindowsIdentity.GetCurrent().User?.Value : null) ?? Environment.UserName;

    private static string MutexName => $"WowStopwatcher.Instance.{Suffix}";

    private static string PipeName => $"WowStopwatcher.Show.{Suffix}";

    /// <summary>Returns the guard when this is the first instance, otherwise null.</summary>
    public static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceGuard(mutex);
    }

    /// <summary>Asks the running instance to show its window.</summary>
    public static void SignalExisting()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, Options);
            client.Connect(1000);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not signal the running instance: {ex.Message}");
        }
    }

    /// <summary>Calls <paramref name="onShowRequested"/> (on a background thread) whenever another launch signals.</summary>
    public void StartListening(Action onShowRequested)
    {
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, Options);
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    onShowRequested();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"Single-instance listener error: {ex.Message}");
                    try
                    {
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from another thread; the OS frees it when the process exits.
        }

        _mutex.Dispose();
        _cts.Dispose();
    }
}
