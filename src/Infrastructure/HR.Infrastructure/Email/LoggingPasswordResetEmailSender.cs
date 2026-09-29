using HR.SharedKernel;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Email;

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
            "PASSWORD RESET EMAIL (stub) Browser={Browser} OS={OperatingSystem} ActionUrl=(redacted)",
            ua.BrowserName,
            ua.OperatingSystem);

        return Task.FromResult(true);
    }
}
