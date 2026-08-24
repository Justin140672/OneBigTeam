using HR.Modules.Assets.Features.CreateAsset;
using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Features.GetAsset;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class GetAssetHandlerTests
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

        var assetHandler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());
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
    public async Task HandleAsync_Returns_Asset_When_Found()
    {
        await using var db = BuildContext();
        var (categoryId, companyId, assetId) = await SeedAssetAsync(db, "ASSET-001", "Laptop");
        var handler = new GetAssetHandler(db);

        var result = await handler.HandleAsync(new GetAssetRequest
        {
            CompanyId = companyId,
            Id = assetId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(assetId, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal("ASSET-001", result.Value.AssetNumber);
        Assert.Equal(categoryId, result.Value.CategoryId);
        Assert.Equal("Laptop", result.Value.Name);
        Assert.Equal("Available", result.Value.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Asset_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new GetAssetHandler(db);

        var result = await handler.HandleAsync(new GetAssetRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Asset_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var (_, _, assetId) = await SeedAssetAsync(db);
        var handler = new GetAssetHandler(db);

        var result = await handler.HandleAsync(new GetAssetRequest
        {
            CompanyId = Guid.NewGuid(), // different company
            Id = assetId
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_All_Fields()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryHandler = new CreateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));
        var categoryResult = await categoryHandler.HandleAsync(new CreateAssetCategoryRequest
        {
            CompanyId = companyId,
            Name = "Electronics"
        }, CancellationToken.None);
        var categoryId = categoryResult.Value!.Id;

        var assetHandler = new CreateAssetHandler(db, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeCompanyAssetNumberSettingsReader(), new FakeAssetNumberGenerator());
        var assetResult = await assetHandler.HandleAsync(new CreateAssetRequest
        {
            CompanyId = companyId,
            AssetNumber = "ASSET-FULL",
            CategoryId = categoryId,
            Name = "MacBook",
            Manufacturer = "Apple",
            Model = "MacBook Pro",
            SerialNumber = "SN12345",
            PurchaseDate = new DateOnly(2025, 1, 15),
            PurchasePrice = 3000m
        }, CancellationToken.None);

        var handler = new GetAssetHandler(db);
        var result = await handler.HandleAsync(new GetAssetRequest
        {
            CompanyId = companyId,
            Id = assetResult.Value!.Id
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Apple", result.Value!.Manufacturer);
        Assert.Equal("MacBook Pro", result.Value.Model);
        Assert.Equal("SN12345", result.Value.SerialNumber);
        Assert.Equal(new DateOnly(2025, 1, 15), result.Value.PurchaseDate);
        Assert.Equal(3000m, result.Value.PurchasePrice);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value.CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_CategoryName_From_Joined_Category()
    {
        await using var db = BuildContext();
        var (_, companyId, assetId) = await SeedAssetAsync(db, "ASSET-CAT", "Monitor");
        var handler = new GetAssetHandler(db);

        var result = await handler.HandleAsync(new GetAssetRequest
        {
            CompanyId = companyId,
            Id = assetId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Electronics", result.Value!.CategoryName);
    }
}
