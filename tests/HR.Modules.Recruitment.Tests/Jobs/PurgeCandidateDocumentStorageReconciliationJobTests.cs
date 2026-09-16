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
        RecruitmentDbContext db, RecordingBackgroundJobClient jobClient, HR.SharedKernel.IAuditEventPublisher auditPublisher,
        FakeLegalHoldStatusReader? legalHoldStatusReader = null) =>
        new(db, new FakeClock(FixedUtcNow), auditPublisher, legalHoldStatusReader ?? new FakeLegalHoldStatusReader(),
            jobClient, NullLogger<PurgeCandidateDocumentStorageReconciliationJob>.Instance);

    private static CandidateDocumentDeletionOperation SeedDeletionOperation(
        RecruitmentDbContext db, string status, DateTimeOffset? leaseExpiresAt = null, Guid? companyId = null)
    {
        var operation = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), companyId ?? Guid.NewGuid(), Guid.NewGuid(), "some/storage/key.pdf", Now.AddMinutes(-30));

        switch (status)
        {
            case CandidateDocumentDeletionOperation.StatusPending:
                break;
            case CandidateDocumentDeletionOperation.StatusFailed:
                operation.Claim(Guid.NewGuid(), Now.AddMinutes(-20));
                operation.MarkFailed("boom", Now.AddMinutes(-19), maxAutomaticAttempts: PurgeCandidateDocumentStorageJob.MaxAttempts);
                break;
            case CandidateDocumentDeletionOperation.StatusProcessing:
                operation.Claim(Guid.NewGuid(), Now.AddMinutes(-1));
                if (leaseExpiresAt.HasValue)
                    db.Entry(operation).Property("LeaseExpiresAt").CurrentValue = leaseExpiresAt.Value;
                break;
            case CandidateDocumentDeletionOperation.StatusCompleted:
                operation.Claim(Guid.NewGuid(), Now.AddMinutes(-20));
                operation.MarkCompleted(Now.AddMinutes(-19));
                break;
            case CandidateDocumentDeletionOperation.StatusHeld:
                operation.MarkHeld(Now.AddMinutes(-20));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        db.CandidateDocumentDeletionOperations.Add(operation);
        db.SaveChanges();
        return operation;
    }

    [Fact]
    public async Task ExecuteAsync_Claims_Pending_Deletion_Operation_And_Enqueues_It()
    {
        await using var db = BuildContext();
        var operation = SeedDeletionOperation(db, CandidateDocumentDeletionOperation.StatusPending);

        var jobClient = new RecordingBackgroundJobClient();
        await BuildJob(db, jobClient, new FakeAuditPublisher()).ExecuteAsync();

        var enqueued = Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == operation.Id);
        Assert.Equal(typeof(PurgeCandidateDocumentStorageJob), enqueued.Type);

        // Ticket 19 (P2): claimed straight to Processing (not left Pending) — a bare status reset
        // would leave the row indistinguishable from any other untouched Pending row.
        var reloaded = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusProcessing, reloaded.Status);
        Assert.NotNull(reloaded.ClaimedBy);
        Assert.NotNull(reloaded.LeaseExpiresAt);
    }

    [Fact]
    public async Task ExecuteAsync_Claims_Failed_Deletion_Operation_And_Enqueues_It()
    {
        await using var db = BuildContext();
        var operation = SeedDeletionOperation(db, CandidateDocumentDeletionOperation.StatusFailed);

        var jobClient = new RecordingBackgroundJobClient();
        await BuildJob(db, jobClient, new FakeAuditPublisher()).ExecuteAsync();

        var reloaded = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusProcessing, reloaded.Status);
        Assert.Null(reloaded.FailureReason);

        Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == operation.Id);
    }

    [Fact]
    public async Task ExecuteAsync_Claims_Stale_Processing_Deletion_Operation_And_Enqueues_It()
    {
        // Ticket 19 (P2): staleness is now determined by the lease having actually expired.
        await using var db = BuildContext();
        var operation = SeedDeletionOperation(
            db, CandidateDocumentDeletionOperation.StatusProcessing, leaseExpiresAt: Now.AddMinutes(-5));

        var jobClient = new RecordingBackgroundJobClient();
        await BuildJob(db, jobClient, new FakeAuditPublisher()).ExecuteAsync();

        var reloaded = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusProcessing, reloaded.Status);
        Assert.True(reloaded.LeaseExpiresAt > Now); // re-claimed under a fresh lease

        Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == operation.Id);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Touch_Or_Enqueue_Fresh_Processing_Deletion_Operation()
    {
        await using var db = BuildContext();
        var operation = SeedDeletionOperation(
            db, CandidateDocumentDeletionOperation.StatusProcessing, leaseExpiresAt: Now.AddMinutes(5));

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

    // ---- Ticket 18 (P1): Held operations are re-checked, not blindly re-enqueued every sweep ----

    [Fact]
    public async Task ExecuteAsync_Does_Not_Touch_Or_Enqueue_Held_Operation_While_Still_Under_Legal_Hold()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var operation = SeedDeletionOperation(db, CandidateDocumentDeletionOperation.StatusHeld, companyId: companyId);

        var jobClient = new RecordingBackgroundJobClient();
        var auditPublisher = new FakeAuditPublisher();
        await BuildJob(db, jobClient, auditPublisher, new FakeLegalHoldStatusReader(companyId)).ExecuteAsync();

        Assert.DoesNotContain(jobClient.CreatedJobs, j => (Guid)j.Args[0] == operation.Id);

        var reloaded = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusHeld, reloaded.Status); // untouched
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Resumes_Held_Operation_And_Publishes_Resumed_Audit_Once_Hold_Lifts()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var operation = SeedDeletionOperation(db, CandidateDocumentDeletionOperation.StatusHeld, companyId: companyId);

        var jobClient = new RecordingBackgroundJobClient();
        var auditPublisher = new FakeAuditPublisher();
        // Hold has been lifted — the fake reports no companies under hold.
        await BuildJob(db, jobClient, auditPublisher, new FakeLegalHoldStatusReader()).ExecuteAsync();

        var reloaded = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        // Ticket 19 (P2): claimed straight to Processing on resume (not left Pending).
        Assert.Equal(CandidateDocumentDeletionOperation.StatusProcessing, reloaded.Status);
        Assert.NotNull(reloaded.ClaimedBy);
        Assert.Equal(operation.StorageKey, reloaded.StorageKey); // never lost while held

        Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == operation.Id);
        Assert.Single(auditPublisher.Published.OfType<CandidateDocumentDeletionResumedAfterLegalHoldAuditEvent>());
    }

    // ---- Ticket 19 (P2): terminal failures, and atomic claim under concurrency -------------------

    [Fact]
    public async Task ExecuteAsync_Does_Not_Reset_A_Terminally_Failed_Operation()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var operation = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "some/storage/key.pdf", Now.AddMinutes(-30));
        for (var i = 0; i < PurgeCandidateDocumentStorageJob.MaxAttempts; i++)
            operation.Claim(Guid.NewGuid(), Now.AddMinutes(-20 + i));
        operation.MarkFailed("boom", Now.AddMinutes(-10), maxAutomaticAttempts: PurgeCandidateDocumentStorageJob.MaxAttempts);
        Assert.True(operation.IsTerminallyFailed);
        db.CandidateDocumentDeletionOperations.Add(operation);
        await db.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();
        await BuildJob(db, jobClient, new FakeAuditPublisher()).ExecuteAsync();

        Assert.DoesNotContain(jobClient.CreatedJobs, j => (Guid)j.Args[0] == operation.Id);

        var reloaded = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusFailed, reloaded.Status); // untouched
        Assert.True(reloaded.IsTerminallyFailed);
    }

    [Fact]
    public async Task ExecuteAsync_Claims_A_Record_Manually_Retried_After_Terminal_Failure()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var operation = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "some/storage/key.pdf", Now.AddMinutes(-30));
        for (var i = 0; i < PurgeCandidateDocumentStorageJob.MaxAttempts; i++)
            operation.Claim(Guid.NewGuid(), Now.AddMinutes(-20 + i));
        operation.MarkFailed("boom", Now.AddMinutes(-10), maxAutomaticAttempts: PurgeCandidateDocumentStorageJob.MaxAttempts);
        operation.RecordManualRetry(Guid.NewGuid(), "Confirmed the storage outage is resolved.", Now.AddMinutes(-5));
        db.CandidateDocumentDeletionOperations.Add(operation);
        await db.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();
        await BuildJob(db, jobClient, new FakeAuditPublisher()).ExecuteAsync();

        Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == operation.Id);
    }

    [Fact]
    public async Task ExecuteAsync_From_Two_Concurrent_Reconcilers_Only_One_Claims_And_Enqueues_The_Same_Stale_Record()
    {
        // Ticket 19 (P2): the core claim/lease correctness guarantee, verified against a real
        // Postgres database — same pattern already validated for AccountDisablementReconciliationJob.
        var fixture = new RecruitmentDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            var companyId = Guid.NewGuid();
            await using (var seedDb = fixture.BuildContext())
            {
                var operation = CandidateDocumentDeletionOperation.CreatePending(
                    Guid.NewGuid(), companyId, Guid.NewGuid(), "some/storage/key.pdf", Now.AddMinutes(-30));
                operation.Claim(Guid.NewGuid(), Now.AddMinutes(-20));
                seedDb.CandidateDocumentDeletionOperations.Add(operation);
                await seedDb.SaveChangesAsync();
                seedDb.Entry(operation).Property("LeaseExpiresAt").CurrentValue = Now.AddMinutes(-5);
                await seedDb.SaveChangesAsync();

                var operationId = operation.Id;

                await using var dbA = fixture.BuildContext();
                await using var dbB = fixture.BuildContext();

                var jobClientA = new RecordingBackgroundJobClient();
                var jobClientB = new RecordingBackgroundJobClient();

                var jobA = new PurgeCandidateDocumentStorageReconciliationJob(
                    dbA, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeLegalHoldStatusReader(),
                    jobClientA, NullLogger<PurgeCandidateDocumentStorageReconciliationJob>.Instance);
                var jobB = new PurgeCandidateDocumentStorageReconciliationJob(
                    dbB, new FakeClock(FixedUtcNow), new FakeAuditPublisher(), new FakeLegalHoldStatusReader(),
                    jobClientB, NullLogger<PurgeCandidateDocumentStorageReconciliationJob>.Instance);

                var taskA = jobA.ExecuteAsync();
                var taskB = jobB.ExecuteAsync();
                await Task.WhenAll(taskA, taskB);

                var enqueuedByA = jobClientA.CreatedJobs.Count(j => (Guid)j.Args[0] == operationId);
                var enqueuedByB = jobClientB.CreatedJobs.Count(j => (Guid)j.Args[0] == operationId);

                Assert.Equal(1, enqueuedByA + enqueuedByB);

                await using var verify = fixture.BuildContext();
                var reloaded = await verify.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operationId);
                Assert.Equal(CandidateDocumentDeletionOperation.StatusProcessing, reloaded.Status);
                Assert.NotNull(reloaded.ClaimedBy);
            }
        }
        finally
        {
            await fixture.DisposeAsync();
        }
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
