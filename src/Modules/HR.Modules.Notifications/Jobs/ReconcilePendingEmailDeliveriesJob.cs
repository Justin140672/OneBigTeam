using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Jobs;

/// <summary>
/// OBT-REM-12: periodic reconciliation for EmailDelivery rows stuck in <see cref="EmailDeliveryStatus.Pending"/>
/// because the Hangfire enqueue that should have followed their commit (in
/// <see cref="NotificationWriter"/>) never happened or was lost (process crash between commit and
/// enqueue, Hangfire outage, etc.).
///
/// Safety controls:
/// <list type="bullet">
///   <item><b>Grace period.</b> Only rows older than <see cref="GraceMinutes"/> are considered — a
///   delivery that is merely mid-flight on its normal, first-attempt path is never touched.</item>
///   <item><b>Terminal states excluded.</b> Only Pending rows are selected; Sent, Skipped and
///   permanently Failed rows are never re-enqueued (they must not be repeatedly retried).</item>
///   <item><b>Tenant-safe, paginated, bounded.</b> Processed per company, capped at
///   <see cref="BatchSizePerCompany"/> rows per company per run, so one very backlogged tenant can
///   never starve reconciliation for the rest and each run does bounded work.</item>
///   <item><b>Idempotent re-enqueue.</b> EmailDeliveryJob itself no-ops on an already-Sent row and
///   (OBT-REM-12) backs off via the xmin concurrency token if another execution is already claiming
///   the row — so duplicate Hangfire jobs for the same delivery converge on exactly one effective
///   send.</item>
///   <item><b>Channel/company settings respected.</b> This job never re-implements the
///   scheduled-reminder or email-disabled checks — EmailDeliveryJob re-evaluates
///   ICompanyNotificationSettingsReader itself immediately before sending (SET-06), so a delivery
///   queued while a setting was one way is still evaluated correctly if the setting changed before
///   this reconciliation run picked it up.</item>
/// </list>
/// </summary>
[AutomaticRetry(Attempts = 0)]
internal sealed class ReconcilePendingEmailDeliveriesJob(
    NotificationsDbContext db,
    IClock clock,
    IBackgroundJobClient backgroundJobClient,
    ILogger<ReconcilePendingEmailDeliveriesJob> logger)
{
    public const int GraceMinutes = 15;
    public const int BatchSizePerCompany = 200;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.UtcNowOffset().AddMinutes(-GraceMinutes);

        var companyIds = await db.EmailDeliveries
            .Where(d => d.Status == EmailDeliveryStatus.Pending && d.CreatedAt < cutoff)
            .Select(d => d.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (companyIds.Count == 0)
        {
            logger.LogInformation(
                "ReconcilePendingEmailDeliveriesJob: nothing eligible (cutoff {Cutoff:u}).", cutoff);
            return;
        }

        var totalEnqueued = 0;

        foreach (var companyId in companyIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stale = await db.EmailDeliveries
                .AsNoTracking()
                .Where(d => d.CompanyId == companyId && d.Status == EmailDeliveryStatus.Pending && d.CreatedAt < cutoff)
                .OrderBy(d => d.CreatedAt)
                .Take(BatchSizePerCompany)
                .Select(d => d.NotificationId)
                .ToListAsync(cancellationToken);

            foreach (var notificationId in stale)
            {
                backgroundJobClient.Enqueue<EmailDeliveryJob>(job => job.SendAsync(notificationId, companyId, null));
            }

            totalEnqueued += stale.Count;

            logger.LogInformation(
                "ReconcilePendingEmailDeliveriesJob: re-enqueued {Count} stale pending delivery(ies) for company {CompanyId}.",
                stale.Count, companyId);
        }

        logger.LogInformation(
            "ReconcilePendingEmailDeliveriesJob complete: {CompanyCount} companies, {TotalEnqueued} delivery(ies) re-enqueued (cutoff {Cutoff:u}).",
            companyIds.Count, totalEnqueued, cutoff);
    }
}
