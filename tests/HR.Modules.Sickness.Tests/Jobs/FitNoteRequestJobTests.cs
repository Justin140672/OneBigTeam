using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using HR.Modules.Sickness;

namespace HR.Modules.Sickness.Tests.Jobs;

/// <summary>
/// SICK-01: FitNoteRequestJob re-evaluates open records' calendar-day duration against "today" and
/// catches any closed record left without a request. These tests use a fixed "today" of
/// 2026-06-15 and express eligibility via StartDate relative to that date rather than any TotalDays
/// field on the record (the working-day total is irrelevant to the fit-note threshold — see
/// FitNoteEvaluator).
/// </summary>
public class FitNoteRequestJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 15, 2, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static FitNoteRequestJob BuildJob(
        SicknessDbContext db,
        int fitNoteRequiredAfterDays = 7,
        FakeIntegrationEventPublisher? integrationPublisher = null,
        FakeAuditEventPublisher? auditPublisher = null) =>
        new(db,
            new FakeCompanySicknessSettingsReader(fitNoteRequiredAfterDays: fitNoteRequiredAfterDays),
            new FitNoteEvidenceRequestService(
                db,
                integrationPublisher ?? new FakeIntegrationEventPublisher(),
                auditPublisher ?? new FakeAuditEventPublisher(),
                new FakeTaskRescheduler()),
            new FakeClock(FixedUtcNow));

    private static async Task<Guid> SeedCategory(SicknessDbContext db, Guid companyId)
    {
        var category = SicknessCategory.Create(Guid.NewGuid(), companyId, "Cold", 1, Now);
        db.SicknessCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    /// <summary>
    /// An open record that started <paramref name="daysAgo"/> calendar days before Today (inclusive
    /// — StartDate itself counts as day 1, so daysAgo=6 means 7 calendar days have elapsed today).
    /// </summary>
    private static SicknessRecord CreateOpenRecord(
        Guid companyId,
        Guid categoryId,
        int daysAgo,
        SicknessEvidenceStatus evidenceStatus = SicknessEvidenceStatus.Pending)
    {
        return SicknessRecord.Create(
            Guid.NewGuid(),
            companyId,
            Guid.NewGuid(),
            categoryId,
            Today.AddDays(-daysAgo),
            SicknessDayPart.FullDay,
            endDate: null,
            endDayPart: null,
            totalDays: null,
            notes: null,
            evidenceStatus,
            Now);
    }

    private static SicknessRecord CreateClosedRecord(
        Guid companyId,
        Guid categoryId,
        DateOnly startDate,
        DateOnly endDate,
        SicknessEvidenceStatus evidenceStatus = SicknessEvidenceStatus.Pending)
    {
        return SicknessRecord.Create(
            Guid.NewGuid(),
            companyId,
            Guid.NewGuid(),
            categoryId,
            startDate,
            SicknessDayPart.FullDay,
            endDate,
            SicknessDayPart.FullDay,
            totalDays: 1m,
            notes: null,
            evidenceStatus,
            Now);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesEvidenceRequest_WhenOngoingAbsenceReachesCalendarDayThreshold()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        // Started 6 days before Today → 7 calendar days elapsed today (inclusive) = threshold met
        var record = CreateOpenRecord(companyId, categoryId, daysAgo: 6);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        var requests = await db.SicknessEvidenceRequests.ToListAsync();
        Assert.Single(requests);
        Assert.Equal(record.Id, requests[0].SicknessRecordId);
        Assert.Equal(companyId, requests[0].CompanyId);
        Assert.Equal(SicknessEvidenceRequestStatus.Pending, requests[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotCreateRequest_OneDayBeforeThreshold()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        // Started 5 days before Today → only 6 calendar days elapsed, threshold 7 not yet reached
        var record = CreateOpenRecord(companyId, categoryId, daysAgo: 5);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        Assert.Empty(await db.SicknessEvidenceRequests.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_WeekendsAndHolidays_CountTowardCalendarDayThreshold()
    {
        // Regression for SICK-01: threshold must not depend on the working-day TotalDays total —
        // an absence spanning a weekend still accrues calendar days toward the threshold even
        // though TotalDays (working days) is never populated for an open record at all.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateOpenRecord(companyId, categoryId, daysAgo: 6);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        Assert.Single(await db.SicknessEvidenceRequests.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_SkipsRecord_WhenActiveEvidenceRequestAlreadyExists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateOpenRecord(companyId, categoryId, daysAgo: 6);
        db.SicknessRecords.Add(record);

        var existingRequest = SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, record.Id, Guid.Empty, Today.AddDays(7), null, Now);
        db.SicknessEvidenceRequests.Add(existingRequest);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        var requests = await db.SicknessEvidenceRequests.ToListAsync();
        Assert.Single(requests); // only the pre-existing one
        Assert.Equal(existingRequest.Id, requests[0].Id);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesRequest_WhenOnlyExistingRequestIsCancelled()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateOpenRecord(companyId, categoryId, daysAgo: 6);
        db.SicknessRecords.Add(record);

        var cancelledRequest = SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, record.Id, Guid.Empty, Today.AddDays(7), null, Now);
        cancelledRequest.Cancel(Now);
        db.SicknessEvidenceRequests.Add(cancelledRequest);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        var requests = await db.SicknessEvidenceRequests.ToListAsync();
        Assert.Equal(2, requests.Count);
        Assert.Contains(requests, r => r.Status == SicknessEvidenceRequestStatus.Pending);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsRecord_WhenEvidenceReceived()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateOpenRecord(companyId, categoryId, daysAgo: 6, evidenceStatus: SicknessEvidenceStatus.Received);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        Assert.Empty(await db.SicknessEvidenceRequests.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_SkipsRecord_WhenEvidenceWaived()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateOpenRecord(companyId, categoryId, daysAgo: 6, evidenceStatus: SicknessEvidenceStatus.Waived);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        Assert.Empty(await db.SicknessEvidenceRequests.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_SetsCorrectDueDate_TodayPlusSevenDays()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateOpenRecord(companyId, categoryId, daysAgo: 6);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        var request = await db.SicknessEvidenceRequests.SingleAsync();
        Assert.Equal(Today.AddDays(7), request.DueDate);
    }

    [Fact]
    public async Task ExecuteAsync_PublishesAuditEvent_ForEachCreatedRequest()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record1 = CreateOpenRecord(companyId, categoryId, daysAgo: 6);
        var record2 = CreateOpenRecord(companyId, categoryId, daysAgo: 10);
        db.SicknessRecords.AddRange(record1, record2);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditEventPublisher();
        var job = BuildJob(db, fitNoteRequiredAfterDays: 7, auditPublisher: auditPublisher);
        await job.ExecuteAsync();

        var auditEvents = auditPublisher.PublishedEvents.OfType<SicknessEvidenceRequestedAuditEvent>().ToList();
        Assert.Equal(2, auditEvents.Count);
        Assert.All(auditEvents, e =>
        {
            Assert.Equal(companyId, e.CompanyId);
            Assert.Equal(Today.AddDays(7), e.DueDate);
        });
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotPublishAuditEvent_WhenRecordIsSkipped()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateOpenRecord(companyId, categoryId, daysAgo: 6);
        db.SicknessRecords.Add(record);

        var existingRequest = SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, record.Id, Guid.Empty, Today.AddDays(7), null, Now);
        db.SicknessEvidenceRequests.Add(existingRequest);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditEventPublisher();
        var job = BuildJob(db, fitNoteRequiredAfterDays: 7, auditPublisher: auditPublisher);
        await job.ExecuteAsync();

        Assert.Empty(auditPublisher.PublishedEvents.OfType<SicknessEvidenceRequestedAuditEvent>());
    }

    [Fact]
    public async Task ExecuteAsync_IsIdempotent_OnRepeatedExecution()
    {
        // Simulates a Hangfire retry / re-run of the same daily job.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateOpenRecord(companyId, categoryId, daysAgo: 6);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditEventPublisher();
        var job = BuildJob(db, fitNoteRequiredAfterDays: 7, auditPublisher: auditPublisher);

        await job.ExecuteAsync();
        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Single(await db.SicknessEvidenceRequests.ToListAsync());
        Assert.Single(auditPublisher.PublishedEvents.OfType<SicknessEvidenceRequestedAuditEvent>());
    }

    [Fact]
    public async Task ExecuteAsync_ClosedRecord_ShortAbsence_DoesNotCreateRequest()
    {
        // A short closed absence (below threshold at close) must never get a request.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateClosedRecord(
            companyId, categoryId, Today.AddDays(-10), Today.AddDays(-8),
            evidenceStatus: SicknessEvidenceStatus.NotRequired);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        Assert.Empty(await db.SicknessEvidenceRequests.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_EligibleClosedRecord_WithoutExistingRequest_CreatesRequest()
    {
        // Simulates a record closed before the job last ran (or a legacy/imported closed record)
        // that never got its evidence request.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var start = Today.AddDays(-20);
        var end = Today.AddDays(-14); // 7 calendar days elapsed at close — threshold met
        var record = CreateClosedRecord(companyId, categoryId, start, end, evidenceStatus: SicknessEvidenceStatus.Pending);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        var request = await db.SicknessEvidenceRequests.SingleAsync();
        Assert.Equal(record.Id, request.SicknessRecordId);
        Assert.Equal(end.AddDays(7), request.DueDate);
    }

    [Fact]
    public async Task ExecuteAsync_ClosedRecord_ReceivedEvidence_NeverRequested()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateClosedRecord(
            companyId, categoryId, Today.AddDays(-20), Today.AddDays(-10),
            evidenceStatus: SicknessEvidenceStatus.Received);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        Assert.Empty(await db.SicknessEvidenceRequests.ToListAsync());
    }
}
