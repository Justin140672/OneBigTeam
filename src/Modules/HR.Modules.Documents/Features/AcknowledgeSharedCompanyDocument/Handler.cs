using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;

internal sealed class AcknowledgeSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    IEmployeeAudienceReader audienceReader,
    IClock clock)
{
    public async Task<Result<AcknowledgeSharedCompanyDocumentResponse>> HandleAsync(
        AcknowledgeSharedCompanyDocumentRequest request,
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

        if (document is null)
            return Result.Failure<AcknowledgeSharedCompanyDocumentResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        if (!document.RequiresAcknowledgement)
            return Result.Failure<AcknowledgeSharedCompanyDocumentResponse>(
                Error.Validation("This document does not require acknowledgement."));

        var (myDepartmentId, myLocationId) = await audienceReader.GetEmployeeAudienceAsync(
            request.CompanyId, callerEmployeeId, cancellationToken);

        var inAudience =
            (document.AudienceDepartmentId is null && document.AudienceLocationId is null) ||
            (document.AudienceDepartmentId is not null && document.AudienceDepartmentId == myDepartmentId) ||
            (document.AudienceLocationId is not null && document.AudienceLocationId == myLocationId);

        if (!inAudience)
            return Result.Failure<AcknowledgeSharedCompanyDocumentResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        var now = clock.UtcNowOffset();

        // Idempotent: acknowledging the same version twice just returns the existing row rather
        // than creating a duplicate (the unique index on (document, employee, version) would
        // reject a second insert anyway, but this avoids the round-trip failure).
        var existing = await db.SharedCompanyDocumentAcknowledgements
            .FirstOrDefaultAsync(
                a => a.SharedCompanyDocumentId == document.Id &&
                     a.EmployeeId == callerEmployeeId &&
                     a.VersionNumber == document.VersionNumber,
                cancellationToken);

        if (existing is not null)
        {
            return Result.Success(new AcknowledgeSharedCompanyDocumentResponse(
                document.Id, document.VersionNumber, existing.AcknowledgedAt));
        }

        var acknowledgement = SharedCompanyDocumentAcknowledgement.Create(
            Guid.NewGuid(),
            request.CompanyId,
            document.Id,
            callerEmployeeId,
            document.VersionNumber,
            now);

        db.SharedCompanyDocumentAcknowledgements.Add(acknowledgement);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new AcknowledgeSharedCompanyDocumentResponse(
            document.Id, document.VersionNumber, acknowledgement.AcknowledgedAt));
    }
}
