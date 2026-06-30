using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Features.CreateAsset;
using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Features.RetireAsset;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class RetireAssetHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }

    private static async Task<(Guid assetId, Guid companyId)> SeedAssetAsync(
        AssetsDbContext db,
        AssetStatus? overrideStatus = null)
    {
        var companyId = Guid.NewGuid();
        var clock = new FakeClock(FixedUtcNow);

        var categoryHandler = new CreateAssetCategoryHandler(db, clock);
        var categoryResult = await categoryHandler.HandleAsync(new CreateAssetCategoryRequest
        {
            CompanyId = companyId,
            Name = "Electronics",
            Description = null
        }, CancellationToken.None);

        var assetHandler = new CreateAssetHandler(db, clock);
        var assetResult = await assetHandler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "ASSET-001",
            CategoryId = categoryResult.Value!.Id,
            Name = "Laptop",
            Manufacturer = null,
            Model = null,
            SerialNumber = null,
            PurchaseDate = null,
            PurchasePrice = null
        }, CancellationToken.None);

        var asset = await db.Assets.SingleAsync();

        if (overrideStatus == AssetStatus.Assigned)
            asset.MarkAssigned(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        else if (overrideStatus == AssetStatus.UnderRepair)
            asset.MarkUnderRepair(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));

        await db.SaveChangesAsync();

        return (asset.Id, companyId);
    }

    [Fact]
    public async Task HandleAsync_Retires_Asset()
    {
        await using var db = BuildContext();
        var (assetId, companyId) = await SeedAssetAsync(db);
        var handler = new RetireAssetHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new RetireAssetRequest
        {
            CompanyId = companyId,
            Id = assetId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.Assets.SingleAsync();
        Assert.Equal(AssetStatus.Retired, saved.Status);
    }

    [Fact]
    public async Task HandleAsync_Sets_UpdatedAt_On_Retirement()
    {
        await using var db = BuildContext();
        var (assetId, companyId) = await SeedAssetAsync(db);
        var handler = new RetireAssetHandler(db, new FakeClock(FixedUtcNow));

        await handler.HandleAsync(new RetireAssetRequest
        {
            CompanyId = companyId,
            Id = assetId
        }, CancellationToken.None);

        var saved = await db.Assets.SingleAsync();
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), saved.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_When_Asset_Not_Found()
    {
        await using var db = BuildContext();
        var handler = new RetireAssetHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new RetireAssetRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_When_Asset_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var (assetId, _) = await SeedAssetAsync(db);
        var handler = new RetireAssetHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new RetireAssetRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = assetId
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Asset_Is_Assigned()
    {
        await using var db = BuildContext();
        var (assetId, companyId) = await SeedAssetAsync(db, AssetStatus.Assigned);
        var handler = new RetireAssetHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new RetireAssetRequest
        {
            CompanyId = companyId,
            Id = assetId
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Can_Retire_Asset_UnderRepair()
    {
        await using var db = BuildContext();
        var (assetId, companyId) = await SeedAssetAsync(db, AssetStatus.UnderRepair);
        var handler = new RetireAssetHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new RetireAssetRequest
        {
            CompanyId = companyId,
            Id = assetId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.Assets.SingleAsync();
        Assert.Equal(AssetStatus.Retired, saved.Status);
    }
}
