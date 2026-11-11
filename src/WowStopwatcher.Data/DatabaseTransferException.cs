namespace WowStopwatcher.Data;

/// <summary>A transfer step failed. Nothing that existed before was changed or deleted.</summary>
public sealed class DatabaseTransferException : Exception
{
    public DatabaseTransferException(TransferErrorCode code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    public TransferErrorCode Code { get; }
}
