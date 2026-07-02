using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using HR.Modules.Sickness;

namespace HR.Modules.Sickness.Tests.Jobs;

public class FitNoteRequestJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 15, 2, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static FitNoteRequestJob BuildJob(
        SicknessDbContext db,
        int? fitNoteRequiredAfterDays = 7,
        FakeIntegrationEventPublisher? integrationPublisher = null,
        FakeAuditEventPublisher? auditPublisher = null) =>
        new(db,
            new FakeCompanySicknessSettingsReader(fitNoteRequiredAfterDays: fitNoteRequiredAfterDays),
            integrationPublisher ?? new FakeIntegrationEventPublisher(),
            auditPublisher ?? new FakeAuditEventPublisher(),
            new FakeClock(FixedUtcNow));

    private static async Task<Guid> SeedCategory(SicknessDbContext db, Guid companyId)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var category = SicknessCategory.Create(Guid.NewGuid(), companyId, "Cold", 1, now);
        db.SicknessCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private static SicknessRecord CreateOpenRecord(
        Guid companyId,
        Guid categoryId,
        decimal totalDays,
        SicknessEvidenceStatus evidenceStatus = SicknessEvidenceStatus.Pending)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        return SicknessRecord.Create(
            Guid.NewGuid(),
            companyId,
            Guid.NewGuid(),
            categoryId,
            new DateOnly(2026, 6, 1),
            SicknessDayPart.FullDay,
            endDate: null,
            endDayPart: null,
            totalDays,
            notes: null,
            evidenceStatus,
            now);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesEvidenceRequest_WhenThresholdExceededAndNoExistingRequest()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateOpenRecord(companyId, categoryId, totalDays: 8m);
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
    public async Task ExecuteAsync_SkipsRecord_WhenActiveEvidenceRequestAlreadyExists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = CreateOpenRecord(companyId, categoryId, totalDays: 8m);
        db.SicknessRecords.Add(record);

        var existingRequest = SicknessEvidenceRequest.Create(
            Guid.NewGuid(),
            companyId,
            record.Id,
            Guid.Empty,
            Today.AddDays(7),
            null,
            now);
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
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = CreateOpenRecord(companyId, categoryId, totalDays: 8m);
        db.SicknessRecords.Add(record);

        var cancelledRequest = SicknessEvidenceRequest.Create(
            Guid.NewGuid(),
            companyId,
            record.Id,
            Guid.Empty,
            Today.AddDays(7),
            null,
            now);
        cancelledRequest.Cancel(now);
        db.SicknessEvidenceRequests.Add(cancelledRequest);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        var requests = await db.SicknessEvidenceRequests.ToListAsync();
        Assert.Equal(2, requests.Count);
        Assert.Contains(requests, r => r.Status == SicknessEvidenceRequestStatus.Pending);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsRecord_WhenEvidenceStatusIsNotPending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateOpenRecord(companyId, categoryId, totalDays: 8m, evidenceStatus: SicknessEvidenceStatus.Received);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: 7);
        await job.ExecuteAsync();

        var requests = await db.SicknessEvidenceRequests.ToListAsync();
        Assert.Empty(requests);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsCompany_WhenFitNoteRequiredAfterDaysIsNull()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateOpenRecord(companyId, categoryId, totalDays: 8m);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var job = BuildJob(db, fitNoteRequiredAfterDays: null);
        await job.ExecuteAsync();

        var requests = await db.SicknessEvidenceRequests.ToListAsync();
        Assert.Empty(requests);
    }

    [Fact]
    public async Task ExecuteAsync_SetsCorrectDueDate_TodayPlusSevenDays()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var record = CreateOpenRecord(companyId, categoryId, totalDays: 8m);
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

        var record1 = CreateOpenRecord(companyId, categoryId, totalDays: 8m);
        var record2 = CreateOpenRecord(companyId, categoryId, totalDays: 10m);
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
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var record = CreateOpenRecord(companyId, categoryId, totalDays: 8m);
        db.SicknessRecords.Add(record);

        var existingRequest = SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, record.Id, Guid.Empty, Today.AddDays(7), null, now);
        db.SicknessEvidenceRequests.Add(existingRequest);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditEventPublisher();
        var job = BuildJob(db, fitNoteRequiredAfterDays: 7, auditPublisher: auditPublisher);
        await job.ExecuteAsync();

        Assert.Empty(auditPublisher.PublishedEvents.OfType<SicknessEvidenceRequestedAuditEvent>());
    }
}
