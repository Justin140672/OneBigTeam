using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.RecordMySickness;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class RecordMySicknessHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<Guid> SeedCategory(SicknessDbContext db, Guid companyId)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var category = SicknessCategory.Create(Guid.NewGuid(), companyId, "Cold", 1, now);
        db.SicknessCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    [Fact]
    public async Task HandleAsync_Creates_SicknessRecord()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var handler = new RecordMySicknessHandler(db, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay,
            Notes = "Feeling unwell"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal(categoryId, result.Value.CategoryId);
        Assert.Equal(SicknessStatus.Active, result.Value.Status);
        Assert.Equal(StartDate, result.Value.StartDate);
        Assert.Equal(SicknessDayPart.FullDay, result.Value.StartDayPart);
        Assert.Equal(SicknessEvidenceStatus.NotRequired, result.Value.EvidenceStatus);
        Assert.Equal("Feeling unwell", result.Value.Notes);

        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(employeeId, saved.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Sets_CreatedAt_And_UpdatedAt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var handler = new RecordMySicknessHandler(db, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.HalfDayAM
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var expected = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        Assert.Equal(expected, result.Value!.CreatedAt);
        Assert.Equal(expected, result.Value.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new RecordMySicknessHandler(db, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var categoryId = await SeedCategory(db, Guid.NewGuid()); // different company

        var handler = new RecordMySicknessHandler(db, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Null_Notes()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var handler = new RecordMySicknessHandler(db, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.HalfDayPM,
            Notes = null
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Notes);
    }
}
