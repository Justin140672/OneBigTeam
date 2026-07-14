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
    IClock clock)
{
    private const int ReminderWindowDays = 3;

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
            var outstandingEmployeeIds = eligibleEmployeeIds.Where(id => !acknowledged.Contains(id));

            var dueDate = document.AcknowledgementDueDate!.Value;

            foreach (var employeeId in outstandingEmployeeIds)
            {
                // An employee who was never engaged via either notification type for this document's
                // current version has not yet been given an acknowledgement task — this is the
                // reconciliation path: it covers anyone whose department/location/position change (or
                // an audience-rule edit, or being newly hired) brought them into the audience after
                // Publish/UploadSharedCompanyDocumentVersion already ran their one-time task-creation
                // loop. They get their task and first notice on this run, regardless of how far the
                // due date is — not deferred until the due-soon window below.
                var alreadyEngaged =
                    await notificationWriter.ExistsAsync(employeeId, document.Id, NotificationType.SharedCompanyDocumentAcknowledgementReminder) ||
                    await notificationWriter.ExistsAsync(employeeId, document.Id, NotificationType.SharedCompanyDocumentAcknowledgementOverdue);

                if (!alreadyEngaged)
                {
                    await taskCreator.CreateAsync(
                        document.CompanyId,
                        createdBy:          document.CreatedBy,
                        title:              $"Acknowledge: {document.Title} (v{document.VersionNumber})",
                        description:        $"Please read and acknowledge '{document.Title}'.",
                        priority:           TaskPriority.Medium,
                        source:             TaskSource.Document,
                        actionType:         TaskActionType.Acknowledge,
                        dueDate:            document.AcknowledgementDueDate,
                        assignedEmployeeId: employeeId,
                        assignedUserId:     null,
                        sourceEntityId:     document.Id,
                        CancellationToken.None);
                }

                // Overdue always fires once the due date has passed. The reminder fires immediately
                // for a never-before-engaged employee (rather than waiting for the window below) —
                // this both gives prompt notice to anyone newly brought into the audience (department/
                // location/position change, new hire, or an audience-rule edit) and closes a
                // duplicate-task hole: without it, "alreadyEngaged" would stay false and a task would
                // be created again on every subsequent run until the window was finally reached.
                if (dueDate < today)
                {
                    await SendIfNotAlreadySentAsync(
                        document,
                        employeeId,
                        NotificationType.SharedCompanyDocumentAcknowledgementOverdue,
                        "Overdue: document acknowledgement required",
                        $"Your acknowledgement of '{document.Title}' is now overdue. Please read and acknowledge it as soon as possible.",
                        NotificationPriority.High,
                        now);
                }
                else if (!alreadyEngaged || dueDate <= today.AddDays(ReminderWindowDays))
                {
                    await SendIfNotAlreadySentAsync(
                        document,
                        employeeId,
                        NotificationType.SharedCompanyDocumentAcknowledgementReminder,
                        "Reminder: document acknowledgement required",
                        $"Please read and acknowledge '{document.Title}' before it is due.",
                        NotificationPriority.Normal,
                        now);
                }
            }
        }
    }

    // Dedup is keyed on (employeeId, document.Id, type), not versioned. If a document is
    // re-versioned after a reminder was already sent for a prior version, this job will not
    // re-send a reminder for the new version — the outstanding-vs-acknowledged check above
    // already excludes anyone who acknowledged the current version, but ExistsAsync has no way
    // to distinguish "acknowledged and dismissed" from "reminder already sent for an older
    // version". Accepted as a known v1 limitation per INotificationWriter's fixed signature.
    private async Task SendIfNotAlreadySentAsync(
        SharedCompanyDocument document,
        Guid employeeId,
        NotificationType type,
        string title,
        string body,
        NotificationPriority priority,
        DateTimeOffset now)
    {
        var alreadySent = await notificationWriter.ExistsAsync(employeeId, document.Id, type);

        if (alreadySent) return;

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            document.CompanyId,
            employeeId,
            title,
            body,
            document.Id,
            type,
            priority,
            now);
    }
}
