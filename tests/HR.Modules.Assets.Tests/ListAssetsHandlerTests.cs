using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Features.ListAssets;
using HR.Modules.Assets.Persistence;
using HR.Modules.Assets.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Tests;

public class ListAssetsHandlerTests
{
    private static readonly DateTimeOffset FixedOffset = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_All_Assets_For_Company_When_No_Status_Filter()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        db.Assets.Add(Asset.Create(Guid.NewGuid(), companyId, "A001", categoryId, "Laptop", null, null, null, null, null, FixedOffset));
        db.Assets.Add(Asset.Create(Guid.NewGuid(), companyId, "A002", categoryId, "Desk", null, null, null, null, null, FixedOffset));
        db.Assets.Add(Asset.Create(Guid.NewGuid(), otherCompanyId, "A003", categoryId, "Chair", null, null, null, null, null, FixedOffset));
        await db.SaveChangesAsync();

        var handler = new ListAssetsHandler(db);
        var result = await handler.HandleAsync(new ListAssetsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(companyId, r.CompanyId));
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status_When_Status_Provided()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var asset1 = Asset.Create(Guid.NewGuid(), companyId, "A001", categoryId, "Laptop", null, null, null, null, null, FixedOffset);
        var asset2 = Asset.Create(Guid.NewGuid(), companyId, "A002", categoryId, "Desk", null, null, null, null, null, FixedOffset);
        db.Assets.Add(asset1);
        db.Assets.Add(asset2);
        await db.SaveChangesAsync();

        asset2.MarkAssigned(FixedOffset);
        await db.SaveChangesAsync();

        var handler = new ListAssetsHandler(db);
        var result = await handler.HandleAsync(
            new ListAssetsRequest { CompanyId = companyId, Status = AssetStatus.Available },
            CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("A001", result[0].AssetNumber);
        Assert.Equal(AssetStatus.Available, result[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_Assets_Ordered_By_AssetNumber()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        db.Assets.Add(Asset.Create(Guid.NewGuid(), companyId, "C003", categoryId, "Monitor", null, null, null, null, null, FixedOffset));
        db.Assets.Add(Asset.Create(Guid.NewGuid(), companyId, "A001", categoryId, "Laptop", null, null, null, null, null, FixedOffset));
        db.Assets.Add(Asset.Create(Guid.NewGuid(), companyId, "B002", categoryId, "Desk", null, null, null, null, null, FixedOffset));
        await db.SaveChangesAsync();

        var handler = new ListAssetsHandler(db);
        var result = await handler.HandleAsync(new ListAssetsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal("A001", result[0].AssetNumber);
        Assert.Equal("B002", result[1].AssetNumber);
        Assert.Equal("C003", result[2].AssetNumber);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Assets_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var handler = new ListAssetsHandler(db);
        var result = await handler.HandleAsync(new ListAssetsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Assets_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        db.Assets.Add(Asset.Create(Guid.NewGuid(), otherCompanyId, "A001", categoryId, "Laptop", null, null, null, null, null, FixedOffset));
        await db.SaveChangesAsync();

        var handler = new ListAssetsHandler(db);
        var result = await handler.HandleAsync(new ListAssetsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_Maps_All_Fields_Correctly()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var purchaseDate = new DateOnly(2025, 1, 15);

        db.Assets.Add(Asset.Create(id, companyId, "A001", categoryId, "Laptop Pro", "Dell", "XPS 15", "SN-12345", purchaseDate, 1999.99m, FixedOffset));
        await db.SaveChangesAsync();

        var handler = new ListAssetsHandler(db);
        var result = await handler.HandleAsync(new ListAssetsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Single(result);
        var item = result[0];
        Assert.Equal(id, item.Id);
        Assert.Equal(companyId, item.CompanyId);
        Assert.Equal("A001", item.AssetNumber);
        Assert.Equal(categoryId, item.CategoryId);
        Assert.Equal("Laptop Pro", item.Name);
        Assert.Equal("Dell", item.Manufacturer);
        Assert.Equal("XPS 15", item.Model);
        Assert.Equal("SN-12345", item.SerialNumber);
        Assert.Equal(purchaseDate, item.PurchaseDate);
        Assert.Equal(1999.99m, item.PurchasePrice);
        Assert.Equal(AssetStatus.Available, item.Status);
        Assert.Equal(FixedOffset, item.CreatedAt);
        Assert.Equal(FixedOffset, item.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Includes_CategoryName_When_Category_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category = AssetCategory.Create(Guid.NewGuid(), companyId, "Electronics", null, FixedOffset);
        db.AssetCategories.Add(category);
        db.Assets.Add(Asset.Create(Guid.NewGuid(), companyId, "A001", category.Id, "Laptop", null, null, null, null, null, FixedOffset));
        await db.SaveChangesAsync();

        var handler = new ListAssetsHandler(db);
        var result = await handler.HandleAsync(new ListAssetsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal("Electronics", Assert.Single(result).CategoryName);
    }

    [Fact]
    public async Task HandleAsync_Uses_Fallback_CategoryName_When_Category_Cannot_Be_Resolved()
    {
        // An asset must never silently disappear from the list just because its category can't
        // be resolved (e.g. a stale/orphaned CategoryId) — this asserts the left-join fallback,
        // not just "some assets are returned".
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var orphanedCategoryId = Guid.NewGuid();
        db.Assets.Add(Asset.Create(Guid.NewGuid(), companyId, "A001", orphanedCategoryId, "Laptop", null, null, null, null, null, FixedOffset));
        await db.SaveChangesAsync();

        var handler = new ListAssetsHandler(db);
        var result = await handler.HandleAsync(new ListAssetsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal("Unknown", Assert.Single(result).CategoryName);
    }

    private static AssetsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AssetsDbContext(options);
    }
}
