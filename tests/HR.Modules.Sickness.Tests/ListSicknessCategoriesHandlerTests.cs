using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.ListSicknessCategories;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class ListSicknessCategoriesHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedOffset = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_All_Categories_For_Company_Including_Inactive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        db.SicknessCategories.Add(SicknessCategory.Create(Guid.NewGuid(), companyId, "Cold", 1, FixedOffset));
        db.SicknessCategories.Add(SicknessCategory.Create(Guid.NewGuid(), companyId, "Flu", 2, FixedOffset));
        db.SicknessCategories.Add(SicknessCategory.Create(Guid.NewGuid(), otherCompanyId, "Other", 1, FixedOffset));
        await db.SaveChangesAsync();

        var inactive = SicknessCategory.Create(Guid.NewGuid(), companyId, "Inactive", 3, FixedOffset);
        inactive.Deactivate(FixedOffset);
        db.SicknessCategories.Add(inactive);
        await db.SaveChangesAsync();

        var handler = new ListSicknessCategoriesHandler(db);

        var result = await handler.HandleAsync(new ListSicknessCategoriesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.Equal(companyId, r.CompanyId));
    }

    [Fact]
    public async Task HandleAsync_Returns_Categories_Ordered_By_DisplayOrder()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.SicknessCategories.Add(SicknessCategory.Create(Guid.NewGuid(), companyId, "Flu", 3, FixedOffset));
        db.SicknessCategories.Add(SicknessCategory.Create(Guid.NewGuid(), companyId, "Cold", 1, FixedOffset));
        db.SicknessCategories.Add(SicknessCategory.Create(Guid.NewGuid(), companyId, "Back Pain", 2, FixedOffset));
        await db.SaveChangesAsync();

        var handler = new ListSicknessCategoriesHandler(db);

        var result = await handler.HandleAsync(new ListSicknessCategoriesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal("Cold", result[0].Name);
        Assert.Equal("Back Pain", result[1].Name);
        Assert.Equal("Flu", result[2].Name);
    }

    [Fact]
    public async Task HandleAsync_Orders_Ties_By_DisplayOrder_Newest_First()
    {
        // Categories created via the UI without an explicit Display Order all default to 0 —
        // with no secondary sort, Postgres does not guarantee a stable tie order, so a newly
        // created category could sort anywhere among same-DisplayOrder rows and fall off a
        // paginated grid's first page. Newest-first as the tiebreaker keeps new rows visible.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var oldest = SicknessCategory.Create(Guid.NewGuid(), companyId, "Oldest", 0, FixedOffset);
        var middle = SicknessCategory.Create(Guid.NewGuid(), companyId, "Middle", 0, FixedOffset.AddMinutes(1));
        var newest = SicknessCategory.Create(Guid.NewGuid(), companyId, "Newest", 0, FixedOffset.AddMinutes(2));
        db.SicknessCategories.AddRange(oldest, middle, newest);
        await db.SaveChangesAsync();

        var handler = new ListSicknessCategoriesHandler(db);

        var result = await handler.HandleAsync(new ListSicknessCategoriesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal("Newest", result[0].Name);
        Assert.Equal("Middle", result[1].Name);
        Assert.Equal("Oldest", result[2].Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Categories()
    {
        await using var db = BuildContext();
        var handler = new ListSicknessCategoriesHandler(db);

        var result = await handler.HandleAsync(new ListSicknessCategoriesRequest { CompanyId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_Maps_All_Fields_Correctly()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();

        db.SicknessCategories.Add(SicknessCategory.Create(id, companyId, "Cold", 5, FixedOffset));
        await db.SaveChangesAsync();

        var handler = new ListSicknessCategoriesHandler(db);

        var result = await handler.HandleAsync(new ListSicknessCategoriesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Single(result);
        var item = result[0];
        Assert.Equal(id, item.Id);
        Assert.Equal(companyId, item.CompanyId);
        Assert.Equal("Cold", item.Name);
        Assert.Equal(5, item.DisplayOrder);
        Assert.True(item.IsActive);
        Assert.Equal(FixedOffset, item.CreatedAt);
        Assert.Equal(FixedOffset, item.UpdatedAt);
    }

    private static SicknessDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new SicknessDbContext(options);
    }
}
