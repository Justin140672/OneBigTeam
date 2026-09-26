using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// Closed set of safe, persisted/audited explanations for a candidate-document scan outcome that
/// is not Clean. Mirrors HR.Modules.Documents' VirusScanFailureReasonMapper (which that module keeps
/// internal): raw exception text can carry hosts, storage keys, signed URLs, tokens or personal
/// data, so only these fixed categories are ever written to candidate_documents.scan_failure_reason
/// or an audit payload. The full exception is logged via ILogger by the caller for restricted
/// operational diagnosis only.
/// </summary>
internal static partial class CandidateDocumentScanFailureReasons
{
    public const string ScannerUnavailable = "Virus scanner unavailable.";
    public const string ScanTimedOut = "Virus scan timed out.";
    public const string FileUnreadable = "File could not be read from storage for scanning.";
    public const string GenericFailure = "Virus scan failed.";
    public const string ScanAbandoned = "Virus scan attempt did not complete.";
    public const string RetryLimitReached = "Virus scan retry limit reached.";
    public const string UnknownThreat = "Unknown threat";

    private const int MaxThreatNameLength = 150;

    public static string FromException(Exception exception) => exception switch
    {
        OperationCanceledException => ScanTimedOut,
        TimeoutException => ScanTimedOut,
        SocketException => ScannerUnavailable,
        HttpRequestException => FileUnreadable,
        IOException => FileUnreadable,
        _ => GenericFailure,
    };

    /// <summary>
    /// Scanner threat names (e.g. ClamAV's "Eicar-Test-Signature" or "Win.Trojan.Agent-123") are a
    /// useful part of the quarantine evidence but still come from an external process, so they are
    /// restricted to a conservative character set and length before being persisted or audited.
    /// </summary>
    public static string SanitiseThreatName(string? threatName)
    {
        if (string.IsNullOrWhiteSpace(threatName))
            return UnknownThreat;

        var trimmed = threatName.Trim();
        if (trimmed.Length > MaxThreatNameLength || !SafeThreatName().IsMatch(trimmed))
            return UnknownThreat;

        return trimmed;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._:/+\- ]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeThreatName();
}
