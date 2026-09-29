using System.Net.Sockets;

namespace HR.Modules.Documents.Services;

internal static class VirusScanFailureReasonMapper
{
    public const string ScannerUnavailable = "Virus scanner unavailable.";
    public const string ScanTimedOut = "Virus scan timed out.";
    public const string DownloadFailed = "File could not be downloaded for scanning.";
    public const string GenericFailure = "Virus scan failed.";

    public static string ToSafeCategory(Exception exception) => exception switch
    {
        OperationCanceledException => ScanTimedOut,
        TimeoutException => ScanTimedOut,
        SocketException => ScannerUnavailable,
        HttpRequestException => DownloadFailed,
        IOException => DownloadFailed,
        _ => GenericFailure,
    };
}
