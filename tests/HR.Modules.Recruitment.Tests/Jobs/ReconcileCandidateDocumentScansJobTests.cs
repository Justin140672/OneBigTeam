using HR.Modules.Recruitment;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests.Jobs;

/// <summary>
/// [P1] ReconcileCandidateDocumentScansJob: the recurring backstop that re-dispatches due Pending
/// scans (lost enqueue, lost retry schedule, rows backfilled to Pending by the migration) and
/// releases abandoned Scanning claims.
/// </summary>
public class ReconcileCandidateDocumentScansJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow);

    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _databaseName = Guid.NewGuid().ToString("N");
    private readonly RecordingBackgroundJobClient _jobs = new();
    private readonly FakeAuditPublisher _audit = new();

    private RecruitmentDbContext NewContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(_databaseName, _root)
            .Options);

    private async Task RunAsync()
    {
        await using var db = NewContext();
        await new ReconcileCandidateDocumentScansJob(
                db, _jobs, _audit, new FakeClock(FixedUtcNow),
                NullLogger<ReconcileCandidateDocumentScansJob>.Instance)
            .ExecuteAsync();
    }

    private async Task<CandidateDocument> SeedAsync(DateTimeOffset createdAt, Action<CandidateDocument>? arrange = null)
    {
        var document = CandidateDocument.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "CV", "cv.pdf", 1024, "application/pdf",
            $"key/{Guid.NewGuid():N}/cv.pdf", Guid.NewGuid(), createdAt, CandidateDocumentKind.Cv);
        arrange?.Invoke(document);

        await using var db = NewContext();
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();
        return document;
    }

    private async Task<CandidateDocument> ReloadAsync(Guid id)
    {
        await using var db = NewContext();
        return await db.CandidateDocuments.AsNoTracking().SingleAsync(d => d.Id == id);
    }

    private List<Guid> DispatchedScanIds() =>
        _jobs.CreatedJobs
            .Where(j => j.Type == typeof(ScanCandidateDocumentJob) && j.Method.Name == nameof(ScanCandidateDocumentJob.ScanAsync))
            .Select(j => (Guid)j.Args[0]!)
            .ToList();

    // ── Due Pending rows ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Backfilled_Pending_Row_Older_Than_Grace_Is_Dispatched()
    {
        // A row that existed before scanning was introduced: Pending, never attempted, old CreatedAt.
        var backfilled = await SeedAsync(Now.AddMonths(-6));

        await RunAsync();

        Assert.Equal(new[] { backfilled.Id }, DispatchedScanIds());
        // Dispatch alone does not change the row — the scan job's own claim does.
        var after = await ReloadAsync(backfilled.Id);
        Assert.Equal(CandidateDocumentScanStatus.Pending, after.ScanStatus);
        Assert.Equal(0, after.ScanAttemptCount);
    }

    [Fact]
    public async Task Pending_Row_Created_Exactly_At_Grace_Cutoff_Is_Dispatched()
    {
        var atCutoff = await SeedAsync(Now - ReconcileCandidateDocumentScansJob.NewUploadGracePeriod);

        await RunAsync();

        Assert.Contains(atCutoff.Id, DispatchedScanIds());
    }

    [Fact]
    public async Task Brand_New_Pending_Row_Within_Grace_Is_Not_Dispatched()
    {
        var justInside = await SeedAsync(Now - ReconcileCandidateDocumentScansJob.NewUploadGracePeriod + TimeSpan.FromSeconds(1));
        var fresh = await SeedAsync(Now);

        await RunAsync();

        Assert.DoesNotContain(justInside.Id, DispatchedScanIds());
        Assert.DoesNotContain(fresh.Id, DispatchedScanIds());
        Assert.Equal(TimeSpan.FromMinutes(2), ReconcileCandidateDocumentScansJob.NewUploadGracePeriod);
    }

    [Fact]
    public async Task Pending_Row_In_Back_Off_Is_Not_Dispatched_Until_Due()
    {
        // Old enough to be past the grace period — but the back-off takes precedence.
        var inBackOff = await SeedAsync(Now.AddHours(-1), d =>
        {
            d.BeginScanAttempt(Now.AddMinutes(-2));
            d.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScannerUnavailable, Now.AddMinutes(-2), Now.AddTicks(1));
        });

        await RunAsync();

        Assert.DoesNotContain(inBackOff.Id, DispatchedScanIds());
    }

    [Fact]
    public async Task Pending_Row_Whose_Back_Off_Has_Elapsed_Is_Dispatched()
    {
        // Exactly due (ScanNextAttemptAt == now) — the boundary is inclusive.
        var due = await SeedAsync(Now.AddHours(-1), d =>
        {
            d.BeginScanAttempt(Now.AddMinutes(-2));
            d.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScannerUnavailable, Now.AddMinutes(-2), Now);
        });

        await RunAsync();

        Assert.Contains(due.Id, DispatchedScanIds());
    }

    [Fact]
    public async Task Retry_Row_Is_Dispatched_Even_If_Created_Within_The_Grace_Period()
    {
        // ScanNextAttemptAt, when set, governs instead of the new-upload grace period.
        var retry = await SeedAsync(Now.AddSeconds(-30), d =>
        {
            d.BeginScanAttempt(Now.AddSeconds(-20));
            d.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScannerUnavailable, Now.AddSeconds(-20), Now.AddSeconds(-1));
        });

        await RunAsync();

        Assert.Contains(retry.Id, DispatchedScanIds());
    }

    [Theory]
    [InlineData(nameof(CandidateDocumentScanStatus.Clean))]
    [InlineData(nameof(CandidateDocumentScanStatus.Infected))]
    [InlineData(nameof(CandidateDocumentScanStatus.Failed))]
    public async Task Terminal_Rows_Are_Never_Dispatched_Or_Touched(string terminalName)
    {
        var terminal = Enum.Parse<CandidateDocumentScanStatus>(terminalName);
        var document = await SeedAsync(Now.AddDays(-30), d =>
        {
            var t = Now.AddDays(-29);
            switch (terminal)
            {
                case CandidateDocumentScanStatus.Clean:
                    d.BeginScanAttempt(t);
                    d.MarkScanClean(t);
                    break;
                case CandidateDocumentScanStatus.Infected:
                    d.BeginScanAttempt(t);
                    d.MarkScanInfected("Eicar-Test-Signature", t);
                    break;
                default:
                    for (var i = 0; i < CandidateDocument.MaxScanAttempts; i++)
                    {
                        d.BeginScanAttempt(t.AddHours(i));
                        d.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScannerUnavailable, t.AddHours(i), t.AddHours(i));
                    }
                    break;
            }
        });

        await RunAsync();

        Assert.Empty(DispatchedScanIds());
        Assert.Empty(_audit.Published);
        Assert.Equal(terminal, (await ReloadAsync(document.Id)).ScanStatus);
    }

    // ── Abandoned Scanning claims ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Expired_Scanning_Claim_Is_Released_To_Pending_Audited_And_Redispatched()
    {
        var abandoned = await SeedAsync(Now.AddHours(-1),
            d => d.BeginScanAttempt(Now - CandidateDocument.ScanLeaseDuration));

        await RunAsync();

        var after = await ReloadAsync(abandoned.Id);
        Assert.Equal(CandidateDocumentScanStatus.Pending, after.ScanStatus);
        Assert.Equal(1, after.ScanAttemptCount);
        Assert.Equal(Now, after.ScanNextAttemptAt);
        Assert.Equal(CandidateDocumentScanFailureReasons.ScanAbandoned, after.ScanFailureReason);
        Assert.False(after.IsDownloadable);

        var audit = Assert.Single(_audit.Published.OfType<CandidateDocumentScanStatusChangedAuditEvent>());
        Assert.Equal(abandoned.Id, audit.DocumentId);
        Assert.Equal(nameof(CandidateDocumentScanStatus.Scanning), audit.PreviousStatus);
        Assert.Equal(nameof(CandidateDocumentScanStatus.Pending), audit.NewStatus);
        Assert.Equal(1, audit.AttemptCount);
        Assert.Equal(CandidateDocumentScanFailureReasons.ScanAbandoned, audit.Reason);

        // Released rows are immediately due, so the same sweep re-dispatches them.
        Assert.Contains(abandoned.Id, DispatchedScanIds());
    }

    [Fact]
    public async Task Expired_Scanning_Claim_With_Exhausted_Attempts_Becomes_Failed_And_Is_Not_Redispatched()
    {
        var exhausted = await SeedAsync(Now.AddDays(-1), d =>
        {
            var t = Now.AddHours(-10);
            for (var i = 0; i < CandidateDocument.MaxScanAttempts - 1; i++)
            {
                d.BeginScanAttempt(t.AddMinutes(i));
                d.RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScannerUnavailable, t.AddMinutes(i), t.AddMinutes(i));
            }
            d.BeginScanAttempt(Now.AddHours(-1)); // fifth and final attempt, abandoned
        });

        await RunAsync();

        var after = await ReloadAsync(exhausted.Id);
        Assert.Equal(CandidateDocumentScanStatus.Failed, after.ScanStatus);
        Assert.Equal(CandidateDocument.MaxScanAttempts, after.ScanAttemptCount);
        Assert.Equal(CandidateDocumentScanFailureReasons.ScanAbandoned, after.ScanFailureReason);
        Assert.False(after.IsDownloadable);

        var audit = Assert.Single(_audit.Published.OfType<CandidateDocumentScanStatusChangedAuditEvent>());
        Assert.Equal(nameof(CandidateDocumentScanStatus.Failed), audit.NewStatus);
        Assert.DoesNotContain(exhausted.Id, DispatchedScanIds());
    }

    [Fact]
    public async Task Live_Scanning_Claim_Is_Left_Alone()
    {
        // One tick short of the lease expiring.
        var live = await SeedAsync(Now.AddHours(-1),
            d => d.BeginScanAttempt(Now - CandidateDocument.ScanLeaseDuration + TimeSpan.FromTicks(1)));

        await RunAsync();

        var after = await ReloadAsync(live.Id);
        Assert.Equal(CandidateDocumentScanStatus.Scanning, after.ScanStatus);
        Assert.Equal(1, after.ScanAttemptCount);
        Assert.Empty(_audit.Published);
        Assert.DoesNotContain(live.Id, DispatchedScanIds());
    }

    // ── Robustness ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Job_Store_Outage_During_Dispatch_Does_Not_Fail_The_Sweep_Or_Change_Rows()
    {
        var backfilled = await SeedAsync(Now.AddDays(-3));

        await using (var db = NewContext())
        {
            var client = new ThrowingBackgroundJobClient();
            await new ReconcileCandidateDocumentScansJob(
                    db, client, _audit, new FakeClock(FixedUtcNow),
                    NullLogger<ReconcileCandidateDocumentScansJob>.Instance)
                .ExecuteAsync();
            Assert.Equal(1, client.Attempts);
        }

        // Still Pending, so the next sweep picks it up again.
        Assert.Equal(CandidateDocumentScanStatus.Pending, (await ReloadAsync(backfilled.Id)).ScanStatus);
        await RunAsync();
        Assert.Contains(backfilled.Id, DispatchedScanIds());
    }

    [Fact]
    public async Task Each_Due_Row_Is_Dispatched_Exactly_Once_Per_Sweep()
    {
        var a = await SeedAsync(Now.AddDays(-2));
        var b = await SeedAsync(Now.AddDays(-1));

        await RunAsync();

        var dispatched = DispatchedScanIds();
        Assert.Equal(2, dispatched.Count);
        // Oldest first.
        Assert.Equal(new[] { a.Id, b.Id }, dispatched);
    }

    [Fact]
    public async Task Empty_Table_Is_A_No_Op()
    {
        await RunAsync();

        Assert.Empty(_jobs.CreatedJobs);
        Assert.Empty(_audit.Published);
    }
}
