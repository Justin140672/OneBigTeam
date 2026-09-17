using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Email;

/// <summary>
/// Stub password-reset email sender used when Postmark is not configured.
/// Logs enough for local development but never logs the action URL (it carries the recovery token).
/// </summary>
internal sealed class LoggingPasswordResetEmailSender(ILogger<LoggingPasswordResetEmailSender> logger)
    : IPasswordResetEmailSender
{
    public Task<bool> SendAsync(
        string toEmail,
        string? recipientName,
        string actionUrl,
        string? userAgent,
        CancellationToken ct = default)
    {
        var ua = UserAgentSummary.Parse(userAgent);

        // Never logs the recipient email/name or the action URL (single-use recovery token).
        logger.LogInformation(
            "PASSWORD RESET EMAIL (stub) Browser={Browser} OS={OperatingSystem} ActionUrl=(redacted)",
            ua.BrowserName,
            ua.OperatingSystem);

        return Task.FromResult(true);
    }
}
