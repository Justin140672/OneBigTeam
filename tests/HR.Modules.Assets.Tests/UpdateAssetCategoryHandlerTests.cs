using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Features.UpdateAssetCategory;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class UpdateAssetCategoryHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Updates_AssetCategory()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Old Name", "Old Description");

        var categoryId = (await db.AssetCategories.SingleAsync()).Id;
        var handler = new UpdateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateAssetCategoryRequest
        {
            CompanyId = companyId,
            Id = categoryId,
            Name = "New Name",
            Description = "New Description"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", result.Value!.Name);
        Assert.Equal("New Description", result.Value.Description);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(categoryId, result.Value.Id);

        var saved = await db.AssetCategories.SingleAsync();
        Assert.Equal("New Name", saved.Name);
        Assert.Equal("New Description", saved.Description);
    }

    [Fact]
    public async Task HandleAsync_Clears_Description_When_Null()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Electronics", "Some description");

        var categoryId = (await db.AssetCategories.SingleAsync()).Id;
        var handler = new UpdateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateAssetCategoryRequest
        {
            CompanyId = companyId,
            Id = categoryId,
            Name = "Electronics",
            Description = null
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Description);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_When_Category_Not_Found()
    {
        await using var db = BuildContext();
        var handler = new UpdateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateAssetCategoryRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            Name = "Anything",
            Description = null
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_When_Category_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Electronics", null);

        var categoryId = (await db.AssetCategories.SingleAsync()).Id;
        var handler = new UpdateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateAssetCategoryRequest
        {
            CompanyId = Guid.NewGuid(), // different company
            Id = categoryId,
            Name = "Updated",
            Description = null
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Sets_UpdatedAt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Vehicles", null);

        var categoryId = (await db.AssetCategories.SingleAsync()).Id;
        var handler = new UpdateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateAssetCategoryRequest
        {
            CompanyId = companyId,
            Id = categoryId,
            Name = "Vehicles Updated",
            Description = null
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value!.UpdatedAt);
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
