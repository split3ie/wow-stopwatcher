namespace WowStopwatcher.App.Shell;

/// <summary>The OS secret store could not be used (locked, access denied, damaged entry or not available). Messages never contain the key.</summary>
public class DatabaseKeyStoreException : Exception
{
    public DatabaseKeyStoreException(string message) : base(message)
    {
    }

    public DatabaseKeyStoreException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
