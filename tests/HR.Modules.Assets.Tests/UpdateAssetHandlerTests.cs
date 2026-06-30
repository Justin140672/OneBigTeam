using HR.Modules.Assets.Features.CreateAsset;
using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Features.UpdateAsset;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class UpdateAssetHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }

    private static async Task<(Guid categoryId, Guid companyId, Guid assetId)> SeedAssetAsync(
        AssetsDbContext db,
        string assetNumber = "ASSET-001",
        string name = "Laptop")
    {
        var companyId = Guid.NewGuid();
        var categoryHandler = new CreateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));
        var categoryResult = await categoryHandler.HandleAsync(new CreateAssetCategoryRequest
        {
            CompanyId = companyId,
            Name = "Electronics",
            Description = null
        }, CancellationToken.None);
        var categoryId = categoryResult.Value!.Id;

        var assetHandler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow));
        var assetResult = await assetHandler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = assetNumber,
            CategoryId = categoryId,
            Name = name
        }, CancellationToken.None);

        return (categoryId, companyId, assetResult.Value!.Id);
    }

    [Fact]
    public async Task HandleAsync_Updates_Asset_Fields()
    {
        await using var db = BuildContext();
        var (categoryId, companyId, assetId) = await SeedAssetAsync(db);
        var handler = new UpdateAssetHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateAssetRequest
        {
            CompanyId = companyId,
            Id = assetId,
            AssetNumber = "ASSET-UPDATED",
            CategoryId = categoryId,
            Name = "Updated Laptop",
            Manufacturer = "Dell",
            Model = "XPS 15",
            SerialNumber = "SN999",
            PurchaseDate = new DateOnly(2025, 3, 1),
            PurchasePrice = 2000m
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ASSET-UPDATED", result.Value!.AssetNumber);
        Assert.Equal("Updated Laptop", result.Value.Name);
        Assert.Equal("Dell", result.Value.Manufacturer);
        Assert.Equal("XPS 15", result.Value.Model);
        Assert.Equal("SN999", result.Value.SerialNumber);
        Assert.Equal(new DateOnly(2025, 3, 1), result.Value.PurchaseDate);
        Assert.Equal(2000m, result.Value.PurchasePrice);
    }

    [Fact]
    public async Task HandleAsync_Clears_Optional_Fields_When_Null()
    {
        await using var db = BuildContext();
        var (categoryId, companyId, assetId) = await SeedAssetAsync(db);
        var handler = new UpdateAssetHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateAssetRequest
        {
            CompanyId = companyId,
            Id = assetId,
            AssetNumber = "ASSET-001",
            CategoryId = categoryId,
            Name = "Laptop",
            Manufacturer = null,
            Model = null,
            SerialNumber = null,
            PurchaseDate = null,
            PurchasePrice = null
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Manufacturer);
        Assert.Null(result.Value.Model);
        Assert.Null(result.Value.SerialNumber);
        Assert.Null(result.Value.PurchaseDate);
        Assert.Null(result.Value.PurchasePrice);
    }

    [Fact]
    public async Task HandleAsync_Sets_UpdatedAt()
    {
        await using var db = BuildContext();
        var (categoryId, companyId, assetId) = await SeedAssetAsync(db);
        var updateTime = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var handler = new UpdateAssetHandler(db, new FakeClock(updateTime));

        var result = await handler.HandleAsync(new UpdateAssetRequest
        {
            CompanyId = companyId,
            Id = assetId,
            AssetNumber = "ASSET-001",
            CategoryId = categoryId,
            Name = "Laptop"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(updateTime, TimeSpan.Zero), result.Value!.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Asset_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new UpdateAssetHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateAssetRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            AssetNumber = "ASSET-001",
            CategoryId = Guid.NewGuid(),
            Name = "Ghost Asset"
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Asset_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var (categoryId, _, assetId) = await SeedAssetAsync(db);
        var handler = new UpdateAssetHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateAssetRequest
        {
            CompanyId = Guid.NewGuid(), // different company
            Id = assetId,
            AssetNumber = "ASSET-001",
            CategoryId = categoryId,
            Name = "Laptop"
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_AssetNumber_Belongs_To_Another_Asset()
    {
        await using var db = BuildContext();
        var (categoryId, companyId, assetId) = await SeedAssetAsync(db, "ASSET-001");

        // Create a second asset with a different number
        var assetHandler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow));
        await assetHandler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "ASSET-002",
            CategoryId = categoryId,
            Name = "Monitor"
        }, CancellationToken.None);

        var handler = new UpdateAssetHandler(db, new FakeClock(FixedUtcNow));

        // Try to rename ASSET-001 to ASSET-002 (conflict)
        var result = await handler.HandleAsync(new UpdateAssetRequest
        {
            CompanyId = companyId,
            Id = assetId,
            AssetNumber = "ASSET-002",
            CategoryId = categoryId,
            Name = "Laptop"
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_AssetNumber_On_Same_Asset()
    {
        await using var db = BuildContext();
        var (categoryId, companyId, assetId) = await SeedAssetAsync(db, "ASSET-001");
        var handler = new UpdateAssetHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateAssetRequest
        {
            CompanyId = companyId,
            Id = assetId,
            AssetNumber = "ASSET-001", // same number, same asset — no conflict
            CategoryId = categoryId,
            Name = "Updated Name"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var (_, companyId, assetId) = await SeedAssetAsync(db);
        var handler = new UpdateAssetHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateAssetRequest
        {
            CompanyId = companyId,
            Id = assetId,
            AssetNumber = "ASSET-001",
            CategoryId = Guid.NewGuid(), // non-existent category
            Name = "Laptop"
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var (_, companyId, assetId) = await SeedAssetAsync(db);

        // Seed a category for a different company
        var otherCompanyId = Guid.NewGuid();
        var otherCategoryHandler = new CreateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));
        var otherCategoryResult = await otherCategoryHandler.HandleAsync(new CreateAssetCategoryRequest
        {
            CompanyId = otherCompanyId,
            Name = "Other Company Category"
        }, CancellationToken.None);

        var handler = new UpdateAssetHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateAssetRequest
        {
            CompanyId = companyId,
            Id = assetId,
            AssetNumber = "ASSET-001",
            CategoryId = otherCategoryResult.Value!.Id,
            Name = "Laptop"
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Persists_Changes_To_Database()
    {
        await using var db = BuildContext();
        var (categoryId, companyId, assetId) = await SeedAssetAsync(db);
        var handler = new UpdateAssetHandler(db, new FakeClock(FixedUtcNow));

        await handler.HandleAsync(new UpdateAssetRequest
        {
            CompanyId = companyId,
            Id = assetId,
            AssetNumber = "ASSET-SAVED",
            CategoryId = categoryId,
            Name = "Saved Laptop"
        }, CancellationToken.None);

        var saved = await db.Assets.SingleAsync(a => a.Id == assetId);
        Assert.Equal("ASSET-SAVED", saved.AssetNumber);
        Assert.Equal("Saved Laptop", saved.Name);
    }
}
