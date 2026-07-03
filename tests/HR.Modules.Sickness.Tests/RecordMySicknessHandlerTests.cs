using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.RecordMySickness;
using HR.Modules.Sickness.Features.RecordSickness;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class RecordMySicknessHandlerTests
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

    private static RecordMySicknessHandler BuildHandler(
        SicknessDbContext db,
        WorkingPattern? pattern = null,
        bool excludePublicHolidays = false,
        IReadOnlyCollection<DateOnly>? publicHolidays = null,
        FakeAuditEventPublisher? auditPublisher = null,
        FakeManagerReader? managerReader = null,
        FakeEmployeeNameReader? employeeNameReader = null,
        FakeNotificationWriter? notificationWriter = null) =>
        new(db,
            new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(pattern ?? DefaultPattern),
            new FakeCompanySicknessSettingsReader(excludePublicHolidays),
            new FakePublicHolidayReader(publicHolidays),
            auditPublisher ?? new FakeAuditEventPublisher(),
            managerReader ?? new FakeManagerReader(),
            employeeNameReader ?? new FakeEmployeeNameReader(),
            notificationWriter ?? new FakeNotificationWriter());

    [Fact]
    public async Task HandleAsync_Creates_SicknessRecord_With_No_EndDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordMySicknessRequest
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
        Assert.Null(saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_TotalDays_Is_Null_When_No_EndDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Null(saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Calculates_TotalDays_For_FullDay_Single_Day()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = new DateOnly(2026, 7, 1),
            StartDayPart = SicknessDayPart.FullDay,
            EndDate = new DateOnly(2026, 7, 1),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(1m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Calculates_TotalDays_For_HalfDayPM()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = new DateOnly(2026, 7, 1),
            StartDayPart = SicknessDayPart.HalfDayPM,
            EndDate = new DateOnly(2026, 7, 1),
            EndDayPart = SicknessDayPart.HalfDayPM
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(0.5m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Public_Holidays_When_Setting_Is_Enabled()
    {
        var publicHolidays = new List<DateOnly> { new(2026, 7, 2) };

        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db, excludePublicHolidays: true, publicHolidays: publicHolidays)
            .HandleAsync(new RecordMySicknessRequest
            {
                CompanyId = companyId,
                EmployeeId = Guid.NewGuid(),
                CategoryId = categoryId,
                StartDate = new DateOnly(2026, 7, 1),
                StartDayPart = SicknessDayPart.FullDay,
                EndDate = new DateOnly(2026, 7, 3),
                EndDayPart = SicknessDayPart.FullDay
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(2m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Sets_CreatedAt_And_UpdatedAt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordMySicknessRequest
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
        var result = await BuildHandler(db).HandleAsync(new RecordMySicknessRequest
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

        var result = await BuildHandler(db).HandleAsync(new RecordMySicknessRequest
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

        var result = await BuildHandler(db).HandleAsync(new RecordMySicknessRequest
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

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Employee_Already_Has_Open_Record()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        // Create the first (open) record
        var firstResult = await BuildHandler(db).HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(firstResult.IsSuccess);

        // Attempt to create a second open record for the same employee
        var secondResult = await BuildHandler(db).HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            StartDate = StartDate.AddDays(1),
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(secondResult.IsFailure);
        Assert.Equal("conflict", secondResult.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_On_Success()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var auditPublisher = new FakeAuditEventPublisher();

        var result = await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(auditPublisher.PublishedEvents);

        var auditEvent = Assert.IsType<SicknessRecordedAuditEvent>(auditPublisher.PublishedEvents[0]);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(result.Value!.Id, auditEvent.SicknessRecordId);
        Assert.Equal(categoryId, auditEvent.CategoryId);
        Assert.Equal(StartDate, auditEvent.StartDate);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), auditEvent.OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Category_Not_Found()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditEventPublisher();

        var result = await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Conflict()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var auditPublisher = new FakeAuditEventPublisher();

        // First record succeeds
        await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        auditPublisher.PublishedEvents.Clear();

        // Second record conflicts
        var result = await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new RecordMySicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            StartDate = StartDate.AddDays(1),
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Notifies_Manager_When_Employee_Has_Manager()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var notificationWriter = new FakeNotificationWriter();
        var employeeNameReader = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [employeeId] = "Jane Doe" });

        var result = await BuildHandler(
                db,
                managerReader: new FakeManagerReader(managerId),
                employeeNameReader: employeeNameReader,
                notificationWriter: notificationWriter)
            .HandleAsync(new RecordMySicknessRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                CategoryId = categoryId,
                StartDate = StartDate,
                StartDayPart = SicknessDayPart.FullDay
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(notificationWriter.Written);
        var notification = notificationWriter.Written[0];
        Assert.Equal(managerId, notification.EmployeeId);
        Assert.Equal(companyId, notification.CompanyId);
        Assert.Equal(NotificationType.SicknessRecorded, notification.Type);
        Assert.Contains("Jane Doe", notification.Title);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Notify_When_Employee_Has_No_Manager()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var notificationWriter = new FakeNotificationWriter();

        var result = await BuildHandler(
                db,
                managerReader: new FakeManagerReader(null),
                notificationWriter: notificationWriter)
            .HandleAsync(new RecordMySicknessRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                CategoryId = categoryId,
                StartDate = StartDate,
                StartDayPart = SicknessDayPart.FullDay
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(notificationWriter.Written);
    }
}
