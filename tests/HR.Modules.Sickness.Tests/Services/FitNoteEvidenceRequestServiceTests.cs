using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests.Services;

/// <summary>
/// Direct unit tests for FitNoteEvidenceRequestService.RequestIfEligibleAsync in isolation, covering
/// the eligibility/idempotency guards described in the SICK-01 fix. FitNoteRequestJobTests and the
/// handler tests exercise this service indirectly through their respective callers; these tests
/// pin down the service's own contract.
/// </summary>
public class FitNoteEvidenceRequestServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 15, 2, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly EvaluationDate = new(2026, 6, 15);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static FitNoteEvidenceRequestService BuildService(
        SicknessDbContext db,
        FakeIntegrationEventPublisher? eventPublisher = null,
        FakeAuditEventPublisher? auditPublisher = null) =>
        new(db,
            eventPublisher ?? new FakeIntegrationEventPublisher(),
            auditPublisher ?? new FakeAuditEventPublisher());

    private static SicknessRecord CreateRecord(
        Guid companyId,
        DateOnly startDate,
        SicknessEvidenceStatus evidenceStatus = SicknessEvidenceStatus.Pending)
    {
        return SicknessRecord.Create(
            Guid.NewGuid(),
            companyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            startDate,
            SicknessDayPart.FullDay,
            endDate: null,
            endDayPart: null,
            totalDays: null,
            notes: null,
            evidenceStatus,
            Now);
    }

    [Fact]
    public async Task RequestIfEligibleAsync_CreatesRequestAndEvents_WhenThresholdReached()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        // Started 6 days before EvaluationDate → 7 calendar days elapsed (inclusive) = threshold met
        var record = CreateRecord(companyId, EvaluationDate.AddDays(-6));
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var integrationPublisher = new FakeIntegrationEventPublisher();
        var auditPublisher = new FakeAuditEventPublisher();
        var service = BuildService(db, integrationPublisher, auditPublisher);

        var result = await service.RequestIfEligibleAsync(record, 7, EvaluationDate, Now, CancellationToken.None);

        Assert.True(result);

        var request = await db.SicknessEvidenceRequests.SingleAsync();
        Assert.Equal(record.Id, request.SicknessRecordId);
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(SicknessEvidenceRequestStatus.Pending, request.Status);

        var integrationEvent = Assert.IsType<SicknessEvidenceRequestedIntegrationEvent>(
            Assert.Single(integrationPublisher.PublishedEvents));
        Assert.Equal(record.Id, integrationEvent.SicknessRecordId);
        Assert.Equal(request.Id, integrationEvent.EvidenceRequestId);

        var auditEvent = Assert.IsType<SicknessEvidenceRequestedAuditEvent>(
            Assert.Single(auditPublisher.PublishedEvents));
        Assert.Equal(record.Id, auditEvent.SicknessRecordId);
        Assert.Equal(request.Id, auditEvent.EvidenceRequestId);
    }

    [Fact]
    public async Task RequestIfEligibleAsync_ReturnsFalse_WhenThresholdNotReached()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        // Started 5 days before EvaluationDate → only 6 calendar days elapsed, threshold 7 not met
        var record = CreateRecord(companyId, EvaluationDate.AddDays(-5));
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var service = BuildService(db);

        var result = await service.RequestIfEligibleAsync(record, 7, EvaluationDate, Now, CancellationToken.None);

        Assert.False(result);
        Assert.Empty(await db.SicknessEvidenceRequests.ToListAsync());
    }

    [Fact]
    public async Task RequestIfEligibleAsync_ReturnsFalse_WhenNonCancelledRequestAlreadyExists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var record = CreateRecord(companyId, EvaluationDate.AddDays(-6));
        db.SicknessRecords.Add(record);

        var existingRequest = SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, record.Id, Guid.Empty, EvaluationDate.AddDays(7), null, Now);
        db.SicknessEvidenceRequests.Add(existingRequest);
        await db.SaveChangesAsync();

        var service = BuildService(db);

        var result = await service.RequestIfEligibleAsync(record, 7, EvaluationDate, Now, CancellationToken.None);

        Assert.False(result);
        var requests = await db.SicknessEvidenceRequests.ToListAsync();
        Assert.Single(requests);
        Assert.Equal(existingRequest.Id, requests[0].Id);
    }

    [Fact]
    public async Task RequestIfEligibleAsync_CreatesRequest_WhenOnlyExistingRequestIsCancelled()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var record = CreateRecord(companyId, EvaluationDate.AddDays(-6));
        db.SicknessRecords.Add(record);

        var cancelledRequest = SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, record.Id, Guid.Empty, EvaluationDate.AddDays(7), null, Now);
        cancelledRequest.Cancel(Now);
        db.SicknessEvidenceRequests.Add(cancelledRequest);
        await db.SaveChangesAsync();

        var service = BuildService(db);

        var result = await service.RequestIfEligibleAsync(record, 7, EvaluationDate, Now, CancellationToken.None);

        Assert.True(result);
        var requests = await db.SicknessEvidenceRequests.ToListAsync();
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task RequestIfEligibleAsync_ReturnsFalse_ForReceivedEvidenceStatus()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var record = CreateRecord(companyId, EvaluationDate.AddDays(-20), SicknessEvidenceStatus.Received);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var service = BuildService(db);

        var result = await service.RequestIfEligibleAsync(record, 7, EvaluationDate, Now, CancellationToken.None);

        Assert.False(result);
        Assert.Empty(await db.SicknessEvidenceRequests.ToListAsync());
        Assert.Equal(SicknessEvidenceStatus.Received, record.EvidenceStatus);
    }

    [Fact]
    public async Task RequestIfEligibleAsync_ReturnsFalse_ForWaivedEvidenceStatus()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var record = CreateRecord(companyId, EvaluationDate.AddDays(-20), SicknessEvidenceStatus.Waived);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var service = BuildService(db);

        var result = await service.RequestIfEligibleAsync(record, 7, EvaluationDate, Now, CancellationToken.None);

        Assert.False(result);
        Assert.Empty(await db.SicknessEvidenceRequests.ToListAsync());
        Assert.Equal(SicknessEvidenceStatus.Waived, record.EvidenceStatus);
    }

    [Fact]
    public async Task RequestIfEligibleAsync_MarksEvidencePending_WhenNotAlreadyPending_AndRequestIsCreated()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        // NotRequired e.g. a legacy/imported record being re-evaluated by the daily job.
        var record = CreateRecord(companyId, EvaluationDate.AddDays(-6), SicknessEvidenceStatus.NotRequired);
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var service = BuildService(db);

        var result = await service.RequestIfEligibleAsync(record, 7, EvaluationDate, Now, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(SicknessEvidenceStatus.Pending, record.EvidenceStatus);

        var saved = await db.SicknessRecords.SingleAsync(r => r.Id == record.Id);
        Assert.Equal(SicknessEvidenceStatus.Pending, saved.EvidenceStatus);
    }

    [Fact]
    public async Task RequestIfEligibleAsync_DueDate_IsEvaluationDatePlusSevenDays()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var record = CreateRecord(companyId, EvaluationDate.AddDays(-6));
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var service = BuildService(db);

        await service.RequestIfEligibleAsync(record, 7, EvaluationDate, Now, CancellationToken.None);

        var request = await db.SicknessEvidenceRequests.SingleAsync();
        Assert.Equal(EvaluationDate.AddDays(7), request.DueDate);
    }

    [Fact]
    public async Task RequestIfEligibleAsync_CalledTwice_OnlyEverCreatesOneRequest()
    {
        // Retry safety at the service level, not just via the job's own guard.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var record = CreateRecord(companyId, EvaluationDate.AddDays(-6));
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var service = BuildService(db);

        var first = await service.RequestIfEligibleAsync(record, 7, EvaluationDate, Now, CancellationToken.None);
        var second = await service.RequestIfEligibleAsync(record, 7, EvaluationDate, Now, CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
        Assert.Single(await db.SicknessEvidenceRequests.ToListAsync());
    }

    // SICK-06: evidence requests are always system/policy-triggered (never something an
    // affected employee or manager "did"), so the actor is always the fixed SystemActorId
    // (Guid.Empty) — regardless of caller/context.
    [Fact]
    public async Task RequestIfEligibleAsync_AuditEvent_ActorId_Is_SystemActorId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var record = CreateRecord(companyId, EvaluationDate.AddDays(-6));
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditEventPublisher();
        var service = BuildService(db, auditPublisher: auditPublisher);

        await service.RequestIfEligibleAsync(record, 7, EvaluationDate, Now, CancellationToken.None);

        var auditEvent = Assert.Single(auditPublisher.PublishedEvents.OfType<SicknessEvidenceRequestedAuditEvent>());
        Assert.Equal(Guid.Empty, auditEvent.ActorId);
        Assert.Equal(FitNoteEvidenceRequestService.SystemActorId, auditEvent.ActorId);
        Assert.Equal(Guid.Empty, ((HR.SharedKernel.IAuditEvent)auditEvent).ActorEmployeeId);
    }

    // SICK-06: no free-text content (Notes/EvidenceNotes) is ever carried on this event — the
    // payload is limited to structured ids/dates.
    [Fact]
    public async Task RequestIfEligibleAsync_AuditEvent_Does_Not_Contain_Free_Text()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var record = CreateRecord(companyId, EvaluationDate.AddDays(-6));
        db.SicknessRecords.Add(record);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditEventPublisher();
        var service = BuildService(db, auditPublisher: auditPublisher);

        await service.RequestIfEligibleAsync(record, 7, EvaluationDate, Now, CancellationToken.None);

        var auditEvent = Assert.Single(auditPublisher.PublishedEvents.OfType<SicknessEvidenceRequestedAuditEvent>());
        var serialized = System.Text.Json.JsonSerializer.Serialize(auditEvent);
        // Nothing beyond ids/dates should be present — spot-check no free-text-shaped property exists.
        Assert.DoesNotContain("EvidenceNotes", serialized);
        Assert.DoesNotContain("Notes\":", serialized);
    }
}
