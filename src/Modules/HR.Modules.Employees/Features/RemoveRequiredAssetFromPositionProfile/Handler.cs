using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.RemoveRequiredAssetFromPositionProfile;

internal sealed class RemoveRequiredAssetHandler(
    EmployeesDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result> HandleAsync(
        RemoveRequiredAssetRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var requiredAsset = await dbContext.PositionProfileRequiredAssets
            .SingleOrDefaultAsync(
                a => a.Id == request.Id &&
                     a.PositionProfileId == request.PositionProfileId &&
                     a.CompanyId == request.CompanyId &&
                     a.IsActive,
                cancellationToken);

        if (requiredAsset is null)
            return Result.Failure(
                Error.NotFound($"Required asset '{request.Id}' was not found on this position profile."));

        requiredAsset.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new RequiredAssetRemovedAuditEvent(
                request.CompanyId,
                request.PositionProfileId,
                requiredAsset.Id,
                requiredAsset.AssetCategoryId,
                actorEmployeeId,
                clock.UtcNowOffset()),
            cancellationToken);

        return Result.Success();
    }
}
