using HR.Modules.Support.Domain;
using HR.Modules.Support.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Jobs;

internal sealed class SupportNotificationRetryJob(
    SupportDbContext db,
    IEmailSender emailSender,
    IClock clock)
{
    private const int MaxRetryCount = 5;

    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();

        var failedAttempts = await db.SupportNotificationAttempts
            .Where(a => a.Status == SupportNotificationStatus.Failed && a.RetryCount < MaxRetryCount)
            .ToListAsync();

        foreach (var attempt in failedAttempts)
        {
            attempt.IncrementRetry();

            if (string.IsNullOrWhiteSpace(attempt.RecipientEmail))
            {
                attempt.MarkFailed("No recipient email available to retry.", now);
                continue;
            }

            var supportRequest = await db.SupportRequests
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == attempt.SupportRequestId);

            if (supportRequest is null)
            {
                attempt.MarkFailed("Associated support request no longer exists.", now);
                continue;
            }

            try
            {
                await emailSender.SendAsync(
                    attempt.RecipientEmail,
                    $"[Retry] Support request update: {supportRequest.ReferenceNumber}",
                    $"<p>This is a retried notification for support request {supportRequest.ReferenceNumber}.</p>",
                    default);
                attempt.MarkSent(clock.UtcNowOffset());
            }
            catch (Exception ex)
            {
                attempt.MarkFailed(ex.Message, clock.UtcNowOffset());
            }
        }

        await db.SaveChangesAsync();
    }
}
