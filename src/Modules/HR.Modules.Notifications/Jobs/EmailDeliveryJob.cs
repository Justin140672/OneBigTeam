using Hangfire;
using Hangfire.Server;
using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications;
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
    IAuditEventPublisher auditPublisher,
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

            // NOT-05: not worth retrying (see comment above) — this is itself a final, permanent
            // failure, so it is audited immediately rather than waiting for [AutomaticRetry] to be
            // exhausted. FailureReason is already sanitised (never the raw recipient/exception
            // detail) — see EmailDelivery.MarkFailed's doc comment.
            await auditPublisher.PublishAsync(new EmailDeliveryFailedAuditEvent(
                delivery.CompanyId, notificationId, notification.EmployeeId,
                delivery.FailureReason!, clock.UtcNowOffset()), CancellationToken.None);

            logger.LogWarning(
                "EmailDeliveryJob: no email on file for employee {EmployeeId} (notification {NotificationId}) — delivery marked permanently failed.",
                notification.EmployeeId, notificationId);
            return;
        }

        delivery.RecordAttempt(clock.UtcNowOffset());
        await db.SaveChangesAsync();

        try
        {
            // NOT-03: a templated delivery already carries its own rendered (HTML-encoded) subject
            // and body — see EmailDelivery.CreateTemplated / NotificationTemplateRenderer — and is
            // sent as-is. A non-templated delivery (every notification type outside the NOT-03
            // catalogue) falls back to the pre-existing behaviour of wrapping the notification's own
            // Title/Body.
            var subject = delivery.EmailSubject ?? notification.Title;
            var htmlBody = delivery.EmailBody ?? BuildHtmlBody(notification.Title, notification.Body);
            await emailSender.SendAsync(recipientEmail, subject, htmlBody, CancellationToken.None);

            var sentAt = clock.UtcNowOffset();
            delivery.MarkSent(sentAt);
            await db.SaveChangesAsync();

            // NOT-05: only reached on an actual first-time transition to Sent — the idempotency
            // guard above (Status == Sent → return) short-circuits before this point on a replayed
            // job for an already-delivered notification, so a retry that eventually succeeds
            // produces exactly one success event, never a duplicate.
            await auditPublisher.PublishAsync(new EmailDeliverySucceededAuditEvent(
                delivery.CompanyId, notificationId, notification.EmployeeId, sentAt), CancellationToken.None);
        }
        catch (Exception ex)
        {
            var retryCount = context?.GetJobParameter<int?>("RetryCount") ?? 0;
            var isFinalAttempt = retryCount >= MaxAttempts - 1;

            if (isFinalAttempt)
            {
                var reason = SanitizeFailureReason(ex);
                delivery.MarkFailed(reason);
                await db.SaveChangesAsync();

                // NOT-05: final delivery failure only — never on an intermediate retry attempt
                // (the else branch below, for retries not yet exhausted, deliberately publishes
                // nothing). Reason is the same sanitised category persisted to FailureReason —
                // never the raw exception message/stack trace.
                await auditPublisher.PublishAsync(new EmailDeliveryFailedAuditEvent(
                    delivery.CompanyId, notificationId, notification.EmployeeId,
                    reason, clock.UtcNowOffset()), CancellationToken.None);

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
