using System.Text.RegularExpressions;

namespace HR.Web.E2E.Tests.Infrastructure;

internal static partial class DiagnosticText
{
    public const int DefaultBodyLimit = 2048;

    [GeneratedRegex("""(?i)\b(password|pwd|user id|uid|username|host|server|data source|accountkey|sharedaccesskey|api[_-]?key|secret|token|authorization)"?\s*[=:]\s*("[^"]*"|[^;,\s"]*)""")]
    private static partial Regex KeyValueSecrets();

    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9\-._~+/]+=*")]
    private static partial Regex BearerTokens();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*")]
    private static partial Regex JwtTokens();

    [GeneratedRegex("""(?i)\b[a-z][a-z0-9+.-]*://[^/\s:@"]+:[^/\s@"]+@""")]
    private static partial Regex UrlCredentials();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var result = UrlCredentials().Replace(text, "[redacted-credentials]@");
        result = KeyValueSecrets().Replace(result, m => $"{m.Groups[1].Value}=[redacted]");
        result = BearerTokens().Replace(result, "Bearer [redacted]");
        result = JwtTokens().Replace(result, "[redacted-jwt]");
        return result;
    }

    public static string Truncate(string text, int limit, bool alreadyTruncated = false)
    {
        if (text.Length > limit)
            return text[..limit] + "...[truncated]";
        return alreadyTruncated ? text + "...[truncated]" : text;
    }

    public static string Sanitize(string? text, int limit = DefaultBodyLimit) =>
        Truncate(Redact(text), limit);

    public static string SingleLine(string text) =>
        text.Replace("\r", "\\r").Replace("\n", "\\n");
}
