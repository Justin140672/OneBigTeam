using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.RecordSickness;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class RecordSicknessHandlerTests
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

    private static RecordSicknessHandler BuildHandler(
        SicknessDbContext db,
        WorkingPattern? pattern = null,
        bool excludePublicHolidays = false,
        IReadOnlyCollection<DateOnly>? publicHolidays = null,
        FakeAuditEventPublisher? auditPublisher = null,
        int fitNoteRequiredAfterDays = 7,
        FakeManagerReader? managerReader = null,
        FakeEmployeeNameReader? employeeNameReader = null,
        FakeNotificationWriter? notificationWriter = null,
        FakeIntegrationEventPublisher? eventPublisher = null)
    {
        var resolvedAuditPublisher = auditPublisher ?? new FakeAuditEventPublisher();
        return new RecordSicknessHandler(
            db,
            new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(pattern ?? DefaultPattern),
            new FakeCompanySicknessSettingsReader(excludePublicHolidays, fitNoteRequiredAfterDays),
            new FakePublicHolidayReader(publicHolidays),
            resolvedAuditPublisher,
            managerReader ?? new FakeManagerReader(),
            employeeNameReader ?? new FakeEmployeeNameReader(),
            notificationWriter ?? new FakeNotificationWriter(),
            new FitNoteEvidenceRequestService(
                db,
                eventPublisher ?? new FakeIntegrationEventPublisher(),
                resolvedAuditPublisher,
                new FakeTaskRescheduler()));
    }

    [Fact]
    public async Task HandleAsync_Creates_SicknessRecord_With_No_EndDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
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
        Assert.Equal(SicknessEvidenceStatus.Pending, result.Value.EvidenceStatus);
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

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
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

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
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
    public async Task HandleAsync_Calculates_TotalDays_For_HalfDay()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = new DateOnly(2026, 7, 1),
            StartDayPart = SicknessDayPart.HalfDayAM,
            EndDate = new DateOnly(2026, 7, 1),
            EndDayPart = SicknessDayPart.HalfDayAM
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(0.5m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Calculates_TotalDays_Across_Multiple_Working_Days()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
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
        Assert.Equal(3m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Weekend_Days_From_TotalDays()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = new DateOnly(2026, 7, 1),
            StartDayPart = SicknessDayPart.FullDay,
            EndDate = new DateOnly(2026, 7, 6),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await db.SicknessRecords.SingleAsync();
        Assert.Equal(4m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Public_Holidays_When_Setting_Is_Enabled()
    {
        var publicHolidays = new List<DateOnly> { new(2026, 7, 2) };

        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db, excludePublicHolidays: true, publicHolidays: publicHolidays)
            .HandleAsync(new RecordSicknessRequest
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
    public async Task HandleAsync_Does_Not_Exclude_Public_Holidays_When_Setting_Is_Disabled()
    {
        var publicHolidays = new List<DateOnly> { new(2026, 7, 2) };

        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db, excludePublicHolidays: false, publicHolidays: publicHolidays)
            .HandleAsync(new RecordSicknessRequest
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
        Assert.Equal(3m, saved.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Respects_Custom_Working_Pattern()
    {
        var pattern = new WorkingPattern(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday,
            8m);

        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db, pattern: pattern).HandleAsync(new RecordSicknessRequest
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

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
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
        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
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
        var categoryId = await SeedCategory(db, Guid.NewGuid());

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
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
    public async Task HandleAsync_Returns_Conflict_When_Employee_Already_Has_Open_Record()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var firstResult = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(firstResult.IsSuccess);

        var secondResult = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
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
    public async Task HandleAsync_Allows_Null_Notes()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db).HandleAsync(new RecordSicknessRequest
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
    public async Task HandleAsync_Publishes_Audit_Event_On_Success()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var auditPublisher = new FakeAuditEventPublisher();

        var result = await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new RecordSicknessRequest
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

        Assert.Equal(employeeId, ((HR.SharedKernel.IAuditEvent)auditEvent).EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Category_Not_Found()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditEventPublisher();

        var result = await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new RecordSicknessRequest
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

        await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        auditPublisher.PublishedEvents.Clear();

        var result = await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new RecordSicknessRequest
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
    public async Task HandleAsync_Sets_EvidenceStatus_Pending_When_Open_Record_And_FitNote_Setting_Is_Set()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db, fitNoteRequiredAfterDays: 7).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SicknessEvidenceStatus.Pending, result.Value!.EvidenceStatus);
    }

    [Fact]
    public async Task HandleAsync_Sets_EvidenceStatus_NotRequired_When_TotalDays_Below_Threshold()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db, fitNoteRequiredAfterDays: 7).HandleAsync(new RecordSicknessRequest
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
        Assert.Equal(SicknessEvidenceStatus.NotRequired, result.Value!.EvidenceStatus);
    }

    [Fact]
    public async Task HandleAsync_Sets_EvidenceStatus_Pending_When_TotalDays_Meets_Threshold()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db, fitNoteRequiredAfterDays: 5).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = new DateOnly(2026, 7, 7),
            StartDayPart = SicknessDayPart.FullDay,
            EndDate = new DateOnly(2026, 7, 14),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SicknessEvidenceStatus.Pending, result.Value!.EvidenceStatus);
    }

    [Fact]
    public async Task HandleAsync_CreatesEvidenceRequest_Immediately_ForBackdatedOpenAbsence_AlreadyOverThreshold()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var result = await BuildHandler(db, fitNoteRequiredAfterDays: 7).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            StartDate = new DateOnly(2026, 6, 20),
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var request = await db.SicknessEvidenceRequests.SingleAsync();
        Assert.Equal(result.Value!.Id, request.SicknessRecordId);
    }

    [Fact]
    public async Task HandleAsync_CreatesEvidenceRequest_Immediately_ForClosedBackdatedImportedAbsence()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var start = new DateOnly(2026, 6, 1);
        var end = new DateOnly(2026, 6, 10);
        var result = await BuildHandler(db, fitNoteRequiredAfterDays: 7).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            StartDate = start,
            StartDayPart = SicknessDayPart.FullDay,
            EndDate = end,
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var request = await db.SicknessEvidenceRequests.SingleAsync();
        Assert.Equal(result.Value!.Id, request.SicknessRecordId);
        Assert.Equal(end.AddDays(7), request.DueDate);
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
            .HandleAsync(new RecordSicknessRequest
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
            .HandleAsync(new RecordSicknessRequest
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

    [Fact]
    public async Task HandleAsync_Audit_ActorEmployeeId_Reflects_Authenticated_Caller_Not_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var auditPublisher = new FakeAuditEventPublisher();

        var result = await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay,
            ActorEmployeeId = actorId
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.IsType<SicknessRecordedAuditEvent>(Assert.Single(auditPublisher.PublishedEvents));
        Assert.Equal(actorId, auditEvent.ActorEmployeeIdValue);
        Assert.Equal(actorId, ((IAuditEvent)auditEvent).ActorEmployeeId);
        Assert.NotEqual(employeeId, ((IAuditEvent)auditEvent).ActorEmployeeId);
        Assert.Equal(employeeId, ((IAuditEvent)auditEvent).EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Audit_ActorEmployeeId_Is_Null_When_Not_Supplied()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var auditPublisher = new FakeAuditEventPublisher();

        var result = await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.IsType<SicknessRecordedAuditEvent>(Assert.Single(auditPublisher.PublishedEvents));
        Assert.Null(auditEvent.ActorEmployeeIdValue);
    }

    [Fact]
    public async Task HandleAsync_Audit_Event_Does_Not_Contain_Notes_Free_Text()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var auditPublisher = new FakeAuditEventPublisher();
        const string sensitiveNotes = "DistinctiveSensitiveHealthDetail-Migraine-Diagnosis";

        var result = await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new RecordSicknessRequest
        {
            CompanyId = companyId,
            EmployeeId = Guid.NewGuid(),
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay,
            Notes = sensitiveNotes
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.Single(auditPublisher.PublishedEvents);
        var serialized = System.Text.Json.JsonSerializer.Serialize(auditEvent);
        Assert.DoesNotContain(sensitiveNotes, serialized);
    }
}
