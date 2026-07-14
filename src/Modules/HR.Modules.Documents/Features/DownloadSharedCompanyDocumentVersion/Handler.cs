using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.DownloadSharedCompanyDocumentVersion;

internal sealed class DownloadSharedCompanyDocumentVersionHandler(
    DocumentsDbContext db,
    IDocumentStorageService storage,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<Uri>> HandleAsync(
        DownloadSharedCompanyDocumentVersionRequest request,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<Uri>(Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        var version = await db.SharedCompanyDocumentVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                v => v.SharedCompanyDocumentId == document.Id
                    && v.VersionNumber == request.VersionNumber
                    && v.CompanyId == request.CompanyId,
                cancellationToken);

        if (version is null)
            return Result.Failure<Uri>(Error.NotFound($"Version '{request.VersionNumber}' of shared document '{request.DocumentId}' was not found."));

        var url = await storage.GetDownloadUrlAsync(version.FileReference, cancellationToken);

        // Record the download of this specific past version, with that version's own
        // VersionNumber — not the document's current VersionNumber — same audit event as
        // DownloadSharedCompanyDocument reuses for the current-file download.
        await auditPublisher.PublishAsync(new SharedCompanyDocumentDownloadedAuditEvent(
            document.CompanyId,
            document.Id,
            document.Title,
            document.FileName,
            version.VersionNumber,
            callerEmployeeId,
            clock.UtcNowOffset()), cancellationToken);

        return Result.Success(url);
    }
}
