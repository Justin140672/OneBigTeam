using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.DeactivateDocumentType;

internal sealed class DeactivateDocumentTypeHandler(
    DocumentsDbContext db,
    IClock clock,
    IPositionProfileDocumentsReader positionProfileDocumentsReader)
{
    public async Task<Result> HandleAsync(
        DeactivateDocumentTypeRequest request,
        CancellationToken cancellationToken)
    {
        var documentType = await db.DocumentTypes
            .SingleOrDefaultAsync(
                dt => dt.Id == request.DocumentTypeId &&
                      dt.CompanyId == request.CompanyId &&
                      dt.IsActive,
                cancellationToken);

        if (documentType is null)
            return Result.Failure(Error.NotFound($"Document type '{request.DocumentTypeId}' was not found."));

        var activeDocumentCount = await db.Documents
            .CountAsync(
                d => d.DocumentTypeId == request.DocumentTypeId
                  && d.CompanyId == request.CompanyId
                  && d.Status == DocumentStatus.Active,
                cancellationToken);

        var requestedDocumentRequestCount = await db.DocumentRequests
            .CountAsync(
                r => r.DocumentTypeId == request.DocumentTypeId
                  && r.CompanyId == request.CompanyId
                  && r.Status == DocumentRequestStatus.Requested,
                cancellationToken);

        var activePositionProfileReferenceCount = await positionProfileDocumentsReader
            .CountActiveReferencesToDocumentTypeAsync(request.CompanyId, request.DocumentTypeId, cancellationToken);

        var usageSegments = new List<string>();
        if (activeDocumentCount > 0)
            usageSegments.Add($"{activeDocumentCount} active document{(activeDocumentCount == 1 ? "" : "s")}");
        if (requestedDocumentRequestCount > 0)
        {
            usageSegments.Add(
                $"{requestedDocumentRequestCount} requested document{(requestedDocumentRequestCount == 1 ? "" : "s")}");
        }
        if (activePositionProfileReferenceCount > 0)
        {
            usageSegments.Add(
                $"{activePositionProfileReferenceCount} active position profile" +
                $"{(activePositionProfileReferenceCount == 1 ? "" : "s")}");
        }

        if (usageSegments.Count > 0)
        {
            return Result.Failure(Error.Conflict(
                $"Cannot deactivate '{documentType.Name}' — it is required by/used on " +
                $"{string.Join(" and ", usageSegments)}."));
        }

        documentType.Deactivate(clock.UtcNowOffset());
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
