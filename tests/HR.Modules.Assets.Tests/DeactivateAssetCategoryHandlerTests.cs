using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Features.DeactivateAssetCategory;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class DeactivateAssetCategoryHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Deactivates_AssetCategory()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Electronics", null);

        var categoryId = (await db.AssetCategories.SingleAsync()).Id;
        var handler = new DeactivateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateAssetCategoryRequest
        {
            CompanyId = companyId,
            Id = categoryId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.AssetCategories.SingleAsync();
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Sets_UpdatedAt_On_Deactivation()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Vehicles", null);

        var categoryId = (await db.AssetCategories.SingleAsync()).Id;
        var handler = new DeactivateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));

        await handler.HandleAsync(new DeactivateAssetCategoryRequest
        {
            CompanyId = companyId,
            Id = categoryId
        }, CancellationToken.None);

        var saved = await db.AssetCategories.SingleAsync();
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), saved.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_When_Category_Not_Found()
    {
        await using var db = BuildContext();
        var handler = new DeactivateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateAssetCategoryRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_When_Category_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Furniture", null);

        var categoryId = (await db.AssetCategories.SingleAsync()).Id;
        var handler = new DeactivateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateAssetCategoryRequest
        {
            CompanyId = Guid.NewGuid(), // different company
            Id = categoryId
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static async Task SeedCategory(AssetsDbContext db, Guid companyId, string name, string? description)
    {
        var createHandler = new CreateAssetCategoryHandler(db, new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await createHandler.HandleAsync(new CreateAssetCategoryRequest
        {
            CompanyId = companyId,
            Name = name,
            Description = description
        }, CancellationToken.None);
    }

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }
}
