using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.CreateAsset;

internal sealed class CreateAssetHandler(AssetsDbContext db, IClock clock, IAuditEventPublisher auditPublisher)
{
    public async Task<Result<CreateAssetResponse>> HandleAsync(
        CreateAssetRequest request,
        CancellationToken cancellationToken)
    {
        var exists = await db.Assets.AnyAsync(
            a => a.CompanyId == request.CompanyId && a.AssetNumber == request.AssetNumber,
            cancellationToken);

        if (exists)
            return Result.Failure<CreateAssetResponse>(
                Error.Conflict($"An asset with number '{request.AssetNumber}' already exists."));

        var categoryExists = await db.AssetCategories.AnyAsync(
            c => c.Id == request.CategoryId && c.CompanyId == request.CompanyId && c.IsActive,
            cancellationToken);

        if (!categoryExists)
            return Result.Failure<CreateAssetResponse>(
                Error.NotFound("Asset category not found."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = Asset.Create(
            Guid.NewGuid(), request.CompanyId, request.AssetNumber, request.CategoryId,
            request.Name, request.Manufacturer, request.Model,
            request.SerialNumber, request.PurchaseDate, request.PurchasePrice, now);

        db.Assets.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        await auditPublisher.PublishAsync(new AssetCreatedAuditEvent(
            entity.CompanyId,
            entity.Id,
            entity.AssetNumber,
            entity.Name,
            now), cancellationToken);

        return Result.Success(new CreateAssetResponse(
            entity.Id, entity.CompanyId, entity.AssetNumber, entity.CategoryId,
            entity.Name, entity.Manufacturer, entity.Model,
            entity.SerialNumber, entity.PurchaseDate, entity.PurchasePrice,
            entity.Status.ToString(), entity.CreatedAt, entity.UpdatedAt));
    }
}
