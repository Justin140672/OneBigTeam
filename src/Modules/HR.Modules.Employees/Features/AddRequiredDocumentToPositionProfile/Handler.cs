using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.AddRequiredDocumentToPositionProfile;

internal sealed class AddRequiredDocumentHandler(
    EmployeesDbContext dbContext,
    IDocumentTypeReader documentTypeReader,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<AddRequiredDocumentResponse>> HandleAsync(
        AddRequiredDocumentRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var profileExists = await dbContext.PositionProfiles
            .AnyAsync(
                p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (!profileExists)
            return Result.Failure<AddRequiredDocumentResponse>(
                Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));

        var documentTypeExists = await documentTypeReader.ExistsAsync(
            request.CompanyId, request.DocumentTypeId, cancellationToken);

        if (!documentTypeExists)
            return Result.Failure<AddRequiredDocumentResponse>(
                Error.NotFound($"Document type '{request.DocumentTypeId}' was not found."));

        var duplicateExists = await dbContext.PositionProfileRequiredDocuments
            .AnyAsync(
                d => d.PositionProfileId == request.PositionProfileId &&
                     d.DocumentTypeId == request.DocumentTypeId &&
                     d.IsActive,
                cancellationToken);

        if (duplicateExists)
            return Result.Failure<AddRequiredDocumentResponse>(
                Error.Conflict("This document type is already required for the position profile."));

        var now = clock.UtcNowOffset();

        var requiredDocument = PositionProfileRequiredDocument.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.PositionProfileId,
            request.DocumentTypeId,
            request.IsMandatory,
            request.DueDaysAfterStart,
            request.RequiresExpiryDate,
            actorEmployeeId,
            now);

        dbContext.PositionProfileRequiredDocuments.Add(requiredDocument);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new RequiredDocumentAddedAuditEvent(
                request.CompanyId,
                request.PositionProfileId,
                requiredDocument.Id,
                request.DocumentTypeId,
                actorEmployeeId,
                now),
            cancellationToken);

        return Result.Success(new AddRequiredDocumentResponse(
            requiredDocument.Id,
            requiredDocument.PositionProfileId,
            requiredDocument.DocumentTypeId,
            requiredDocument.IsMandatory,
            requiredDocument.DueDaysAfterStart,
            requiredDocument.RequiresExpiryDate));
    }
}
