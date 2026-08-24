using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.CreateAsset;

internal sealed class CreateAssetHandler(
    AssetsDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    ICompanyAssetNumberSettingsReader assetNumberSettingsReader,
    IAssetNumberGenerator assetNumberGenerator)
{
    public async Task<Result<CreateAssetResponse>> HandleAsync(
        CreateAssetRequest request,
        CancellationToken cancellationToken)
    {
        var categoryExists = await db.AssetCategories.AnyAsync(
            c => c.Id == request.CategoryId && c.CompanyId == request.CompanyId && c.IsActive,
            cancellationToken);

        if (!categoryExists)
            return Result.Failure<CreateAssetResponse>(
                Error.NotFound("Asset category not found."));

        var assetNumberMode = await assetNumberSettingsReader.GetModeAsync(request.CompanyId, cancellationToken);

        string assetNumber;

        if (string.IsNullOrWhiteSpace(request.AssetNumber))
        {
            if (assetNumberMode == AssetNumberMode.Manual)
            {
                return Result.Failure<CreateAssetResponse>(
                    Error.Validation("Asset number is required."));
            }

            // Automatic mode: caller didn't supply one, generate it via the atomic counter and
            // retry on conflict — mirrors CreateEmployeeHandler's own EmployeeNumber generation.
            const int maxAttempts = 5;
            var attempt = 0;
            while (true)
            {
                attempt++;
                assetNumber = await assetNumberGenerator.GenerateNextAsync(request.CompanyId, cancellationToken);

                var candidateExists = await db.Assets.AnyAsync(
                    a => a.CompanyId == request.CompanyId && a.AssetNumber == assetNumber,
                    cancellationToken);

                if (!candidateExists)
                    break;

                if (attempt >= maxAttempts)
                {
                    return Result.Failure<CreateAssetResponse>(
                        Error.Conflict("Could not generate a unique asset number after several attempts."));
                }
            }
        }
        else
        {
            if (assetNumberMode == AssetNumberMode.Automatic)
            {
                return Result.Failure<CreateAssetResponse>(
                    Error.Validation("Asset number is auto-generated for this company and must be left blank."));
            }

            assetNumber = request.AssetNumber.Trim();

            var exists = await db.Assets.AnyAsync(
                a => a.CompanyId == request.CompanyId && a.AssetNumber == assetNumber,
                cancellationToken);

            if (exists)
                return Result.Failure<CreateAssetResponse>(
                    Error.Conflict($"An asset with number '{assetNumber}' already exists."));
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        var entity = Asset.Create(
            Guid.NewGuid(), request.CompanyId, assetNumber, request.CategoryId,
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
