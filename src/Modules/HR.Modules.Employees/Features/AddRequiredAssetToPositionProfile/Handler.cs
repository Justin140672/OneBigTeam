using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Features.AddRequiredAssetToPositionProfile;

internal sealed class AddRequiredAssetHandler(
    EmployeesDbContext dbContext,
    IAssetCategoryReader assetCategoryReader,
    IClock clock,
    IAuditEventPublisher auditEventPublisher)
{
    public async Task<Result<AddRequiredAssetResponse>> HandleAsync(
        AddRequiredAssetRequest request,
        Guid actorEmployeeId,
        CancellationToken cancellationToken)
    {
        var profileExists = await dbContext.PositionProfiles
            .AnyAsync(
                p => p.Id == request.PositionProfileId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (!profileExists)
            return Result.Failure<AddRequiredAssetResponse>(
                Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));

        var assetCategoryExists = await assetCategoryReader.ExistsAsync(
            request.CompanyId, request.AssetCategoryId, cancellationToken);

        if (!assetCategoryExists)
            return Result.Failure<AddRequiredAssetResponse>(
                Error.NotFound($"Asset category '{request.AssetCategoryId}' was not found."));

        var duplicateExists = await dbContext.PositionProfileRequiredAssets
            .AnyAsync(
                a => a.PositionProfileId == request.PositionProfileId &&
                     a.AssetCategoryId == request.AssetCategoryId &&
                     a.IsActive,
                cancellationToken);

        if (duplicateExists)
            return Result.Failure<AddRequiredAssetResponse>(
                Error.Conflict("This asset category is already required for the position profile."));

        var now = clock.UtcNowOffset();

        var requiredAsset = PositionProfileRequiredAsset.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.PositionProfileId,
            request.AssetCategoryId,
            request.IsMandatory,
            request.Quantity,
            actorEmployeeId,
            now);

        dbContext.PositionProfileRequiredAssets.Add(requiredAsset);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new RequiredAssetAddedAuditEvent(
                request.CompanyId,
                request.PositionProfileId,
                requiredAsset.Id,
                request.AssetCategoryId,
                actorEmployeeId,
                now),
            cancellationToken);

        return Result.Success(new AddRequiredAssetResponse(
            requiredAsset.Id,
            requiredAsset.PositionProfileId,
            requiredAsset.AssetCategoryId,
            requiredAsset.IsMandatory,
            requiredAsset.Quantity));
    }
}
