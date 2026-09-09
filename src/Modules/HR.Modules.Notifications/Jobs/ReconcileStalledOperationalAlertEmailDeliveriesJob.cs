using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Jobs;

/// <summary>
/// Follow-up E: periodic reconciliation for <see cref="OperationalAlertEmailDelivery"/> rows that
/// were saved but whose send never completed:
/// <list type="bullet">
///   <item><b>Saved but never queued.</b> A <see cref="EmailDeliveryStatus.Pending"/> row older than
///   <see cref="PendingGraceMinutes"/> — the alert committed but the process died before
///   <c>IBackgroundJobClient.Enqueue</c> in <see cref="Persistence.AdministrativeAlertWriter"/>, or
///   the enqueue itself was lost — is re-enqueued.</item>
///   <item><b>Interrupted mid-send.</b> A <see cref="EmailDeliveryStatus.Sending"/> row whose
///   ownership lease has expired (the owning worker crashed) is re-enqueued while it still has
///   attempts left, or marked permanently <see cref="EmailDeliveryStatus.Failed"/> once
///   <see cref="OperationalAlertEmailDelivery.MaxAttempts"/> is spent.</item>
/// </list>
///
/// Safety controls: a grace period so an in-flight first attempt is never touched; terminal rows
/// (<c>Sent</c>/<c>Skipped</c>/<c>Failed</c>) are excluded; the batch is bounded; and the re-enqueued
/// <see cref="SendOperationalAlertEmailJob"/> re-claims under the concurrency token, so a duplicate
/// Hangfire job for the same delivery converges on exactly one effective send. Idempotent and safe
/// to run repeatedly. Scheduled from <see cref="NotificationsModule.UseNotificationsRecurringJobs"/>.
/// </summary>
[AutomaticRetry(Attempts = 0)]
internal sealed class ReconcileStalledOperationalAlertEmailDeliveriesJob(
    NotificationsDbContext db,
    IClock clock,
    IBackgroundJobClient backgroundJobClient,
    ILogger<ReconcileStalledOperationalAlertEmailDeliveriesJob> logger)
{
    public const int PendingGraceMinutes = 15;
    public const int BatchSize = 200;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNowOffset();
        var pendingCutoff = now.AddMinutes(-PendingGraceMinutes);

        var stalled = await db.OperationalAlertEmailDeliveries
            .Where(d =>
                (d.Status == EmailDeliveryStatus.Pending && d.CreatedAt < pendingCutoff)
                || (d.Status == EmailDeliveryStatus.Sending
                    && (d.LeaseExpiresAt == null || d.LeaseExpiresAt <= now)))
            .OrderBy(d => d.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (stalled.Count == 0)
        {
            logger.LogInformation(
                "ReconcileStalledOperationalAlertEmailDeliveriesJob: nothing eligible (cutoff {Cutoff:u}).", pendingCutoff);
            return;
        }

        var enqueued = 0;
        var failed = 0;

        foreach (var delivery in stalled)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!delivery.HasAttemptsRemaining)
            {
                // Ticket 3L: ownership precedence. A send job may have re-claimed this lease-expired
                // row between our scan and now; only retire it when no live owner holds it per `now`.
                var failResult = delivery.MarkFailedIfNoLiveOwner("Delivery abandoned after repeated interruptions.", now);
                if (failResult.IsFailure)
                {
                    logger.LogInformation(
                        "ReconcileStalledOperationalAlertEmailDeliveriesJob: delivery for alert {AlertId} is owned by a live worker — leaving it.",
                        delivery.AlertId);
                    continue;
                }

                failed++;
                logger.LogError(
                    "ReconcileStalledOperationalAlertEmailDeliveriesJob: delivery for alert {AlertId} exceeded {MaxAttempts} attempts — marked permanently failed.",
                    delivery.AlertId, OperationalAlertEmailDelivery.MaxAttempts);
                continue;
            }

            backgroundJobClient.Enqueue<SendOperationalAlertEmailJob>(job => job.SendAsync(delivery.AlertId, null));
            enqueued++;
            logger.LogWarning(
                "ReconcileStalledOperationalAlertEmailDeliveriesJob: re-enqueued stalled {Status} delivery for alert {AlertId}.",
                delivery.Status, delivery.AlertId);
        }

        if (failed > 0)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Ticket 3L: an active send job re-claimed one of these rows between our scan and this
                // save. Its ownership is authoritative; drop our tracked failure writes and let the
                // next sweep re-evaluate from fresh state.
                logger.LogWarning(ex,
                    "ReconcileStalledOperationalAlertEmailDeliveriesJob: lost a race to an active send job; deferring failures to the next run.");
                db.ChangeTracker.Clear();
                failed = 0;
            }
        }

        logger.LogInformation(
            "ReconcileStalledOperationalAlertEmailDeliveriesJob complete: {Enqueued} re-enqueued, {Failed} permanently failed (cutoff {Cutoff:u}).",
            enqueued, failed, pendingCutoff);
    }
}
