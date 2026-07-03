using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.UpdateSicknessRecord;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class UpdateSicknessRecordHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 7, 1);
    private static readonly WorkingPattern DefaultPattern = WorkingPattern.Default;

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

    private static async Task<SicknessRecord> SeedOpenRecord(SicknessDbContext db, Guid companyId, Guid employeeId, Guid categoryId)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var record = SicknessRecord.Create(Guid.NewGuid(), companyId, employeeId, categoryId, StartDate, SicknessDayPart.FullDay, null, null, null, "Original notes", SicknessEvidenceStatus.NotRequired, now);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    private static async Task<SicknessRecord> SeedClosedRecord(SicknessDbContext db, Guid companyId, Guid employeeId, Guid categoryId)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var endDate = new DateOnly(2026, 7, 3);
        var record = SicknessRecord.Create(Guid.NewGuid(), companyId, employeeId, categoryId, StartDate, SicknessDayPart.FullDay, endDate, SicknessDayPart.FullDay, 3m, "Original notes", SicknessEvidenceStatus.NotRequired, now);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    private static UpdateSicknessRecordHandler BuildHandler(
        SicknessDbContext db,
        WorkingPattern? pattern = null,
        bool excludePublicHolidays = false,
        IReadOnlyCollection<DateOnly>? publicHolidays = null,
        FakeAuditEventPublisher? auditPublisher = null) =>
        new(db,
            new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(pattern ?? DefaultPattern),
            new FakeCompanySicknessSettingsReader(excludePublicHolidays),
            new FakePublicHolidayReader(publicHolidays),
            auditPublisher ?? new FakeAuditEventPublisher());

    [Fact]
    public async Task HandleAsync_Updates_Open_Record_Without_Recalculating_TotalDays()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var newCategoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);

        var result = await BuildHandler(db).HandleAsync(new UpdateSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            CategoryId = newCategoryId,
            StartDate = new DateOnly(2026, 7, 2),
            StartDayPart = SicknessDayPart.HalfDayAM,
            Notes = "Updated notes"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newCategoryId, result.Value!.CategoryId);
        Assert.Equal(new DateOnly(2026, 7, 2), result.Value.StartDate);
        Assert.Equal(SicknessDayPart.HalfDayAM, result.Value.StartDayPart);
        Assert.Equal("Updated notes", result.Value.Notes);
        Assert.Null(result.Value.TotalDays); // still open
    }

    [Fact]
    public async Task HandleAsync_Recalculates_TotalDays_For_Closed_Record()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedClosedRecord(db, companyId, employeeId, categoryId);

        // Change startDate to 2026-07-02 (Thu), endDate stays 2026-07-03 (Fri) = 2 days
        var result = await BuildHandler(db).HandleAsync(new UpdateSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            CategoryId = categoryId,
            StartDate = new DateOnly(2026, 7, 2),
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2m, result.Value!.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Record_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new UpdateSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Category_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);

        var result = await BuildHandler(db).HandleAsync(new UpdateSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            CategoryId = Guid.NewGuid(), // non-existent category
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_On_Success()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);
        var auditPublisher = new FakeAuditEventPublisher();

        var result = await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new UpdateSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(auditPublisher.PublishedEvents);
        var auditEvent = Assert.IsType<SicknessUpdatedAuditEvent>(auditPublisher.PublishedEvents[0]);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(record.Id, auditEvent.SicknessRecordId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_On_NotFound()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditEventPublisher();

        await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new UpdateSicknessRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.Empty(auditPublisher.PublishedEvents);
    }
}
