using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Jobs;

internal sealed class SharedCompanyDocumentAcknowledgementReminderJob(
    DocumentsDbContext db,
    SharedCompanyDocumentAudienceMatcher audienceMatcher,
    INotificationWriter notificationWriter,
    ITaskCreator taskCreator,
    IOpenTaskBySourceEntityReader openTaskReader,
    ICompanyAcknowledgementSettingsReader acknowledgementSettingsReader,
    IManagerReader managerReader,
    IEmployeeNameReader employeeNameReader,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // Only Published documents are considered — an Archived document is the closest thing
        // this codebase has to a "cancelled assignment", and skipping it here means reminders
        // stop automatically without needing any cross-module Tasks-state changes.
        var documents = await db.SharedCompanyDocuments
            .AsNoTracking()
            .Where(d => d.Status == SharedCompanyDocumentStatus.Published &&
                        d.RequiresAcknowledgement &&
                        d.AcknowledgementDueDate != null)
            .ToListAsync();

        foreach (var document in documents)
        {
            var eligibleEmployeeIds = await audienceMatcher.GetEligibleEmployeeIdsAsync(
                document.CompanyId, document.Id, CancellationToken.None);

            if (eligibleEmployeeIds.Count == 0)
                continue;

            var acknowledgedEmployeeIds = await db.SharedCompanyDocumentAcknowledgements
                .AsNoTracking()
                .Where(a => a.SharedCompanyDocumentId == document.Id && a.VersionNumber == document.VersionNumber)
                .Select(a => a.EmployeeId)
                .ToListAsync();

            var acknowledged = new HashSet<Guid>(acknowledgedEmployeeIds);
            var outstandingEmployeeIds = eligibleEmployeeIds.Where(id => !acknowledged.Contains(id)).ToList();

            var dueDate = document.AcknowledgementDueDate!.Value;

            var reminderIntervalDays = await acknowledgementSettingsReader.GetReminderIntervalDaysAsync(
                document.CompanyId, CancellationToken.None);

            // Overdue employees are escalated to their manager (one notification per manager per
            // document, listing all their overdue direct reports for that document) — collected
            // here as we walk the outstanding list below, then sent once after the per-employee loop.
            var overdueEmployeeIds = new List<Guid>();

            foreach (var employeeId in outstandingEmployeeIds)
            {
                // An employee who does not yet have an open Acknowledge task for this document has
                // not yet been engaged — this is the reconciliation path: it covers anyone whose
                // department/location/position change (or an audience-rule edit, or being newly
                // hired) brought them into the audience after Publish/UploadSharedCompanyDocumentVersion
                // already ran their one-time task-creation loop. They get their task and first
                // notice on this run, regardless of how far the due date is — not deferred until
                // the due-soon window below.
                var existingTaskId = await openTaskReader.GetOpenTaskIdForAssigneeAsync(
                    document.CompanyId, document.Id, employeeId, TaskActionType.Acknowledge, CancellationToken.None);

                Guid taskId;
                bool alreadyEngaged;

                if (existingTaskId is null)
                {
                    taskId = await taskCreator.CreateAsync(
                        document.CompanyId,
                        createdBy:          document.CreatedBy,
                        title:              $"Acknowledge: {document.Title} (v{document.VersionNumber})",
                        description:        $"Please read and acknowledge '{document.Title}'.",
                        priority:           TaskPriority.Medium,
                        source:             TaskSource.Document,
                        actionType:         TaskActionType.Acknowledge,
                        dueDate:            document.AcknowledgementDueDate,
                        assignedEmployeeId: employeeId,
                        assignedUserId:     employeeId,
                        sourceEntityId:     document.Id,
                        CancellationToken.None,
                        notifyAssignee:     false);
                    alreadyEngaged = false;
                }
                else
                {
                    taskId = existingTaskId.Value;
                    alreadyEngaged = true;
                }

                // Overdue always fires once the due date has passed. The reminder fires immediately
                // for a never-before-engaged employee (rather than waiting for the window below) —
                // this both gives prompt notice to anyone newly brought into the audience (department/
                // location/position change, new hire, or an audience-rule edit) and closes a
                // duplicate-task hole: without it, "alreadyEngaged" would stay false and a task would
                // be created again on every subsequent run until the window was finally reached.
                if (dueDate < today)
                {
                    overdueEmployeeIds.Add(employeeId);

                    await SendIfIntervalElapsedAsync(
                        document,
                        employeeId,
                        taskId,
                        NotificationType.SharedCompanyDocumentAcknowledgementOverdue,
                        "Overdue: document acknowledgement required",
                        $"Your acknowledgement of '{document.Title}' is now overdue. Please read and acknowledge it as soon as possible.",
                        NotificationPriority.High,
                        reminderIntervalDays,
                        now);
                }
                else if (!alreadyEngaged || dueDate <= today.AddDays(reminderIntervalDays))
                {
                    await SendIfIntervalElapsedAsync(
                        document,
                        employeeId,
                        taskId,
                        NotificationType.SharedCompanyDocumentAcknowledgementReminder,
                        "Reminder: document acknowledgement required",
                        $"Please read and acknowledge '{document.Title}' before it is due.",
                        NotificationPriority.Normal,
                        reminderIntervalDays,
                        now);
                }
            }

            if (overdueEmployeeIds.Count > 0)
                await EscalateToManagersAsync(document, overdueEmployeeIds, reminderIntervalDays, now);
        }
    }

    // One notification per manager per document, listing all of that manager's overdue direct
    // reports for this document — not one notification per (manager, report) pair. Dedup/interval
    // is keyed on document.Id rather than a task id, since a manager has no Acknowledge task of
    // their own to key on.
    private async Task EscalateToManagersAsync(
        SharedCompanyDocument document,
        IReadOnlyList<Guid> overdueEmployeeIds,
        int reminderIntervalDays,
        DateTimeOffset now)
    {
        var reportsByManager = new Dictionary<Guid, List<Guid>>();

        foreach (var employeeId in overdueEmployeeIds)
        {
            var managerId = await managerReader.GetManagerIdAsync(document.CompanyId, employeeId, CancellationToken.None);
            if (managerId is null)
                continue;

            if (!reportsByManager.TryGetValue(managerId.Value, out var reports))
                reportsByManager[managerId.Value] = reports = [];

            reports.Add(employeeId);
        }

        if (reportsByManager.Count == 0)
            return;

        var allReportIds = reportsByManager.Values.SelectMany(r => r).Distinct().ToList();
        var names = await employeeNameReader.GetNamesAsync(document.CompanyId, allReportIds, CancellationToken.None);

        foreach (var (managerId, reportIds) in reportsByManager)
        {
            var lastSentAt = await notificationWriter.GetLastSentAtAsync(
                managerId, document.Id, NotificationType.SharedCompanyDocumentManagerEscalation);

            if (lastSentAt is not null && now - lastSentAt.Value < TimeSpan.FromDays(reminderIntervalDays))
                continue;

            var reportNames = reportIds
                .Select(id => names.TryGetValue(id, out var n) ? n : "Unknown")
                .OrderBy(n => n, StringComparer.Ordinal);

            var plural = reportIds.Count == 1 ? "report has" : "reports have";
            var body = $"{reportIds.Count} of your {plural} not acknowledged '{document.Title}': {string.Join(", ", reportNames)}.";

            await notificationWriter.WriteAsync(
                Guid.NewGuid(),
                document.CompanyId,
                managerId,
                "Overdue: your reports' document acknowledgements",
                body,
                document.Id,
                NotificationType.SharedCompanyDocumentManagerEscalation,
                NotificationPriority.High,
                now);

            await auditPublisher.PublishAsync(new SharedCompanyDocumentManagerEscalationSentAuditEvent(
                document.CompanyId, document.Id, document.Title, managerId, reportIds.Count, now), CancellationToken.None);
        }
    }

    // Dedup is keyed on (employeeId, taskId, type), where taskId is the per-employee Acknowledge
    // task's own id — stable across job runs since a task isn't recreated once it exists, only its
    // notifications repeat. A reminder/overdue notification for a given employee+document can be
    // re-sent once the configured interval has elapsed since the last one, but not before.
    private async Task SendIfIntervalElapsedAsync(
        SharedCompanyDocument document,
        Guid employeeId,
        Guid taskId,
        NotificationType type,
        string title,
        string body,
        NotificationPriority priority,
        int reminderIntervalDays,
        DateTimeOffset now)
    {
        var lastSentAt = await notificationWriter.GetLastSentAtAsync(employeeId, taskId, type);

        if (lastSentAt is not null && now - lastSentAt.Value < TimeSpan.FromDays(reminderIntervalDays))
            return;

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            document.CompanyId,
            employeeId,
            title,
            body,
            taskId,
            type,
            priority,
            now);

        await auditPublisher.PublishAsync(new SharedCompanyDocumentReminderSentAuditEvent(
            document.CompanyId, document.Id, document.Title, employeeId, type.ToString(), now), CancellationToken.None);
    }
}
