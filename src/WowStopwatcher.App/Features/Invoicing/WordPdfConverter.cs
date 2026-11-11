using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Converts with Microsoft Word through late-bound COM (Windows only). Word runs invisibly and is always closed.</summary>
public sealed class WordPdfConverter : IPdfConverter
{
    private const string ProgId = "Word.Application";
    private const int WdExportFormatPdf = 17;

    /// <inheritdoc />
    public string Name => "Microsoft Word";

    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows() && IsInstalled();

    /// <inheritdoc />
    public Task ConvertAsync(string docxPath, string outputPdfPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !IsInstalled())
        {
            throw new PdfConversionException(PdfErrorCode.NotAvailable, "Microsoft Word is not available.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ConvertOnStaThread(docxPath, outputPdfPath);
    }

    [SupportedOSPlatform("windows")]
    private static Task ConvertOnStaThread(string docxPath, string outputPdfPath)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                ConvertOnThisThread(docxPath, outputPdfPath);
                if (File.Exists(outputPdfPath))
                {
                    completion.SetResult();
                }
                else
                {
                    completion.SetException(new PdfConversionException(PdfErrorCode.OutputMissing, "Word produced no PDF."));
                }
            }
            catch (Exception ex)
            {
                completion.SetException(new PdfConversionException(PdfErrorCode.Failed, ex.Message, ex));
            }
        })
        {
            IsBackground = true,
            Name = "WordPdfConverter",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsInstalled()
    {
        try
        {
            return Type.GetTypeFromProgID(ProgId) is not null;
        }
        catch (COMException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ConvertOnThisThread(string docxPath, string outputPdfPath)
    {
        var type = Type.GetTypeFromProgID(ProgId) ?? throw new InvalidOperationException("Word is not installed.");
        dynamic? app = null;
        dynamic? document = null;
        try
        {
            app = Activator.CreateInstance(type)!;
            app.Visible = false;
            app.DisplayAlerts = 0;
            document = app.Documents.Open(docxPath, ReadOnly: true, Visible: false);
            document.ExportAsFixedFormat(outputPdfPath, WdExportFormatPdf);
        }
        finally
        {
            try
            {
                document?.Close(false);
            }
            catch (COMException)
            {
                // Word is shutting down anyway.
            }

            try
            {
                app?.Quit(false);
            }
            catch (COMException)
            {
                // Already closed.
            }

            if (document is not null)
            {
                Marshal.FinalReleaseComObject(document);
            }

            if (app is not null)
            {
                Marshal.FinalReleaseComObject(app);
            }
        }
    }
}
