namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Opens a generated invoice file with the system's default program, or shows it in the file manager.</summary>
public interface IInvoiceFileLauncher
{
    /// <summary>Opens the file in its default app; false when it does not exist or the system cannot open it.</summary>
    bool OpenFile(string path);

    /// <summary>Opens the folder of the file with the file selected where the system supports it; false when it does not exist or cannot be opened.</summary>
    bool ShowInFolder(string path);
}
