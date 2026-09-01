using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Email;

/// <summary>
/// Stub email sender that logs the email instead of delivering it.
/// Replace with a real Postmark/SMTP implementation when email
/// infrastructure is provisioned.
/// </summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        // Never logs htmlBody — transactional emails (invites, resets, support links) carry
        // single-use tokens and secure action links in their body.
        logger.LogInformation(
            "EMAIL (stub) To={ToEmail} Subject={Subject}",
            SensitiveDataScrubber.MaskEmail(toEmail),
            subject);

        return Task.CompletedTask;
    }
}
