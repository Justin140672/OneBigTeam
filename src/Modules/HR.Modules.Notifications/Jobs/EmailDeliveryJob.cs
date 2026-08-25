using Hangfire;
using Hangfire.Server;
using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Jobs;

/// <summary>
/// Hangfire enqueue-style (one-off, fired per-notification) job that performs the actual Postmark
/// send for a single EmailDelivery row, keeping the originating business handler
/// (NotificationWriter.WriteAsync, called synchronously from every notification-raising handler
/// across the app) free of any external HTTP call. Mirrors ScanUploadedFileJob
/// (HR.Modules.Documents/Jobs) — the first enqueue-style (rather than daily recurring) job in this
/// codebase — for retry shape and structured failure logging.
///
/// Idempotency: the notification's own id is this delivery's idempotency key (see EmailDelivery
/// remarks). Before sending, this job re-reads the EmailDelivery row and no-ops if it is already
/// Sent — this covers both Hangfire re-running a job after a crash between "email sent" and
/// "status persisted", and any accidental double-enqueue of the same notification id.
///
/// Retry/backoff: [AutomaticRetry] gives Hangfire's own bounded, increasing-delay retry for
/// transient failures (network errors, Postmark 5xx, etc.). AttemptCount/LastAttemptAt are updated
/// on every real attempt (regardless of outcome) so support/reporting can see how many times
/// delivery was tried. Only on the final exhausted attempt — or immediately, for a failure that is
/// clearly not worth retrying (no recipient email on file) — is the row marked permanently Failed;
/// while retries remain, the row stays Pending and the exception is rethrown so Hangfire schedules
/// the next attempt.
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 30, 120, 600 })]
internal sealed class EmailDeliveryJob(
    NotificationsDbContext db,
    IEmailSender emailSender,
    IUserEmailReader userEmailReader,
    IClock clock,
    ILogger<EmailDeliveryJob> logger)
{
    public const int MaxAttempts = 4;

    public async Task SendAsync(Guid notificationId, PerformContext? context = null)
    {
        var delivery = await db.EmailDeliveries
            .SingleOrDefaultAsync(d => d.NotificationId == notificationId);

        if (delivery is null)
        {
            logger.LogWarning(
                "EmailDeliveryJob: no EmailDelivery row found for notification {NotificationId} — skipping.",
                notificationId);
            return;
        }

        // Idempotency guard: a prior attempt already succeeded (possibly one whose "Sent" status
        // update raced a crash and got re-run by Hangfire, or a duplicate enqueue) — no-op.
        if (delivery.Status == EmailDeliveryStatus.Sent)
            return;

        var notification = await db.Notifications
            .AsNoTracking()
            .SingleOrDefaultAsync(n => n.Id == notificationId);

        if (notification is null)
        {
            delivery.MarkFailed("Notification no longer exists.");
            await db.SaveChangesAsync();
            return;
        }

        var recipientEmail = await userEmailReader.GetEmailAsync(
            delivery.CompanyId, notification.EmployeeId, CancellationToken.None);

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            // Not worth retrying — there is no address to send to, and that will not change on a
            // later attempt within this job's short retry window.
            delivery.RecordAttempt(clock.UtcNowOffset());
            delivery.MarkFailed("Invalid recipient address.");
            await db.SaveChangesAsync();
            logger.LogWarning(
                "EmailDeliveryJob: no email on file for employee {EmployeeId} (notification {NotificationId}) — delivery marked permanently failed.",
                notification.EmployeeId, notificationId);
            return;
        }

        delivery.RecordAttempt(clock.UtcNowOffset());
        await db.SaveChangesAsync();

        try
        {
            var htmlBody = BuildHtmlBody(notification.Title, notification.Body);
            await emailSender.SendAsync(recipientEmail, notification.Title, htmlBody, CancellationToken.None);

            delivery.MarkSent(clock.UtcNowOffset());
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            var retryCount = context?.GetJobParameter<int?>("RetryCount") ?? 0;
            var isFinalAttempt = retryCount >= MaxAttempts - 1;

            if (isFinalAttempt)
            {
                delivery.MarkFailed(SanitizeFailureReason(ex));
                await db.SaveChangesAsync();

                logger.LogError(ex,
                    "EmailDeliveryJob: email delivery permanently failed after {Attempts} attempts for notification {NotificationId}.",
                    MaxAttempts, notificationId);
            }
            else
            {
                logger.LogWarning(ex,
                    "EmailDeliveryJob: email delivery attempt {AttemptCount} failed for notification {NotificationId} — will retry.",
                    delivery.AttemptCount, notificationId);
            }

            // Rethrow while retries remain so Hangfire schedules the next attempt; rethrow on the
            // final attempt too so the existing BackgroundJobAuditFilter records the standard
            // operational-failure audit trail every other job already relies on.
            throw;
        }
    }

    /// <summary>
    /// Reduces an arbitrary send exception to a short, human-readable category — never the raw
    /// exception message or stack trace, which could contain internal request/response detail that
    /// should not surface in a support/reporting-visible record.
    /// </summary>
    private static string SanitizeFailureReason(Exception ex) => ex switch
    {
        HttpRequestException => "Email provider error.",
        TaskCanceledException => "Email provider request timed out.",
        _ => "Email delivery failed.",
    };

    private static string BuildHtmlBody(string title, string? body) => $"""
        <html>
        <body style="font-family:sans-serif;max-width:600px;margin:auto;padding:24px">
          <h2>{title}</h2>
          {(string.IsNullOrWhiteSpace(body) ? "" : $"<p>{body}</p>")}
        </body>
        </html>
        """;
}
