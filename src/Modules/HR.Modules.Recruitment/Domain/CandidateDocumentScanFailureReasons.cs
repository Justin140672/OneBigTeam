using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace HR.Modules.Recruitment.Domain;

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
