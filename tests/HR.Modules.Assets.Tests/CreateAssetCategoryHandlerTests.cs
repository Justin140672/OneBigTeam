using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class CreateAssetCategoryHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_AssetCategory()
    {
        await using var db = BuildContext();
        var handler = new CreateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(new CreateAssetCategoryRequest
        {
            CompanyId = companyId,
            Name = "Electronics",
            Description = "Electronic devices"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal("Electronics", result.Value.Name);
        Assert.Equal("Electronic devices", result.Value.Description);
        Assert.True(result.Value.IsActive);

        var saved = await db.AssetCategories.SingleAsync();
        Assert.Equal("Electronics", saved.Name);
        Assert.Equal(companyId, saved.CompanyId);
    }

    [Fact]
    public async Task HandleAsync_Creates_AssetCategory_Without_Description()
    {
        await using var db = BuildContext();
        var handler = new CreateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(new CreateAssetCategoryRequest
        {
            CompanyId = companyId,
            Name = "Furniture",
            Description = null
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Description);
    }

    [Fact]
    public async Task HandleAsync_Sets_CreatedAt_And_UpdatedAt()
    {
        await using var db = BuildContext();
        var handler = new CreateAssetCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new CreateAssetCategoryRequest
        {
            CompanyId = Guid.NewGuid(),
            Name = "Vehicles",
            Description = null
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value!.CreatedAt);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value.UpdatedAt);
    }

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }
}
