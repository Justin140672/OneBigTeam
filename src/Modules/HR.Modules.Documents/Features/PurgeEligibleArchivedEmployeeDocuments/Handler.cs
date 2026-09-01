using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
    IAuditEventPublisher auditPublisher,
    ILegalHoldStatusReader legalHoldStatusReader,
    ILogger<PurgeEligibleArchivedEmployeeDocumentsHandler> logger)
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
        // NFR-07: a company under legal hold is exempt from all retention deletion until the hold
        // is lifted. Fail closed — never destroy data for a held company.
        if (await legalHoldStatusReader.IsUnderLegalHoldAsync(request.CompanyId, cancellationToken))
        {
            return Result.Failure<PurgeEligibleArchivedEmployeeDocumentsResponse>(Error.Conflict(
                "This company is under a legal hold. Document purge is suspended until the hold is lifted."));
        }

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

            // NFR-07 / NFR-08 storage-orphan finding: the DB row is already gone (committed above).
            // If the blob delete fails we must not fail the whole purge or lose visibility of the
            // orphan — log the exact storage key so an operator can remove it out of band. A re-run
            // of the purge will not revisit this key because its DB row no longer exists.
            if (storageKeyToDelete is not null)
            {
                try
                {
                    await storage.DeleteAsync(storageKeyToDelete, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Retention purge deleted document {DocumentId} for company {CompanyId} but failed to delete its stored file {StorageKey}. Manual storage cleanup required.",
                        row.d.Id, request.CompanyId, storageKeyToDelete);
                }
            }

            purgedCount++;
        }

        return Result.Success(new PurgeEligibleArchivedEmployeeDocumentsResponse(purgedCount));
    }
}
