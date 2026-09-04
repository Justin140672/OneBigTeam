using System.Net.Sockets;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Maps an operational virus-scan failure exception to a safe, closed-set category string for
/// persistence (IScannableFile.ScanFailureReason) and auditing (FileScanStatusChangedAuditEvent).
///
/// Raw exception messages must never reach persisted or audited data — they can carry internal
/// paths, host/port details, storage addresses, signed URLs, tokens or personal data pulled in via
/// interpolated diagnostic text (e.g. ClamAvVirusScanService's "Unexpected clamd response scanning
/// '{fileName}': '{reply}'" or HttpClient exceptions that echo the request URI). The full exception
/// is still logged via ILogger by the caller for restricted operational diagnosis — only the
/// category returned here is ever persisted or audited.
///
/// This is a fixed category mapping keyed on exception type/shape, not a regex-based scrubber of
/// arbitrary exception text — scrubbing free text can never be proven safe, while a closed set of
/// categories can.
/// </summary>
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
