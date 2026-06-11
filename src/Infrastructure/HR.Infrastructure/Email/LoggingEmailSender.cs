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
        logger.LogInformation(
            "EMAIL (stub) To={ToEmail} Subject={Subject}",
            toEmail,
            subject);

        return Task.CompletedTask;
    }
}
