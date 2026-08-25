using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.PurgeEligibleArchivedEmployeeDocuments;

/// <summary>
/// DOC-04: the ONLY code path in this module that physically deletes an employee-document DB row
/// and its stored file. Deliberately separate from Archive/Restore (DeleteEmployeeDocumentHandler
/// / RestoreEmployeeDocumentHandler never touch storage or remove rows) and gated at the endpoint
/// by a distinctly stronger permission (role:company-administrator, not the HrAdministrator-level
/// "employee:manage"/document.manage used everywhere else in this module) — see Endpoint.cs.
///
/// Only acts on documents that are IsArchived AND have been archived for at least
/// MinimumRetentionDays. This is an explicit, HR/company-admin-triggered action, not an
/// automatically scheduled job — see DocumentsModule.cs, which intentionally does NOT register a
/// recurring job for this handler. Auto-purging real employee documents on a timer is exactly the
/// kind of silent, hard-to-reverse regression this ticket warns against; a human must trigger
/// each purge run.
/// </summary>
internal sealed class PurgeEligibleArchivedEmployeeDocumentsHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    // DOC-04: no existing company-settings mechanism in this module covers document retention, so
    // a fixed default is used, matching the ticket's suggested fallback. Documented here rather
    // than hidden in a magic number at the call site.
    public const int MinimumRetentionDays = 90;

    public async Task<Result<PurgeEligibleArchivedEmployeeDocumentsResponse>> HandleAsync(
        PurgeEligibleArchivedEmployeeDocumentsRequest request,
        Guid purgedBy,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var cutoff = now.AddDays(-MinimumRetentionDays);

        var eligible = await (
            from ed in db.EmployeeDocuments
            join d  in db.Documents     on ed.DocumentId    equals d.Id
            join dt in db.DocumentTypes on d.DocumentTypeId equals dt.Id
            where ed.CompanyId  == request.CompanyId
               && ed.IsArchived
               && ed.ArchivedAt != null
               && ed.ArchivedAt <= cutoff
            select new { ed, d, DocumentTypeName = dt.Name })
            .ToListAsync(cancellationToken);

        var purgedCount = 0;

        foreach (var row in eligible)
        {
            db.EmployeeDocuments.Remove(row.ed);

            var otherLinks = await db.EmployeeDocuments
                .AnyAsync(other => other.DocumentId == row.d.Id && other.Id != row.ed.Id, cancellationToken);

            string? storageKeyToDelete = null;

            if (!otherLinks)
            {
                storageKeyToDelete = row.d.StorageKey;
                db.Documents.Remove(row.d);
            }

            await db.SaveChangesAsync(cancellationToken);

            await auditPublisher.PublishAsync(new EmployeeDocumentPurgedAuditEvent(
                request.CompanyId,
                row.ed.Id,
                row.ed.EmployeeId,
                row.d.Title,
                row.DocumentTypeName,
                row.d.FileName,
                row.ed.ArchivedAt!.Value,
                purgedBy,
                now), cancellationToken);

            if (storageKeyToDelete is not null)
                await storage.DeleteAsync(storageKeyToDelete, cancellationToken);

            purgedCount++;
        }

        return Result.Success(new PurgeEligibleArchivedEmployeeDocumentsResponse(purgedCount));
    }
}
