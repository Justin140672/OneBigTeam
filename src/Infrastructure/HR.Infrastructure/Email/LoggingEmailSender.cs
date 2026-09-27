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
        // Never logs the recipient email or htmlBody — transactional emails (invites, resets,
        // support links) carry single-use tokens and secure action links in their body, and the
        // recipient address is personal data that does not belong in routine operational logs.
        // The subject is caller-controlled and can carry names or company names, so only its
        // length is recorded.
        logger.LogInformation(
            "EMAIL (stub) SubjectLength={SubjectLength} BodyLength={BodyLength}",
            subject?.Length ?? 0,
            htmlBody?.Length ?? 0);

        return Task.CompletedTask;
    }
}
