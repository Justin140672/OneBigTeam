using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.CloseSicknessRecord;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class CloseSicknessRecordHandlerTests
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
        var record = SicknessRecord.Create(Guid.NewGuid(), companyId, employeeId, categoryId, StartDate, SicknessDayPart.FullDay, null, null, null, null, SicknessEvidenceStatus.NotRequired, now);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    private static async Task<SicknessRecord> SeedClosedRecord(SicknessDbContext db, Guid companyId, Guid employeeId, Guid categoryId)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var endDate = new DateOnly(2026, 7, 3);
        var record = SicknessRecord.Create(Guid.NewGuid(), companyId, employeeId, categoryId, StartDate, SicknessDayPart.FullDay, endDate, SicknessDayPart.FullDay, 3m, null, SicknessEvidenceStatus.NotRequired, now);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    private static CloseSicknessRecordHandler BuildHandler(
        SicknessDbContext db,
        WorkingPattern? pattern = null,
        bool excludePublicHolidays = false,
        IReadOnlyCollection<DateOnly>? publicHolidays = null,
        FakeAuditEventPublisher? auditPublisher = null,
        // Deliberately a high default rather than the real production default (1) — most tests in
        // this file call BuildHandler without caring about fit-note/return-to-work behavior at
        // all, and a low default would spuriously trigger both for their (typically small)
        // totalDays, same as the old "null = never triggers" default did before these settings
        // became mandatory (see CompanySettings.FitNoteRequiredAfterDays). Tests that actually
        // exercise this behavior always pass an explicit low value.
        int fitNoteRequiredAfterDays = 9999,
        int returnToWorkRequiredAfterDays = 9999,
        FakeIntegrationEventPublisher? eventPublisher = null) =>
        new(db,
            new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(pattern ?? DefaultPattern),
            new FakeCompanySicknessSettingsReader(excludePublicHolidays, fitNoteRequiredAfterDays, returnToWorkRequiredAfterDays),
            new FakePublicHolidayReader(publicHolidays),
            auditPublisher ?? new FakeAuditEventPublisher(),
            eventPublisher ?? new FakeIntegrationEventPublisher());

    private static async Task<SicknessRecord> SeedOpenRecordWithEvidenceStatus(
        SicknessDbContext db, Guid companyId, Guid employeeId, Guid categoryId,
        SicknessEvidenceStatus evidenceStatus)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var record = SicknessRecord.Create(Guid.NewGuid(), companyId, employeeId, categoryId, StartDate, SicknessDayPart.FullDay, null, null, null, null, evidenceStatus, now);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    [Fact]
    public async Task HandleAsync_Closes_Record_And_Calculates_TotalDays()
    {
        // 2026-07-01 (Wed) to 2026-07-03 (Fri) = 3 working days
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);

        var result = await BuildHandler(db).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            EndDate = new DateOnly(2026, 7, 3),
            EndDayPart = SicknessDayPart.FullDay,
            ReturnToWorkDate = new DateOnly(2026, 7, 6)
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SicknessStatus.Closed, result.Value!.Status);
        Assert.Equal(new DateOnly(2026, 7, 3), result.Value.EndDate);
        Assert.Equal(SicknessDayPart.FullDay, result.Value.EndDayPart);
        Assert.Equal(new DateOnly(2026, 7, 6), result.Value.ReturnToWorkDate);
        Assert.Equal(3m, result.Value.TotalDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Record_Does_Not_Exist()
    {
        await using var db = BuildContext();

        var result = await BuildHandler(db).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            EndDate = new DateOnly(2026, 7, 3),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Record_Is_Already_Closed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedClosedRecord(db, companyId, employeeId, categoryId);

        var result = await BuildHandler(db).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            EndDate = new DateOnly(2026, 7, 5),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_EndDate_Before_StartDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);

        var result = await BuildHandler(db).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            EndDate = new DateOnly(2026, 6, 30), // before StartDate 2026-07-01
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Public_Holidays_When_Setting_Is_Enabled()
    {
        // 2026-07-01 to 2026-07-03 = 3 working days, but 2026-07-02 is a holiday → 2 days
        var publicHolidays = new List<DateOnly> { new(2026, 7, 2) };

        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);

        var result = await BuildHandler(db, excludePublicHolidays: true, publicHolidays: publicHolidays)
            .HandleAsync(new CloseSicknessRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                Id = record.Id,
                EndDate = new DateOnly(2026, 7, 3),
                EndDayPart = SicknessDayPart.FullDay
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2m, result.Value!.TotalDays);
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

        var result = await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            EndDate = new DateOnly(2026, 7, 3),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(auditPublisher.PublishedEvents);
        var auditEvent = Assert.IsType<SicknessClosedAuditEvent>(auditPublisher.PublishedEvents[0]);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(record.Id, auditEvent.SicknessRecordId);
        Assert.Equal(new DateOnly(2026, 7, 3), auditEvent.EndDate);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_On_NotFound()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditEventPublisher();

        await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Id = Guid.NewGuid(),
            EndDate = new DateOnly(2026, 7, 3),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_On_Conflict()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedClosedRecord(db, companyId, employeeId, categoryId);
        var auditPublisher = new FakeAuditEventPublisher();

        await BuildHandler(db, auditPublisher: auditPublisher).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            EndDate = new DateOnly(2026, 7, 5),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Sets_EvidenceStatus_Pending_When_TotalDays_Meets_Threshold_On_Close()
    {
        // StartDate = 2026-07-01, EndDate = 2026-07-03 = 3 working days, threshold = 3 → Pending
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);

        var result = await BuildHandler(db, fitNoteRequiredAfterDays: 3).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            EndDate = new DateOnly(2026, 7, 3),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SicknessEvidenceStatus.Pending, result.Value!.EvidenceStatus);
    }

    [Fact]
    public async Task HandleAsync_Sets_EvidenceStatus_NotRequired_When_TotalDays_Below_Threshold_On_Close()
    {
        // StartDate = 2026-07-01, EndDate = 2026-07-03 = 3 working days, threshold = 7 → NotRequired
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);

        var result = await BuildHandler(db, fitNoteRequiredAfterDays: 7).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            EndDate = new DateOnly(2026, 7, 3),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SicknessEvidenceStatus.NotRequired, result.Value!.EvidenceStatus);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Override_Received_EvidenceStatus_On_Close()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecordWithEvidenceStatus(db, companyId, employeeId, categoryId, SicknessEvidenceStatus.Received);

        var result = await BuildHandler(db, fitNoteRequiredAfterDays: 3).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            EndDate = new DateOnly(2026, 7, 3),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SicknessEvidenceStatus.Received, result.Value!.EvidenceStatus);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Override_Waived_EvidenceStatus_On_Close()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecordWithEvidenceStatus(db, companyId, employeeId, categoryId, SicknessEvidenceStatus.Waived);

        var result = await BuildHandler(db, fitNoteRequiredAfterDays: 3).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            EndDate = new DateOnly(2026, 7, 3),
            EndDayPart = SicknessDayPart.FullDay
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SicknessEvidenceStatus.Waived, result.Value!.EvidenceStatus);
    }

    [Fact]
    public async Task HandleAsync_Creates_ReturnToWorkReview_When_TotalDays_Meets_Threshold_On_Close()
    {
        // StartDate = 2026-07-01, EndDate = 2026-07-03 = 3 working days, threshold = 3 → review created
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);
        var returnToWorkDate = new DateOnly(2026, 7, 6);

        var result = await BuildHandler(db, returnToWorkRequiredAfterDays: 3).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            EndDate = new DateOnly(2026, 7, 3),
            EndDayPart = SicknessDayPart.FullDay,
            ReturnToWorkDate = returnToWorkDate
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var review = await db.ReturnToWorkReviews.SingleAsync(r => r.SicknessRecordId == record.Id);
        Assert.Equal(companyId, review.CompanyId);
        Assert.Equal(employeeId, review.EmployeeId);
        Assert.Equal(record.Id, review.SicknessRecordId);
        Assert.Equal(returnToWorkDate, review.DueDate);
    }

    [Fact]
    public async Task HandleAsync_ReturnToWorkReview_DueDate_Falls_Back_To_EndDate_When_No_ReturnToWorkDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);
        var endDate = new DateOnly(2026, 7, 3);

        var result = await BuildHandler(db, returnToWorkRequiredAfterDays: 3).HandleAsync(new CloseSicknessRecordRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = record.Id,
            EndDate = endDate,
            EndDayPart = SicknessDayPart.FullDay,
            ReturnToWorkDate = null
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var review = await db.ReturnToWorkReviews.SingleAsync(r => r.SicknessRecordId == record.Id);
        Assert.Equal(endDate, review.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Publishes_ReturnToWorkReviewRequired_IntegrationEvent_When_Threshold_Met()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);
        var eventPublisher = new FakeIntegrationEventPublisher();
        var returnToWorkDate = new DateOnly(2026, 7, 6);

        var result = await BuildHandler(db, returnToWorkRequiredAfterDays: 3, eventPublisher: eventPublisher)
            .HandleAsync(new CloseSicknessRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                Id = record.Id,
                EndDate = new DateOnly(2026, 7, 3),
                EndDayPart = SicknessDayPart.FullDay,
                ReturnToWorkDate = returnToWorkDate
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(eventPublisher.PublishedEvents);
        var integrationEvent = Assert.IsType<ReturnToWorkReviewRequiredIntegrationEvent>(eventPublisher.PublishedEvents[0]);
        Assert.Equal(companyId, integrationEvent.CompanyId);
        Assert.Equal(employeeId, integrationEvent.EmployeeId);
        Assert.Equal(record.Id, integrationEvent.SicknessRecordId);
        Assert.Equal(returnToWorkDate, integrationEvent.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Publishes_ReturnToWorkReviewRequired_AuditEvent_When_Threshold_Met()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);
        var auditPublisher = new FakeAuditEventPublisher();

        var result = await BuildHandler(db, returnToWorkRequiredAfterDays: 3, auditPublisher: auditPublisher)
            .HandleAsync(new CloseSicknessRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                Id = record.Id,
                EndDate = new DateOnly(2026, 7, 3),
                EndDayPart = SicknessDayPart.FullDay
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(auditPublisher.PublishedEvents, e => e is ReturnToWorkReviewRequiredAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Create_ReturnToWorkReview_When_TotalDays_Below_Threshold()
    {
        // StartDate = 2026-07-01, EndDate = 2026-07-03 = 3 working days, threshold = 7 → no review
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var record = await SeedOpenRecord(db, companyId, employeeId, categoryId);
        var eventPublisher = new FakeIntegrationEventPublisher();

        var result = await BuildHandler(db, returnToWorkRequiredAfterDays: 7, eventPublisher: eventPublisher)
            .HandleAsync(new CloseSicknessRecordRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                Id = record.Id,
                EndDate = new DateOnly(2026, 7, 3),
                EndDayPart = SicknessDayPart.FullDay
            }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(await db.ReturnToWorkReviews.AnyAsync(r => r.SicknessRecordId == record.Id));
        Assert.Empty(eventPublisher.PublishedEvents);
    }

    // ReturnToWorkRequiredAfterDays is mandatory now (no opt-out — see
    // CompanySettings.ReturnToWorkRequiredAfterDays), so the "setting is null, no review created"
    // case this used to cover can no longer occur and has been removed.
}
