using HR.Modules.Assets.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Features.RetireAsset;

internal sealed class RetireAssetHandler(AssetsDbContext db, IClock clock)
{
    public async Task<Result> HandleAsync(
        RetireAssetRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await db.Assets
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.CompanyId == request.CompanyId, cancellationToken);

        if (asset is null)
            return Result.Failure(Error.NotFound("Asset not found."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);

        try
        {
            asset.Retire(now);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.Conflict(ex.Message));
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
