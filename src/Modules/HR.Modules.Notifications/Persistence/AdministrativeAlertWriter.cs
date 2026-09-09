using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Modules.Notifications.Persistence;

/// <summary>
/// ADM-03: the single write path for administrative alerts. De-duplicates on
/// (CompanyId, DedupKey) among non-resolved alerts — a repeated failure folds into the existing
/// live alert; once an alert is resolved a subsequent identical failure starts a fresh one.
///
/// Follow-up C / F: when a <b>new</b> alert whose <see cref="RaiseAdministrativeAlertCommand.Reason"/>
/// is <see cref="AdministrativeAlertReason.MissingDocumentExport"/> opens, an internal-operations
/// notification email is queued exactly once (a recurrence of an already-open alert never queues
/// another; an identical failure after resolution opens a new alert and queues a fresh one). Ordinary
/// report-generation failures carry no Reason and are recorded without an email. The notification
/// delivery row is written in the same transaction as the alert, and the send job is enqueued only
/// after that commit succeeds — an email failure can never hide or roll back the alert.
/// </summary>
internal sealed class AdministrativeAlertWriter(
    NotificationsDbContext dbContext,
    IAuditEventPublisher auditPublisher,
    IBackgroundJobClient backgroundJobClient,
    IOptions<OperationalAlertEmailOptions> operationalAlertEmailOptions,
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
        bool queuedOperationsEmail = false;

        if (existing is not null)
        {
            existing.RecordRecurrence(
                command.Severity, command.Summary, command.Detail, command.OccurredAt, command.AffectedItemCount);
            alertId = existing.Id;
            isRecurrence = true;
        }
        else
        {
            var newId = Guid.NewGuid();
            var enriched = command.ActionUrl is null && BuildAdminAlertUrl(newId) is { } url
                ? command with { ActionUrl = url }
                : command;

            var alert = AdministrativeAlert.Raise(newId, enriched, clock.UtcNowOffset());
            dbContext.AdministrativeAlerts.Add(alert);
            alertId = alert.Id;
            isRecurrence = false;

            if (command.Reason == AdministrativeAlertReason.MissingDocumentExport)
            {
                dbContext.OperationalAlertEmailDeliveries.Add(
                    OperationalAlertEmailDelivery.Create(Guid.NewGuid(), alertId, command.CompanyId, clock.UtcNowOffset()));
                queuedOperationsEmail = true;
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (!isRecurrence)
        {
            // ADM-03: lost a race on the partial unique (company_id, dedup_key) index — another
            // request created the live alert first. Reload it and fold this occurrence in. The
            // detached delivery-row insert is dropped, so the race loser sends no email.
            foreach (var entry in dbContext.ChangeTracker.Entries().ToList())
                entry.State = EntityState.Detached;

            var winner = await dbContext.AdministrativeAlerts
                .FirstAsync(
                    a => a.CompanyId == command.CompanyId
                         && a.DedupKey == command.DedupKey
                         && a.Status != AdministrativeAlertStatus.Resolved,
                    cancellationToken);

            winner.RecordRecurrence(
                command.Severity, command.Summary, command.Detail, command.OccurredAt, command.AffectedItemCount);
            alertId = winner.Id;
            isRecurrence = true;
            queuedOperationsEmail = false;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(
            new AdministrativeAlertRaisedAuditEvent(
                command.CompanyId, alertId, command.Category, command.Severity,
                command.DedupKey, isRecurrence, command.OccurredAt),
            cancellationToken);

        if (queuedOperationsEmail && !isRecurrence)
        {
            backgroundJobClient.Enqueue<SendOperationalAlertEmailJob>(
                job => job.SendAsync(alertId, null));
        }
    }

    private string? BuildAdminAlertUrl(Guid alertId)
    {
        var baseUrl = operationalAlertEmailOptions.Value.AdminAppBaseUrl;
        return string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : $"{baseUrl.TrimEnd('/')}/operational-alerts/{alertId}";
    }
}
