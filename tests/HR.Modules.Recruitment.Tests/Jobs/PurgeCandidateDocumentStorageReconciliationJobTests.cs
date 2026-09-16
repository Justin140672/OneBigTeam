using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests.Jobs;

/// <summary>
/// Ticket 13 (P2): PurgeCandidateDocumentStorageReconciliationJob is the recurring sweep that
/// repairs document-deletion operations and audit deliveries left behind by
/// Features/PurgeEligibleCandidates/Handler.cs when their immediate best-effort trigger (a Hangfire
/// enqueue, an inline audit publish) didn't happen or didn't complete. See
/// Jobs/PurgeCandidateDocumentStorageReconciliationJob.cs.
/// </summary>
public class PurgeCandidateDocumentStorageReconciliationJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static PurgeCandidateDocumentStorageReconciliationJob BuildJob(
        RecruitmentDbContext db, RecordingBackgroundJobClient jobClient, HR.SharedKernel.IAuditEventPublisher auditPublisher) =>
        new(db, new FakeClock(FixedUtcNow), auditPublisher, jobClient, NullLogger<PurgeCandidateDocumentStorageReconciliationJob>.Instance);

    private static CandidateDocumentDeletionOperation SeedDeletionOperation(
        RecruitmentDbContext db, string status, DateTimeOffset? lastAttemptAt = null)
    {
        var operation = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "some/storage/key.pdf", Now.AddMinutes(-30));

        switch (status)
        {
            case CandidateDocumentDeletionOperation.StatusPending:
                break;
            case CandidateDocumentDeletionOperation.StatusFailed:
                operation.MarkProcessing(Now.AddMinutes(-20));
                operation.MarkFailed("boom", Now.AddMinutes(-19));
                break;
            case CandidateDocumentDeletionOperation.StatusProcessing:
                operation.MarkProcessing(lastAttemptAt ?? Now.AddMinutes(-1));
                break;
            case CandidateDocumentDeletionOperation.StatusCompleted:
                operation.MarkProcessing(Now.AddMinutes(-20));
                operation.MarkCompleted(Now.AddMinutes(-19));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        db.CandidateDocumentDeletionOperations.Add(operation);
        db.SaveChanges();
        return operation;
    }

    [Fact]
    public async Task ExecuteAsync_Enqueues_Pending_Deletion_Operation()
    {
        await using var db = BuildContext();
        var operation = SeedDeletionOperation(db, CandidateDocumentDeletionOperation.StatusPending);

        var jobClient = new RecordingBackgroundJobClient();
        await BuildJob(db, jobClient, new FakeAuditPublisher()).ExecuteAsync();

        var enqueued = Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == operation.Id);
        Assert.Equal(typeof(PurgeCandidateDocumentStorageJob), enqueued.Type);

        var reloaded = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusPending, reloaded.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Resets_Failed_Deletion_Operation_To_Pending_And_Enqueues_It()
    {
        await using var db = BuildContext();
        var operation = SeedDeletionOperation(db, CandidateDocumentDeletionOperation.StatusFailed);

        var jobClient = new RecordingBackgroundJobClient();
        await BuildJob(db, jobClient, new FakeAuditPublisher()).ExecuteAsync();

        var reloaded = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusPending, reloaded.Status);
        Assert.Null(reloaded.FailureReason);

        Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == operation.Id);
    }

    [Fact]
    public async Task ExecuteAsync_Resets_Stale_Processing_Deletion_Operation_To_Pending_And_Enqueues_It()
    {
        await using var db = BuildContext();
        var operation = SeedDeletionOperation(
            db, CandidateDocumentDeletionOperation.StatusProcessing, lastAttemptAt: Now.AddMinutes(-20));

        var jobClient = new RecordingBackgroundJobClient();
        await BuildJob(db, jobClient, new FakeAuditPublisher()).ExecuteAsync();

        var reloaded = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusPending, reloaded.Status);

        Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == operation.Id);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Touch_Or_Enqueue_Fresh_Processing_Deletion_Operation()
    {
        await using var db = BuildContext();
        var operation = SeedDeletionOperation(
            db, CandidateDocumentDeletionOperation.StatusProcessing, lastAttemptAt: Now.AddMinutes(-2));

        var jobClient = new RecordingBackgroundJobClient();
        await BuildJob(db, jobClient, new FakeAuditPublisher()).ExecuteAsync();

        Assert.DoesNotContain(jobClient.CreatedJobs, j => (Guid)j.Args[0] == operation.Id);

        var reloaded = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusProcessing, reloaded.Status); // untouched
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Enqueue_Completed_Deletion_Operation()
    {
        await using var db = BuildContext();
        var operation = SeedDeletionOperation(db, CandidateDocumentDeletionOperation.StatusCompleted);

        var jobClient = new RecordingBackgroundJobClient();
        await BuildJob(db, jobClient, new FakeAuditPublisher()).ExecuteAsync();

        Assert.DoesNotContain(jobClient.CreatedJobs, j => (Guid)j.Args[0] == operation.Id);

        var reloaded = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusCompleted, reloaded.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Publishes_Pending_AuditDelivery_And_Marks_It_Delivered()
    {
        await using var db = BuildContext();
        var candidateIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var delivery = CandidatePurgeAuditDelivery.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), candidateIds, Guid.NewGuid(), Now.AddMinutes(-30));
        db.CandidatePurgeAuditDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        await BuildJob(db, new RecordingBackgroundJobClient(), auditPublisher).ExecuteAsync();

        var published = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<CandidatesPurgedAuditEvent>(published);
        Assert.Equal(candidateIds, auditEvent.PurgedCandidateIds);

        var reloaded = await db.CandidatePurgeAuditDeliveries.SingleAsync(d => d.Id == delivery.Id);
        Assert.Equal(CandidatePurgeAuditDelivery.StatusDelivered, reloaded.Status);
        Assert.NotNull(reloaded.DeliveredAt);
        Assert.Equal(1, reloaded.AttemptCount);
    }

    [Fact]
    public async Task ExecuteAsync_Leaves_AuditDelivery_Pending_And_Increments_AttemptCount_When_Publish_Throws()
    {
        await using var db = BuildContext();
        var delivery = CandidatePurgeAuditDelivery.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], Guid.NewGuid(), Now.AddMinutes(-30));
        db.CandidatePurgeAuditDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        var throwingPublisher = new ThrowingAuditPublisher();
        await BuildJob(db, new RecordingBackgroundJobClient(), throwingPublisher).ExecuteAsync();

        var reloaded = await db.CandidatePurgeAuditDeliveries.SingleAsync(d => d.Id == delivery.Id);
        Assert.Equal(CandidatePurgeAuditDelivery.StatusPending, reloaded.Status);
        Assert.Null(reloaded.DeliveredAt);
        Assert.Equal(1, reloaded.AttemptCount);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Republish_Delivered_AuditDelivery()
    {
        await using var db = BuildContext();
        var delivery = CandidatePurgeAuditDelivery.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], Guid.NewGuid(), Now.AddMinutes(-30));
        delivery.RecordAttempt();
        delivery.MarkDelivered(Now.AddMinutes(-29));
        db.CandidatePurgeAuditDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        await BuildJob(db, new RecordingBackgroundJobClient(), auditPublisher).ExecuteAsync();

        Assert.Empty(auditPublisher.Published);

        var reloaded = await db.CandidatePurgeAuditDeliveries.SingleAsync(d => d.Id == delivery.Id);
        Assert.Equal(1, reloaded.AttemptCount); // untouched by this sweep
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class ThrowingAuditPublisher : HR.SharedKernel.IAuditEventPublisher
    {
        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated audit sink outage.");
    }
}
