using HR.Modules.Tasks.Contracts;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.FulfilEvidenceRequest;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;
using HR.Modules.Sickness;

namespace HR.Modules.Sickness.Tests;

public class SicknessEvidenceUploadCompletionActionTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(NowUtc, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static SicknessDbContext BuildDbContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<(SicknessRecord record, SicknessEvidenceRequest evidenceRequest)> SeedData(
        SicknessDbContext db,
        SicknessEvidenceRequestStatus evidenceRequestStatus = SicknessEvidenceRequestStatus.Pending)
    {
        var category = SicknessCategory.Create(Guid.NewGuid(), CompanyId, "Cold", 1, Now);
        db.SicknessCategories.Add(category);

        var record = SicknessRecord.Create(
            Guid.NewGuid(), CompanyId, EmployeeId, category.Id,
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay,
            null, null, null, null, SicknessEvidenceStatus.Pending, Now);
        db.SicknessRecords.Add(record);

        var evidenceRequest = SicknessEvidenceRequest.Create(
            Guid.NewGuid(), CompanyId, record.Id,
            Guid.NewGuid(), new DateOnly(2026, 7, 9), null, Now);

        if (evidenceRequestStatus == SicknessEvidenceRequestStatus.Fulfilled)
            evidenceRequest.Fulfil(Now);
        else if (evidenceRequestStatus == SicknessEvidenceRequestStatus.Overdue)
            evidenceRequest.MarkOverdue(Now);

        db.SicknessEvidenceRequests.Add(evidenceRequest);
        await db.SaveChangesAsync();

        return (record, evidenceRequest);
    }

    private static SicknessEvidenceUploadCompletionAction BuildAction(
        SicknessDbContext db,
        DateTime? nowUtc = null,
        FakeAuditEventPublisher? auditPublisher = null) =>
        new(db, new FakeClock(nowUtc ?? NowUtc), auditPublisher ?? new FakeAuditEventPublisher());

    private static TaskCompletionContext BuildCompletionContext(Guid companyId, Guid? sourceEntityId) =>
        new(companyId, Guid.NewGuid(), "Upload fit note", null,
            TaskSource.Sickness, TaskActionType.Upload,
            EmployeeId, Guid.NewGuid(), Now, sourceEntityId);

    [Fact]
    public async Task ExecuteAsync_FulfilsEvidenceRequestAndSetsRecordStatusToReceived()
    {
        var db = BuildDbContext();
        var (record, evidenceRequest) = await SeedData(db);
        var action = BuildAction(db);

        await action.ExecuteAsync(BuildCompletionContext(CompanyId, evidenceRequest.Id), CancellationToken.None);

        var updatedRequest = await db.SicknessEvidenceRequests.FindAsync(evidenceRequest.Id);
        var updatedRecord = await db.SicknessRecords.FindAsync(record.Id);

        Assert.Equal(SicknessEvidenceRequestStatus.Fulfilled, updatedRequest!.Status);
        Assert.NotNull(updatedRequest.FulfilledAt);
        Assert.Equal(SicknessEvidenceStatus.Received, updatedRecord!.EvidenceStatus);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSourceEntityIdIsNull_DoesNothing()
    {
        var db = BuildDbContext();
        var (record, _) = await SeedData(db);
        var action = BuildAction(db);

        await action.ExecuteAsync(BuildCompletionContext(CompanyId, sourceEntityId: null), CancellationToken.None);

        var updatedRecord = await db.SicknessRecords.FindAsync(record.Id);
        Assert.Equal(SicknessEvidenceStatus.Pending, updatedRecord!.EvidenceStatus);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEvidenceRequestNotFound_DoesNothing()
    {
        var db = BuildDbContext();
        var (record, _) = await SeedData(db);
        var action = BuildAction(db);

        await action.ExecuteAsync(BuildCompletionContext(CompanyId, Guid.NewGuid()), CancellationToken.None);

        var updatedRecord = await db.SicknessRecords.FindAsync(record.Id);
        Assert.Equal(SicknessEvidenceStatus.Pending, updatedRecord!.EvidenceStatus);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyFulfilled_SkipsWithoutError()
    {
        var db = BuildDbContext();
        var (record, evidenceRequest) = await SeedData(db, SicknessEvidenceRequestStatus.Fulfilled);
        var originalFulfilledAt = evidenceRequest.FulfilledAt;
        var action = BuildAction(db, NowUtc.AddHours(1));

        await action.ExecuteAsync(BuildCompletionContext(CompanyId, evidenceRequest.Id), CancellationToken.None);

        var updatedRequest = await db.SicknessEvidenceRequests.FindAsync(evidenceRequest.Id);
        Assert.Equal(SicknessEvidenceRequestStatus.Fulfilled, updatedRequest!.Status);
        // FulfilledAt should not have changed
        Assert.Equal(originalFulfilledAt, updatedRequest.FulfilledAt);
    }

    [Fact]
    public void Source_ReturnsSickness() =>
        Assert.Equal(TaskSource.Sickness, BuildAction(BuildDbContext()).Source);

    [Fact]
    public void ActionType_ReturnsUpload() =>
        Assert.Equal(TaskActionType.Upload, BuildAction(BuildDbContext()).ActionType);

    [Fact]
    public async Task ExecuteAsync_PublishesAuditEvent_OnSuccessfulFulfil()
    {
        var db = BuildDbContext();
        var (_, evidenceRequest) = await SeedData(db);
        var auditPublisher = new FakeAuditEventPublisher();
        var action = BuildAction(db, auditPublisher: auditPublisher);

        await action.ExecuteAsync(BuildCompletionContext(CompanyId, evidenceRequest.Id), CancellationToken.None);

        var auditEvents = auditPublisher.PublishedEvents.OfType<SicknessEvidenceFulfilledAuditEvent>().ToList();
        Assert.Single(auditEvents);
        var evt = auditEvents[0];
        Assert.Equal(evidenceRequest.Id, evt.EvidenceRequestId);
        Assert.Equal(evidenceRequest.SicknessRecordId, evt.SicknessRecordId);
        Assert.Equal(CompanyId, evt.CompanyId);
        Assert.Equal(EmployeeId, evt.EmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotPublishAuditEvent_WhenSourceEntityIdIsNull()
    {
        var db = BuildDbContext();
        await SeedData(db);
        var auditPublisher = new FakeAuditEventPublisher();
        var action = BuildAction(db, auditPublisher: auditPublisher);

        await action.ExecuteAsync(BuildCompletionContext(CompanyId, sourceEntityId: null), CancellationToken.None);

        Assert.Empty(auditPublisher.PublishedEvents.OfType<SicknessEvidenceFulfilledAuditEvent>());
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotPublishAuditEvent_WhenAlreadyFulfilled()
    {
        var db = BuildDbContext();
        var (_, evidenceRequest) = await SeedData(db, SicknessEvidenceRequestStatus.Fulfilled);
        var auditPublisher = new FakeAuditEventPublisher();
        var action = BuildAction(db, auditPublisher: auditPublisher);

        await action.ExecuteAsync(BuildCompletionContext(CompanyId, evidenceRequest.Id), CancellationToken.None);

        Assert.Empty(auditPublisher.PublishedEvents.OfType<SicknessEvidenceFulfilledAuditEvent>());
    }
}
