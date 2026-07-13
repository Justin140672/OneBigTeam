using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetPublishedSharedCompanyDocument;

internal sealed class GetPublishedSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    IEmployeeAudienceReader audienceReader)
{
    public async Task<Result<GetPublishedSharedCompanyDocumentResponse>> HandleAsync(
        GetPublishedSharedCompanyDocumentRequest request,
        Guid callerEmployeeId,
        CancellationToken cancellationToken)
    {
        var document = await db.SharedCompanyDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.Id == request.DocumentId &&
                     d.CompanyId == request.CompanyId &&
                     d.Status == SharedCompanyDocumentStatus.Published,
                cancellationToken);

        // Not found also covers "exists but not Published" and "exists but outside my audience"
        // — deliberately not distinguished, so a caller can't use this endpoint to probe for the
        // existence of documents that aren't meant for them.
        if (document is null)
            return Result.Failure<GetPublishedSharedCompanyDocumentResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        var (myDepartmentId, myLocationId) = await audienceReader.GetEmployeeAudienceAsync(
            request.CompanyId, callerEmployeeId, cancellationToken);

        var inAudience =
            (document.AudienceDepartmentId is null && document.AudienceLocationId is null) ||
            (document.AudienceDepartmentId is not null && document.AudienceDepartmentId == myDepartmentId) ||
            (document.AudienceLocationId is not null && document.AudienceLocationId == myLocationId);

        if (!inAudience)
            return Result.Failure<GetPublishedSharedCompanyDocumentResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        var category = await db.CompanyDocumentCategories
            .AsNoTracking()
            .Where(c => c.Id == document.CategoryId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);

        DateTimeOffset? myAcknowledgedAt = null;
        if (document.RequiresAcknowledgement)
        {
            myAcknowledgedAt = await db.SharedCompanyDocumentAcknowledgements
                .AsNoTracking()
                .Where(a =>
                    a.SharedCompanyDocumentId == document.Id &&
                    a.EmployeeId == callerEmployeeId &&
                    a.VersionNumber == document.VersionNumber)
                .Select(a => (DateTimeOffset?)a.AcknowledgedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return Result.Success(new GetPublishedSharedCompanyDocumentResponse(
            document.Id,
            document.Title,
            document.Description,
            category ?? "Unknown",
            document.EffectiveDate,
            document.RequiresAcknowledgement,
            myAcknowledgedAt));
    }
}
