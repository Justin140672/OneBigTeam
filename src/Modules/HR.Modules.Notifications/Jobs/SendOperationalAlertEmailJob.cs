using System.Net;
using Hangfire;
using Hangfire.Server;
using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Modules.Notifications.Jobs;

[AutomaticRetry(Attempts = OperationalAlertEmailDelivery.MaxAttempts, DelaysInSeconds = new[] { 30, 120, 600 })]
internal sealed class SendOperationalAlertEmailJob(
    NotificationsDbContext db,
    IEmailSender emailSender,
    IOptions<OperationalAlertEmailOptions> options,
    IClock clock,
    ILogger<SendOperationalAlertEmailJob> logger)
{
    public async Task SendAsync(Guid alertId, PerformContext? context = null)
    {
        var delivery = await db.OperationalAlertEmailDeliveries
            .SingleOrDefaultAsync(d => d.AlertId == alertId);

        if (delivery is null)
        {
            logger.LogWarning(
                "SendOperationalAlertEmailJob: no delivery row for alert {AlertId} — nothing to send.", alertId);
            return;
        }

        if (delivery.IsTerminal)
        {
            logger.LogInformation(
                "SendOperationalAlertEmailJob: delivery for alert {AlertId} is terminal ({Status}) — no-op.",
                alertId, delivery.Status);
            return;
        }

        var recipient = options.Value.InternalRecipientEmail;
        if (string.IsNullOrWhiteSpace(recipient))
        {
            delivery.MarkSkipped("No internal operations recipient configured.");
            await db.SaveChangesAsync();
            logger.LogInformation(
                "SendOperationalAlertEmailJob: no internal recipient configured — alert {AlertId} email skipped.", alertId);
            return;
        }

        var alert = await db.AdministrativeAlerts
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == alertId);

        if (alert is null)
        {
            delivery.MarkFailed("Alert no longer exists.");
            await db.SaveChangesAsync();
            return;
        }

        var now = clock.UtcNowOffset();

        // Ticket 3L: ownership precedence. If another worker still holds a live lease on an in-flight
        // send, do nothing — regardless of the attempt budget. The old behaviour marked the row
        // permanently Failed here as soon as the attempt budget was spent; when the real sender was
        // mid-send on its final attempt (Sending, lease live, AttemptCount == MaxAttempts) that raced
        // its MarkSent save and silently dropped a delivered email.
        if (delivery.HasLiveOwner(now))
        {
            logger.LogInformation(
                "SendOperationalAlertEmailJob: delivery for alert {AlertId} is owned by another worker until {LeaseExpiresAt:o} — deferring.",
                alertId, delivery.LeaseExpiresAt);
            return;
        }

        if (!delivery.HasAttemptsRemaining)
        {
            delivery.MarkFailed("Delivery abandoned after repeated failed attempts.");
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                logger.LogInformation(
                    "SendOperationalAlertEmailJob: delivery for alert {AlertId} was re-claimed before it could be failed — deferring.",
                    alertId);
                return;
            }

            logger.LogError(
                "SendOperationalAlertEmailJob: delivery for alert {AlertId} exhausted its {MaxAttempts} attempts — marked permanently failed.",
                alertId, OperationalAlertEmailDelivery.MaxAttempts);
            return;
        }

        var ownerToken = Guid.NewGuid();
        var claim = delivery.Claim(ownerToken, now);
        if (claim.IsFailure)
        {
            logger.LogInformation(
                "SendOperationalAlertEmailJob: could not claim delivery for alert {AlertId} — {Reason}", alertId, claim.Error.Message);
            return;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation(
                "SendOperationalAlertEmailJob: concurrent claim detected for alert {AlertId} — deferring.", alertId);
            return;
        }

        try
        {
            var (subject, htmlBody) = BuildEmail(alert, options.Value.AdminAppBaseUrl);
            await emailSender.SendAsync(recipient, subject, htmlBody, CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (!delivery.HasAttemptsRemaining)
            {
                delivery.MarkFailed(SanitizeFailureReason(ex));
                await SaveIgnoringConcurrencyAsync();
                logger.LogError(ex,
                    "SendOperationalAlertEmailJob: operations notification permanently failed after {Attempts} attempts for alert {AlertId}.",
                    OperationalAlertEmailDelivery.MaxAttempts, alertId);
            }
            else
            {
                delivery.ReleaseForRetry(clock.UtcNowOffset());
                await SaveIgnoringConcurrencyAsync();
                logger.LogWarning(ex,
                    "SendOperationalAlertEmailJob: send attempt {AttemptCount} failed for alert {AlertId} — will retry.",
                    delivery.AttemptCount, alertId);
            }

            throw;
        }

        delivery.MarkSent(clock.UtcNowOffset());
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning(
                "SendOperationalAlertEmailJob: alert {AlertId} email was sent but the delivery row was re-claimed before the Sent status could be persisted.",
                alertId);
            return;
        }

        logger.LogInformation(
            "SendOperationalAlertEmailJob: operations notification sent for alert {AlertId} (company {CompanyId}).",
            alertId, alert.CompanyId);
    }

    private async Task SaveIgnoringConcurrencyAsync()
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
        }
    }

    private static string SanitizeFailureReason(Exception ex) => ex switch
    {
        HttpRequestException => "Email provider error.",
        TaskCanceledException => "Email provider request timed out.",
        _ => "Email delivery failed.",
    };

    private static (string Subject, string HtmlBody) BuildEmail(
        Domain.AdministrativeAlert alert, string? adminAppBaseUrl)
    {
        var subject = $"[Operations] {alert.Summary}";

        var link = !string.IsNullOrWhiteSpace(alert.ActionUrl)
            ? alert.ActionUrl
            : !string.IsNullOrWhiteSpace(adminAppBaseUrl)
                ? $"{adminAppBaseUrl.TrimEnd('/')}/operational-alerts/{alert.Id}"
                : null;

        var exportRef = alert.AffectedEntityId?.ToString() ?? "n/a";
        var missingCount = alert.AffectedItemCount?.ToString() ?? "n/a";

        var linkRow = link is null
            ? ""
            : $"""<p><a href="{WebUtility.HtmlEncode(link)}">Open this alert in the admin app</a></p>""";

        var htmlBody = $"""
            <html>
            <body style="font-family:sans-serif;max-width:600px;margin:auto;padding:24px">
              <h2>{WebUtility.HtmlEncode(alert.Summary)}</h2>
              <p>A new operational alert has been opened and requires investigation.</p>
              <table cellpadding="4" style="border-collapse:collapse">
                <tr><td><strong>Severity</strong></td><td>{alert.Severity}</td></tr>
                <tr><td><strong>Category</strong></td><td>{alert.Category}</td></tr>
                <tr><td><strong>Company reference</strong></td><td>{alert.CompanyId}</td></tr>
                <tr><td><strong>Export reference</strong></td><td>{WebUtility.HtmlEncode(exportRef)}</td></tr>
                <tr><td><strong>Missing file count</strong></td><td>{WebUtility.HtmlEncode(missingCount)}</td></tr>
                <tr><td><strong>Occurrences</strong></td><td>{alert.OccurrenceCount}</td></tr>
                <tr><td><strong>First seen</strong></td><td>{alert.FirstOccurredAt:u}</td></tr>
              </table>
              {linkRow}
              <p style="color:#666;font-size:12px">This message deliberately omits document names and contents.</p>
            </body>
            </html>
            """;

        return (subject, htmlBody);
    }
}
