using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.GetSicknessRecord;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class GetSicknessRecordHandlerTests
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

    private static async Task<SicknessRecord> SeedRecord(SicknessDbContext db, Guid companyId, Guid employeeId, Guid categoryId)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var record = SicknessRecord.Create(Guid.NewGuid(), companyId, employeeId, categoryId, StartDate, SicknessDayPart.FullDay, null, null, null, "Feeling unwell", SicknessEvidenceStatus.NotRequired, now);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    [Fact]
    public async Task HandleAsync_Returns_Record_When_Found()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedRecord(db, companyId, employeeId, categoryId);

        var handler = new GetSicknessRecordHandler(db);
        var result = await handler.HandleAsync(new GetSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(record.Id, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal(categoryId, result.Value.CategoryId);
        Assert.Equal(SicknessStatus.Active, result.Value.Status);
        Assert.Equal(StartDate, result.Value.StartDate);
        Assert.Equal(SicknessDayPart.FullDay, result.Value.StartDayPart);
        Assert.Null(result.Value.EndDate);
        Assert.Null(result.Value.TotalDays);
        Assert.Equal("Feeling unwell", result.Value.Notes);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Record_Does_Not_Exist()
    {
        await using var db = BuildContext();

        var handler = new GetSicknessRecordHandler(db);
        var result = await handler.HandleAsync(new GetSicknessRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Id = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Record_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedRecord(db, companyId, employeeId, categoryId);

        var handler = new GetSicknessRecordHandler(db);
        var result = await handler.HandleAsync(new GetSicknessRecordRequest
        {
            CompanyId = Guid.NewGuid(), // different company
            EmployeeId = employeeId,
            Id = record.Id
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Record_Belongs_To_Different_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedRecord(db, companyId, Guid.NewGuid(), categoryId);

        var handler = new GetSicknessRecordHandler(db);
        var result = await handler.HandleAsync(new GetSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(), // different employee
            Id = record.Id
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
