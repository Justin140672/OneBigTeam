using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Email;

/// <summary>
/// Stub invitation email sender used when Postmark is not configured.
/// Logs enough for local development but never logs the action URL — it carries the single-use
/// invitation token. Developers can retrieve the pending invite/link from the database when needed.
/// </summary>
internal sealed class LoggingInvitationEmailSender(ILogger<LoggingInvitationEmailSender> logger)
    : IInvitationEmailSender
{
    public Task<bool> SendAsync(
        string toEmail,
        string? recipientName,
        string actionUrl,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "INVITATION EMAIL (stub) To={ToEmail} Name={RecipientName} ActionUrl=(redacted - contains invitation token)",
            SensitiveDataScrubber.MaskEmail(toEmail),
            recipientName ?? "(none)");

        return Task.FromResult(true);
    }
}
