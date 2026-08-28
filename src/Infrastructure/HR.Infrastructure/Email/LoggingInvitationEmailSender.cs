using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Email;

/// <summary>
/// Stub invitation email sender used when Postmark is not configured.
/// Logs the details so developers can verify the link during local development.
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
            "INVITATION EMAIL (stub) To={ToEmail} Name={RecipientName} ActionUrl={ActionUrl}",
            toEmail,
            recipientName ?? "(none)",
            actionUrl);

        return Task.FromResult(true);
    }
}
