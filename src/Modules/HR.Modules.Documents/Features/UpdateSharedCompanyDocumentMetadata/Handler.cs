using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UpdateSharedCompanyDocumentMetadata;

internal sealed class UpdateSharedCompanyDocumentMetadataHandler(
    DocumentsDbContext db,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<UpdateSharedCompanyDocumentMetadataResponse>> HandleAsync(
        UpdateSharedCompanyDocumentMetadataRequest request,
        Guid updatedBy,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<UpdateSharedCompanyDocumentMetadataResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        // Company ownership — the category must belong to the same company as the document,
        // same check UploadSharedCompanyDocument performs.
        var categoryExists = await db.CompanyDocumentCategories
            .AnyAsync(
                c => c.Id == request.CategoryId &&
                     c.CompanyId == request.CompanyId &&
                     c.IsActive,
                cancellationToken);

        if (!categoryExists)
        {
            return Result.Failure<UpdateSharedCompanyDocumentMetadataResponse>(
                Error.NotFound($"Document category '{request.CategoryId}' was not found."));
        }

        // Snapshot the editable fields before mutating, so the audit event only fires — and only
        // describes — an actual change. Audience/acknowledgement are intentionally excluded from
        // the snapshot: they aren't in the "Editable fields" list — audience now has its own
        // dedicated endpoint (UpdateSharedCompanyDocumentAudience).
        var before = new
        {
            document.Title,
            document.Description,
            document.CategoryId,
            document.EffectiveDate,
            document.ReviewDate,
        };

        var hasChanges =
            document.Title != request.Title.Trim() ||
            document.Description != (string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()) ||
            document.CategoryId != request.CategoryId ||
            document.EffectiveDate != request.EffectiveDate ||
            document.ReviewDate != request.ReviewDate;

        var now = clock.UtcNowOffset();

        document.UpdateDetails(
            request.Title,
            request.Description,
            request.CategoryId,
            request.EffectiveDate,
            request.ReviewDate,
            updatedBy,
            now);

        await db.SaveChangesAsync(cancellationToken);

        if (hasChanges)
        {
            var after = new
            {
                document.Title,
                document.Description,
                document.CategoryId,
                document.EffectiveDate,
                document.ReviewDate,
            };

            await auditPublisher.PublishAsync(new SharedCompanyDocumentMetadataUpdatedAuditEvent(
                document.CompanyId,
                document.Id,
                document.Title,
                before,
                after,
                updatedBy,
                now), cancellationToken);
        }

        return Result.Success(new UpdateSharedCompanyDocumentMetadataResponse(
            document.Id,
            document.CompanyId,
            document.Title,
            document.Description,
            document.CategoryId,
            document.VersionNumber,
            document.Status.ToString(),
            document.EffectiveDate,
            document.ReviewDate,
            document.UpdatedBy,
            document.UpdatedAt));
    }
}
