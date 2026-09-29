using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Email;

internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        logger.LogInformation(
            "EMAIL (stub) SubjectLength={SubjectLength} BodyLength={BodyLength}",
            subject?.Length ?? 0,
            htmlBody?.Length ?? 0);

        return Task.CompletedTask;
    }
}
