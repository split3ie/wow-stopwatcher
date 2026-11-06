using System.Security.Principal;

namespace WowStopwatcher.App.Services;

/// <summary>
/// Allows one app instance per user (named mutex). A second launch signals the first one through a named
/// event so it can show its window.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private volatile bool _disposed;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    private static string Suffix =>
        WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;

    private static string MutexName => $"WowStopwatcher.Instance.{Suffix}";

    private static string EventName => $"WowStopwatcher.Show.{Suffix}";

    /// <summary>Returns the guard when this is the first instance, otherwise null.</summary>
    public static SingleInstanceGuard? TryAcquire()
    {
        // The event is created first so a second instance can always find it once the mutex is taken.
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            mutex.Dispose();
            showEvent.Dispose();
            return null;
        }

        return new SingleInstanceGuard(mutex, showEvent);
    }

    /// <summary>Asks the running instance to show its window.</summary>
    public static void SignalExisting()
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(EventName);
            handle.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not signal the running instance: {ex.Message}");
        }
    }

    /// <summary>Calls <paramref name="onShowRequested"/> (on a background thread) whenever another launch signals.</summary>
    public void StartListening(Action onShowRequested)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                _showEvent.WaitOne();
                if (_disposed)
                {
                    return;
                }

                onShowRequested();
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstanceListener",
        };
        thread.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _showEvent.Set();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from another thread; the OS frees it when the process exits.
        }

        _mutex.Dispose();
        _showEvent.Dispose();
    }
}
