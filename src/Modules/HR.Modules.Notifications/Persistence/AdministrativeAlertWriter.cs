using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Persistence;

/// <summary>
/// ADM-03: the single write path for administrative alerts. De-duplicates on
/// (CompanyId, DedupKey) among non-resolved alerts — a repeated failure folds into the existing
/// live alert; once an alert is resolved a subsequent identical failure starts a fresh one.
/// </summary>
internal sealed class AdministrativeAlertWriter(
    NotificationsDbContext dbContext,
    IAuditEventPublisher auditPublisher,
    IClock clock) : IAdministrativeAlertWriter
{
    public async Task RaiseAsync(RaiseAdministrativeAlertCommand command, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.AdministrativeAlerts
            .FirstOrDefaultAsync(
                a => a.CompanyId == command.CompanyId
                     && a.DedupKey == command.DedupKey
                     && a.Status != AdministrativeAlertStatus.Resolved,
                cancellationToken);

        Guid alertId;
        bool isRecurrence;

        if (existing is not null)
        {
            existing.RecordRecurrence(command.Severity, command.Summary, command.Detail, command.OccurredAt);
            alertId = existing.Id;
            isRecurrence = true;
        }
        else
        {
            var alert = AdministrativeAlert.Raise(Guid.NewGuid(), command, clock.UtcNowOffset());
            dbContext.AdministrativeAlerts.Add(alert);
            alertId = alert.Id;
            isRecurrence = false;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (!isRecurrence)
        {
            // ADM-03: lost a race on the partial unique (company_id, dedup_key) index — another
            // request created the live alert first. Reload it and fold this occurrence in.
            foreach (var entry in dbContext.ChangeTracker.Entries<AdministrativeAlert>().ToList())
                entry.State = EntityState.Detached;

            var winner = await dbContext.AdministrativeAlerts
                .FirstAsync(
                    a => a.CompanyId == command.CompanyId
                         && a.DedupKey == command.DedupKey
                         && a.Status != AdministrativeAlertStatus.Resolved,
                    cancellationToken);

            winner.RecordRecurrence(command.Severity, command.Summary, command.Detail, command.OccurredAt);
            alertId = winner.Id;
            isRecurrence = true;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(
            new AdministrativeAlertRaisedAuditEvent(
                command.CompanyId, alertId, command.Category, command.Severity,
                command.DedupKey, isRecurrence, command.OccurredAt),
            cancellationToken);
    }
}
