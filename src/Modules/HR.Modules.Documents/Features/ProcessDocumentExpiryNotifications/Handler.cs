using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ProcessDocumentExpiryNotifications;

internal sealed class ProcessDocumentExpiryNotificationsHandler(
    DocumentsDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    private const int ExpirySoonThresholdDays = 30;

    public async Task<ProcessDocumentExpiryNotificationsResponse> HandleAsync(
        ProcessDocumentExpiryNotificationsRequest request,
        CancellationToken cancellationToken)
    {
        var now       = clock.UtcNowOffset();
        var today     = DateOnly.FromDateTime(clock.UtcNow);
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
                entity.MarkExpiredNotified(now);
                expiredCount++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ProcessDocumentExpiryNotificationsResponse(expiringSoonCount, expiredCount);
    }
}
