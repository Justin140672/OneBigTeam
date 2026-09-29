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

[AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 30, 120, 600 })]
internal sealed class EmailDeliveryJob(
    NotificationsDbContext db,
    IEmailSender emailSender,
    IUserEmailReader userEmailReader,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IAdministrativeAlertWriter administrativeAlertWriter,
    ICompanyNotificationSettingsReader notificationSettingsReader,
    ILogger<EmailDeliveryJob> logger)
{
    public const int MaxAttempts = 4;

    private async Task RaiseDeliveryFailureAlertAsync(Guid companyId, Guid notificationId, string sanitizedReason, DateTimeOffset occurredAt)
    {
        try
        {
            await administrativeAlertWriter.RaiseAsync(new RaiseAdministrativeAlertCommand(
                companyId,
                AdministrativeAlertSeverity.Warning,
                AdministrativeAlertCategory.IntegrationDelivery,
                "Notification email delivery is failing",
                $"At least one notification email could not be delivered ({sanitizedReason}). Most recent failed notification: {notificationId}.",
                occurredAt,
                DedupKey: "integration:email-delivery-failure",
                AffectedEntityType: "EmailDelivery",
                AffectedEntityId: notificationId,
                RecommendedAction: "Check the email provider configuration and recipient addresses.",
                ActionUrl: null),
                CancellationToken.None);
        }
        catch (Exception alertEx)
        {
            logger.LogWarning(alertEx,
                "EmailDeliveryJob: failed to raise administrative alert for delivery failure of notification {NotificationId}.",
                notificationId);
        }
    }

    public async Task SendAsync(Guid notificationId, Guid companyId, PerformContext? context = null)
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

        if (delivery.CompanyId != companyId)
        {
            logger.LogError(
                "EmailDeliveryJob: company mismatch for notification {NotificationId} — job argument {ArgCompanyId} does not match delivery's company {ActualCompanyId}.",
                notificationId, companyId, delivery.CompanyId);
            throw new InvalidOperationException(
                $"EmailDelivery {notificationId} does not belong to company {companyId}.");
        }

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

        var notificationSettings = await notificationSettingsReader.GetNotificationSettingsAsync(delivery.CompanyId, CancellationToken.None);
        if (!notificationSettings.EmailNotificationsEnabled && !NotificationChannelDefaults.IsMandatoryEmail(notification.Type))
        {
            delivery.MarkSkipped("Email notifications disabled for this company.");
            await db.SaveChangesAsync();
            logger.LogInformation(
                "EmailDeliveryJob: email notifications disabled for company {CompanyId} — delivery for notification {NotificationId} skipped.",
                delivery.CompanyId, notificationId);
            return;
        }

        var recipientEmail = await userEmailReader.GetEmailAsync(
            delivery.CompanyId, notification.EmployeeId, CancellationToken.None);

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            delivery.RecordAttempt(clock.UtcNowOffset());
            delivery.MarkFailed("Invalid recipient address.");
            await db.SaveChangesAsync();

            await auditPublisher.PublishAsync(new EmailDeliveryFailedAuditEvent(
                delivery.CompanyId, notificationId, notification.EmployeeId,
                delivery.FailureReason!, clock.UtcNowOffset()), CancellationToken.None);

            await RaiseDeliveryFailureAlertAsync(
                delivery.CompanyId, notificationId, delivery.FailureReason!, clock.UtcNowOffset());

            logger.LogWarning(
                "EmailDeliveryJob: no email on file for employee {EmployeeId} (notification {NotificationId}) — delivery marked permanently failed.",
                notification.EmployeeId, notificationId);
            return;
        }

        delivery.RecordAttempt(clock.UtcNowOffset());
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation(
                "EmailDeliveryJob: concurrent claim detected for notification {NotificationId} — deferring to the other execution.",
                notificationId);
            return;
        }

        try
        {
            var subject = delivery.EmailSubject ?? notification.Title;
            var htmlBody = delivery.EmailBody ?? BuildHtmlBody(notification.Title, notification.Body);
            await emailSender.SendAsync(recipientEmail, subject, htmlBody, CancellationToken.None);

            var sentAt = clock.UtcNowOffset();
            delivery.MarkSent(sentAt);
            await db.SaveChangesAsync();

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

                await RaiseDeliveryFailureAlertAsync(
                    delivery.CompanyId, notificationId, reason, clock.UtcNowOffset());

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

            throw;
        }
    }

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
