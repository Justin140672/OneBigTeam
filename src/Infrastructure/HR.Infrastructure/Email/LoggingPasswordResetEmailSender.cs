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

        logger.LogInformation(
            "PASSWORD RESET EMAIL (stub) To={ToEmail} Name={RecipientName} Browser={Browser} OS={OperatingSystem} ActionUrl=(redacted)",
            toEmail,
            recipientName ?? "(none)",
            ua.BrowserName,
            ua.OperatingSystem);

        return Task.FromResult(true);
    }
}
