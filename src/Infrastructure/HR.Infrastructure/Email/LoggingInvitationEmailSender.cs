using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Email;

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
            "INVITATION EMAIL (stub) ActionUrl=(redacted - contains invitation token)");

        return Task.FromResult(true);
    }
}
