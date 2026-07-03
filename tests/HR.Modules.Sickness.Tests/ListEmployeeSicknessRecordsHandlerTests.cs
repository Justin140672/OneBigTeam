using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.ListEmployeeSicknessRecords;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class ListEmployeeSicknessRecordsHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

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

    private static async Task<SicknessRecord> SeedRecord(SicknessDbContext db, Guid companyId, Guid employeeId, Guid categoryId, DateOnly startDate, SicknessEvidenceStatus evidenceStatus = SicknessEvidenceStatus.NotRequired)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var record = SicknessRecord.Create(Guid.NewGuid(), companyId, employeeId, categoryId, startDate, SicknessDayPart.FullDay, null, null, null, null, evidenceStatus, now);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Records()
    {
        await using var db = BuildContext();

        var handler = new ListEmployeeSicknessRecordsHandler(db);
        var result = await handler.HandleAsync(new ListEmployeeSicknessRecordsRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Records);
    }

    [Fact]
    public async Task HandleAsync_Returns_Records_For_Employee_Ordered_By_StartDate_Descending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var older = await SeedRecord(db, companyId, employeeId, categoryId, new DateOnly(2026, 6, 1));
        var newer = await SeedRecord(db, companyId, employeeId, categoryId, new DateOnly(2026, 7, 1));

        var handler = new ListEmployeeSicknessRecordsHandler(db);
        var result = await handler.HandleAsync(new ListEmployeeSicknessRecordsRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Records.Count);
        Assert.Equal(newer.Id, result.Value.Records[0].Id);
        Assert.Equal(older.Id, result.Value.Records[1].Id);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Records_For_Different_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        await SeedRecord(db, companyId, Guid.NewGuid(), categoryId, new DateOnly(2026, 7, 1));

        var handler = new ListEmployeeSicknessRecordsHandler(db);
        var result = await handler.HandleAsync(new ListEmployeeSicknessRecordsRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid()
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Records);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Return_Records_For_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        await SeedRecord(db, companyId, employeeId, categoryId, new DateOnly(2026, 7, 1));

        var handler = new ListEmployeeSicknessRecordsHandler(db);
        var result = await handler.HandleAsync(new ListEmployeeSicknessRecordsRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = employeeId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Records);
    }

    [Fact]
    public async Task HandleAsync_Populates_EvidenceStatus_From_Record()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedRecord(db, companyId, employeeId, categoryId, new DateOnly(2026, 7, 1), SicknessEvidenceStatus.Pending);

        var handler = new ListEmployeeSicknessRecordsHandler(db);
        var result = await handler.HandleAsync(new ListEmployeeSicknessRecordsRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var summary = Assert.Single(result.Value!.Records);
        Assert.Equal(record.Id, summary.Id);
        Assert.Equal(SicknessEvidenceStatus.Pending, summary.EvidenceStatus);
    }
}
