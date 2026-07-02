using HR.Modules.Sickness.Features.CreateSicknessCategory;
using HR.Modules.Sickness.Features.DeactivateSicknessCategory;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class DeactivateSicknessCategoryHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Deactivates_SicknessCategory()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Cold", 1);

        var categoryId = (await db.SicknessCategories.SingleAsync()).Id;
        var handler = new DeactivateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Id = categoryId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.SicknessCategories.SingleAsync();
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Sets_UpdatedAt_On_Deactivation()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedCategory(db, companyId, "Flu", 1);

        var categoryId = (await db.SicknessCategories.SingleAsync()).Id;
        var handler = new DeactivateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow));

        await handler.HandleAsync(new DeactivateSicknessCategoryRequest
        {
            CompanyId = companyId,
            Id = categoryId
        }, CancellationToken.None);

        var saved = await db.SicknessCategories.SingleAsync();
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), saved.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Failure_When_Category_Not_Found()
    {
        await using var db = BuildContext();
        var handler = new DeactivateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateSicknessCategoryRequest
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
        await SeedCategory(db, companyId, "Cold", 1);

        var categoryId = (await db.SicknessCategories.SingleAsync()).Id;
        var handler = new DeactivateSicknessCategoryHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new DeactivateSicknessCategoryRequest
        {
            CompanyId = Guid.NewGuid(), // different company
            Id = categoryId
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
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
