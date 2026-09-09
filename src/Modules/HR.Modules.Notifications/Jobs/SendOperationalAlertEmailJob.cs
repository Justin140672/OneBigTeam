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

/// <summary>
/// Follow-up C: Hangfire enqueue-style job that sends a single internal-operations notification email
/// when a new missing-file organisation-data-export alert opens. Enqueued by
/// <see cref="Persistence.AdministrativeAlertWriter"/> only on the first open of the alert, and
/// re-enqueued by <see cref="ReconcileStalledOperationalAlertEmailDeliveriesJob"/> for a delivery
/// that was saved but never queued, or whose owning worker crashed mid-send.
///
/// <para>Follow-up E — exclusive, recoverable delivery:
/// <list type="bullet">
///   <item><b>Atomic claim.</b> Before contacting Postmark the job calls
///   <see cref="OperationalAlertEmailDelivery.Claim"/> (row -&gt; Sending, attempt++, ownership lease)
///   and persists it under the <c>xmin</c> concurrency token. Two jobs racing after the first claim
///   has committed: the loser gets <see cref="DbUpdateConcurrencyException"/> and returns without
///   sending.</item>
///   <item><b>Lease.</b> A second job cannot claim a row whose lease is still live. A crashed owner's
///   lease expires and the row becomes re-claimable.</item>
///   <item><b>Recovery of interrupted sends.</b> A transient failure calls
///   <see cref="OperationalAlertEmailDelivery.ReleaseForRetry"/> (back to Pending) and rethrows so
///   Hangfire retries; the reconciliation sweep is the backstop if the process dies first.</item>
///   <item><b>Retry limit / terminal states.</b> Once <see cref="OperationalAlertEmailDelivery.MaxAttempts"/>
///   is reached the row is marked permanently <c>Failed</c> and every subsequent execution is a
///   no-op. <c>Sent</c> and <c>Skipped</c> are also terminal. There is no endless resend loop.</item>
///   <item><b>Alert always survives.</b> The alert is committed before this job runs; no email
///   outcome can hide or roll it back.</item>
/// </list></para>
///
/// <para>Idempotency reality: Postmark's send endpoint accepts no client idempotency key, so a crash
/// in the window between "Postmark accepted" and "row saved as Sent" can cause one duplicate send on
/// recovery. This is at-least-once delivery to an internal operations mailbox — see
/// <see cref="OperationalAlertEmailDelivery"/>.</para>
///
/// <para>Content safety: the email carries only non-sensitive metadata (company id, export id,
/// affected-item count, severity, category, occurrence count, admin deep link). It never includes
/// the alert Detail text, document filenames, document contents or storage credentials.</para>
/// </summary>
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

        // Terminal states are final: a previous attempt delivered, was skipped, or exhausted retries.
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

        // No live owner remains. Only now may the attempt budget retire the row: this covers a final
        // owner that crashed with an expired lease, and a duplicate/reconcile execution finding an
        // already-exhausted row. (The current owner recording its own failed final attempt happens in
        // the catch below, where it still legitimately holds the lease.)
        if (!delivery.HasAttemptsRemaining)
        {
            delivery.MarkFailed("Delivery abandoned after repeated failed attempts.");
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // A send job re-claimed the row between our load and this save — its ownership wins.
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
            // Another execution of this same delivery already committed its claim since we loaded the
            // row — back off as a no-op so we never send twice.
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
            // AttemptCount was incremented by Claim above, so this comparison already accounts for
            // the attempt we just made.
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
            // The email was accepted by Postmark but another worker re-claimed the row while we were
            // sending (our lease had expired). At-least-once: the email is out; nothing more to do.
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
            // Another worker owns the row now; its own outcome handling is authoritative.
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
