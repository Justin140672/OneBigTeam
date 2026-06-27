using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.RemoveRequiredDocumentFromPositionProfile;

internal sealed class RemoveRequiredDocumentHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result> HandleAsync(
        RemoveRequiredDocumentRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var requiredDocument = await dbContext.PositionProfileRequiredDocuments
            .SingleOrDefaultAsync(
                d => d.Id == request.Id &&
                     d.PositionProfileId == request.PositionProfileId &&
                     d.CompanyId == request.CompanyId &&
                     d.IsActive,
                cancellationToken);

        if (requiredDocument is null)
            return Result.Failure(
                Error.NotFound($"Required document '{request.Id}' was not found on this position profile."));

        requiredDocument.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new RequiredDocumentRemovedAuditEvent(
                request.CompanyId,
                request.PositionProfileId,
                requiredDocument.Id,
                requiredDocument.DocumentTypeId,
                actorEmployeeId,
                clock.UtcNowOffset()),
            cancellationToken);

        return Result.Success();
    }
}
