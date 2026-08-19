using HR.Modules.Tasks.Contracts;
using HR.Modules.Documents.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;

internal sealed class ProcessDocumentExpiryNotificationsHandler(
    DocumentsDbContext db,
    IClock clock,
    ICompanyTimeZoneReader timeZoneReader,
    IAuditEventPublisher auditPublisher,
    ITaskCreator taskCreator)
{
    private const int ExpirySoonThresholdDays = 30;
    private static readonly Guid SystemActor  = Guid.Empty;

    public async Task<ProcessDocumentExpiryNotificationsResponse> HandleAsync(
        ProcessDocumentExpiryNotificationsRequest request,
        CancellationToken cancellationToken)
    {
        var now       = clock.UtcNowOffset();
        var timeZoneId = await timeZoneReader.GetTimeZoneAsync(request.CompanyId, cancellationToken);
        var today     = clock.TodayIn(timeZoneId);
        var threshold = today.AddDays(ExpirySoonThresholdDays);

        // Read-only projection to gather event data — no change tracking needed.
        var candidates = await (
            from ed in db.EmployeeDocuments.AsNoTracking()
            join d  in db.Documents.AsNoTracking()     on ed.DocumentId    equals d.Id
            join dt in db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals dt.Id
            where ed.CompanyId == request.CompanyId
               && ed.ExpiryDate != null
               && ((ed.ExpiryDate >= today && ed.ExpiryDate <= threshold && ed.ExpiringSoonNotifiedAt == null)
                || (ed.ExpiryDate <  today && ed.ExpiredNotifiedAt == null))
            select new
            {
                EmployeeDocumentId = ed.Id,
                ed.EmployeeId,
                ed.ExpiryDate,
                d.Title,
                DocumentTypeName = dt.Name,
            }
        ).ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return new ProcessDocumentExpiryNotificationsResponse(0, 0);

        // Load entities for update with full tracking.
        var ids      = candidates.Select(c => c.EmployeeDocumentId).ToList();
        var entities = await db.EmployeeDocuments
            .Where(ed => ids.Contains(ed.Id))
            .ToDictionaryAsync(ed => ed.Id, cancellationToken);

        var expiringSoonCount = 0;
        var expiredCount      = 0;

        foreach (var c in candidates)
        {
            var entity = entities[c.EmployeeDocumentId];

            if (c.ExpiryDate >= today)
            {
                var daysUntil = c.ExpiryDate!.Value.DayNumber - today.DayNumber;

                await auditPublisher.PublishAsync(new DocumentExpiringSoonAuditEvent(
                    request.CompanyId,
                    c.EmployeeDocumentId,
                    c.EmployeeId,
                    c.Title,
                    c.DocumentTypeName,
                    c.ExpiryDate.Value,
                    daysUntil,
                    now), cancellationToken);

                await taskCreator.CreateAsync(
                    companyId:          request.CompanyId,
                    createdBy:          SystemActor,
                    title:              $"Document expiring soon: {c.Title}",
                    description:        $"'{c.Title}' ({c.DocumentTypeName}) expires in {daysUntil} day(s) on {c.ExpiryDate.Value:d}. Please arrange renewal.",
                    priority:           TaskPriority.High,
                    source:             TaskSource.Document,
                    actionType:         TaskActionType.Upload,
                    dueDate:            c.ExpiryDate.Value,
                    assignedEmployeeId: c.EmployeeId,
                    assignedUserId:     null,
                    sourceEntityId:     c.EmployeeDocumentId,
                    cancellationToken:  cancellationToken);

                entity.MarkExpiringSoonNotified(now);
                expiringSoonCount++;
            }
            else
            {
                await auditPublisher.PublishAsync(new DocumentExpiredAuditEvent(
                    request.CompanyId,
                    c.EmployeeDocumentId,
                    c.EmployeeId,
                    c.Title,
                    c.DocumentTypeName,
                    c.ExpiryDate!.Value,
                    now), cancellationToken);

                await taskCreator.CreateAsync(
                    companyId:          request.CompanyId,
                    createdBy:          SystemActor,
                    title:              $"Document expired: {c.Title}",
                    description:        $"'{c.Title}' ({c.DocumentTypeName}) expired on {c.ExpiryDate.Value:d}. Please collect an updated copy.",
                    priority:           TaskPriority.Critical,
                    source:             TaskSource.Document,
                    actionType:         TaskActionType.Upload,
                    dueDate:            today.AddDays(7),
                    assignedEmployeeId: c.EmployeeId,
                    assignedUserId:     null,
                    sourceEntityId:     c.EmployeeDocumentId,
                    cancellationToken:  cancellationToken);

                entity.MarkExpiredNotified(now);
                expiredCount++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ProcessDocumentExpiryNotificationsResponse(expiringSoonCount, expiredCount);
    }
}
