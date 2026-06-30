using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Features.ListAssetCategories;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class ListAssetCategoriesHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedOffset = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Only_Active_Categories_For_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        db.AssetCategories.Add(AssetCategory.Create(Guid.NewGuid(), companyId, "Electronics", null, FixedOffset));
        db.AssetCategories.Add(AssetCategory.Create(Guid.NewGuid(), companyId, "Furniture", null, FixedOffset));
        db.AssetCategories.Add(AssetCategory.Create(Guid.NewGuid(), otherCompanyId, "Vehicles", null, FixedOffset));
        await db.SaveChangesAsync();

        // Deactivate one
        var inactive = AssetCategory.Create(Guid.NewGuid(), companyId, "Inactive", null, FixedOffset);
        inactive.Deactivate(FixedOffset);
        db.AssetCategories.Add(inactive);
        await db.SaveChangesAsync();

        var handler = new ListAssetCategoriesHandler(db);

        var result = await handler.HandleAsync(new ListAssetCategoriesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(companyId, r.CompanyId));
        Assert.All(result, r => Assert.True(r.IsActive));
    }

    [Fact]
    public async Task HandleAsync_Returns_Categories_Ordered_By_Name()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.AssetCategories.Add(AssetCategory.Create(Guid.NewGuid(), companyId, "Vehicles", null, FixedOffset));
        db.AssetCategories.Add(AssetCategory.Create(Guid.NewGuid(), companyId, "Computers", null, FixedOffset));
        db.AssetCategories.Add(AssetCategory.Create(Guid.NewGuid(), companyId, "Furniture", null, FixedOffset));
        await db.SaveChangesAsync();

        var handler = new ListAssetCategoriesHandler(db);

        var result = await handler.HandleAsync(new ListAssetCategoriesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal("Computers", result[0].Name);
        Assert.Equal("Furniture", result[1].Name);
        Assert.Equal("Vehicles", result[2].Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Active_Categories()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var handler = new ListAssetCategoriesHandler(db);

        var result = await handler.HandleAsync(new ListAssetCategoriesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_Maps_All_Fields_Correctly()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();

        db.AssetCategories.Add(AssetCategory.Create(id, companyId, "Electronics", "Electronic devices", FixedOffset));
        await db.SaveChangesAsync();

        var handler = new ListAssetCategoriesHandler(db);

        var result = await handler.HandleAsync(new ListAssetCategoriesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Single(result);
        var item = result[0];
        Assert.Equal(id, item.Id);
        Assert.Equal(companyId, item.CompanyId);
        Assert.Equal("Electronics", item.Name);
        Assert.Equal("Electronic devices", item.Description);
        Assert.True(item.IsActive);
        Assert.Equal(FixedOffset, item.CreatedAt);
        Assert.Equal(FixedOffset, item.UpdatedAt);
    }

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }
}
