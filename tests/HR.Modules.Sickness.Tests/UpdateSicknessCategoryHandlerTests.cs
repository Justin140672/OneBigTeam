using HR.Modules.Sickness.Features.CreateSicknessCategory;
using HR.Modules.Sickness.Features.UpdateSicknessCategory;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class UpdateSicknessCategoryHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Updates_SicknessCategory()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Old Name", 1);

        var categoryId = (await db.SicknessCategories.SingleAsync()).Id;
        var handler = new UpdateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Id = categoryId,
            Name = "New Name",
            DisplayOrder = 5
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", result.Value!.Name);
        Assert.Equal(5, result.Value.DisplayOrder);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(categoryId, result.Value.Id);

        var saved = await db.SicknessCategories.SingleAsync();
        Assert.Equal("New Name", saved.Name);
        Assert.Equal(5, saved.DisplayOrder);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_When_Category_Not_Found()
    {
        await using var db = BuildContext();
        var handler = new UpdateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateSicknessCategoryRequest
        {
            CompanyId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            Name = "Anything",
            DisplayOrder = 1
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_When_Category_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Cold", 1);

        var categoryId = (await db.SicknessCategories.SingleAsync()).Id;
        var handler = new UpdateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateSicknessCategoryRequest
        {
            CompanyId = Guid.NewGuid(), // different company
            Id = categoryId,
            Name = "Updated",
            DisplayOrder = 1
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Name_Already_Used_By_Another_Category()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Cold", 1);
        await SeedCategory(db, companyId, "Flu", 2);

        var categories = await db.SicknessCategories.ToListAsync();
        var fluId = categories.First(c => c.Name == "Flu").Id;
        var handler = new UpdateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Id = fluId,
            Name = "Cold", // conflicts with existing
            DisplayOrder = 2
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Same_Name_For_Same_Category()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Cold", 1);

        var categoryId = (await db.SicknessCategories.SingleAsync()).Id;
        var handler = new UpdateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Id = categoryId,
            Name = "Cold", // same name, same record — ok
            DisplayOrder = 3
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Sets_UpdatedAt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Flu", 1);

        var categoryId = (await db.SicknessCategories.SingleAsync()).Id;
        var handler = new UpdateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new UpdateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Id = categoryId,
            Name = "Flu Updated",
            DisplayOrder = 1
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value!.UpdatedAt);
    }

    private static async Task SeedCategory(SicknessDbContext db, Guid companyId, string name, int displayOrder)
    {
        var createHandler = new CreateSicknessCategoryHandler(db, new FakeClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await createHandler.HandleAsync(new CreateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Name = name,
            DisplayOrder = displayOrder
        }, CancellationToken.None);
    }

    private static SicknessDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new SicknessDbContext(options);
    }
}
