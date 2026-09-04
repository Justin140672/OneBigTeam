using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

/// <summary>
/// OBT-REM-12: <see cref="ReconcileMissingNotificationAuditsJob"/> — periodic recovery for
/// NotificationCreatedAuditEvents that may have been lost when a caller committed a Notification but
/// crashed before publishing. See NotificationsAuditTests for why republishing a deterministic
/// EventId is always safe, and NotificationWriterRepairTests for the crashed-writer repair path this
/// job's grace/lookback window backstops.
/// </summary>
public class ReconcileMissingNotificationAuditsJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static NotificationsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ReconcileMissingNotificationAuditsJob BuildJob(
        NotificationsDbContext db, FakeAuditPublisher auditPublisher, FakeAuditEventExistenceReader existenceReader) =>
        new(db, new FakeClock(Now.UtcDateTime), auditPublisher, existenceReader,
            new FakeLogger<ReconcileMissingNotificationAuditsJob>());

    private static async Task<Guid> SeedNotificationAsync(
        NotificationsDbContext db, Guid companyId, DateTimeOffset createdAt)
    {
        var id = Guid.NewGuid();
        db.Notifications.Add(Notification.Create(
            id, companyId, Guid.NewGuid(), "Test", null, Guid.NewGuid(), createdAt, NotificationType.LeaveApproved));
        await db.SaveChangesAsync();
        return id;
    }

    private static DateTimeOffset InWindow() =>
        Now.AddMinutes(-(ReconcileMissingNotificationAuditsJob.GraceMinutes + 30));

    [Fact]
    public async Task ExecuteAsync_Skips_Notifications_Where_Audit_Already_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var id = await SeedNotificationAsync(db, companyId, InWindow());

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader([id]);
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();

        Assert.Empty(auditPublisher.Published);
        Assert.Contains(id, existenceReader.Queried);
    }

    [Fact]
    public async Task ExecuteAsync_Republishes_Notifications_Where_Audit_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var id = Guid.NewGuid();
        db.Notifications.Add(Notification.Create(
            id, companyId, employeeId, "Leave approved", null, Guid.NewGuid(), InWindow(), NotificationType.LeaveApproved));
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader();
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();

        var evt = Assert.Single(auditPublisher.Published);
        var created = Assert.IsType<NotificationCreatedAuditEvent>(evt);
        Assert.Equal(id, created.NotificationId);
        Assert.Equal(companyId, created.CompanyId);
        Assert.Equal(employeeId, created.RecipientEmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Ignores_Notification_Newer_Than_GraceMinutes()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var recentId = await SeedNotificationAsync(db, companyId, Now.AddMinutes(-1));

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader();
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();

        Assert.Empty(auditPublisher.Published);
        Assert.DoesNotContain(recentId, existenceReader.Queried);
    }

    [Fact]
    public async Task ExecuteAsync_Boundary_Exactly_At_GraceMinutes_Cutoff_Is_Not_Yet_Eligible()
    {
        // Job uses CreatedAt < cutoff (strict) — a notification created exactly GraceMinutes ago has
        // CreatedAt == cutoff, which must not be picked up yet.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var boundaryId = await SeedNotificationAsync(
            db, companyId, Now.AddMinutes(-ReconcileMissingNotificationAuditsJob.GraceMinutes));

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader();
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();

        Assert.DoesNotContain(boundaryId, existenceReader.Queried);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Ignores_Notification_Older_Than_LookbackHours()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var tooOldId = await SeedNotificationAsync(
            db, companyId, Now.AddHours(-(ReconcileMissingNotificationAuditsJob.LookbackHours + 1)));

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader();
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();

        Assert.Empty(auditPublisher.Published);
        Assert.DoesNotContain(tooOldId, existenceReader.Queried);
    }

    [Fact]
    public async Task ExecuteAsync_Boundary_Exactly_At_LookbackHours_Is_Still_Eligible()
    {
        // Job uses CreatedAt >= lookback (inclusive) — a notification created exactly LookbackHours
        // ago must still be scanned.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var boundaryId = await SeedNotificationAsync(
            db, companyId, Now.AddHours(-ReconcileMissingNotificationAuditsJob.LookbackHours));

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader();
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();

        Assert.Contains(boundaryId, existenceReader.Queried);
        var evt = Assert.Single(auditPublisher.Published);
        Assert.Equal(boundaryId, ((NotificationCreatedAuditEvent)evt).NotificationId);
    }

    [Fact]
    public async Task ExecuteAsync_Respects_BatchSizePerCompany_Cap()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var inWindow = InWindow();

        var total = ReconcileMissingNotificationAuditsJob.BatchSizePerCompany + 10;
        for (var i = 0; i < total; i++)
        {
            await SeedNotificationAsync(db, companyId, inWindow.AddSeconds(-i));
        }

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader();
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();

        Assert.Equal(ReconcileMissingNotificationAuditsJob.BatchSizePerCompany, auditPublisher.Published.Count);
    }

    [Fact]
    public async Task ExecuteAsync_Is_Tenant_Scoped_Across_Companies()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var idA = await SeedNotificationAsync(db, companyA, InWindow());
        var idB = await SeedNotificationAsync(db, companyB, InWindow());

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader();
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();

        var publishedIds = auditPublisher.Published.Cast<NotificationCreatedAuditEvent>()
            .Select(e => e.NotificationId).ToList();
        Assert.Contains(idA, publishedIds);
        Assert.Contains(idB, publishedIds);
        Assert.Equal(2, publishedIds.Count);
    }

    [Fact]
    public async Task ExecuteAsync_Nothing_In_Window_Does_Nothing_And_Does_Not_Throw()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader();
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();

        Assert.Empty(auditPublisher.Published);
    }

    // Cancellation ---------------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Already_Cancelled_Token_Throws_Before_Publishing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        await SeedNotificationAsync(db, companyId, InWindow());

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader();
        var job = BuildJob(db, auditPublisher, existenceReader);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.ExecuteAsync(cts.Token));
        Assert.Empty(auditPublisher.Published);
    }

    // OBT-REM-14: keyset-cursor forward progress ----------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Multiple_Runs_Advance_Cursor_Past_Already_Audited_Batch_To_Reach_Missing_Audit()
    {
        // Regression for the bug OBT-REM-14 fixes: a fixed Take(BatchSizePerCompany) every run would
        // re-select the same already-audited oldest notifications forever and never reach a
        // genuinely-missing audit further back in the window. With the keyset cursor, run 1 only
        // advances through the already-audited batch; the missing one is only reached (and repaired)
        // on run 2.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var inWindow = InWindow();

        var alreadyAuditedIds = new List<Guid>();
        for (var i = 0; i < ReconcileMissingNotificationAuditsJob.BatchSizePerCompany + 20; i++)
        {
            var id = await SeedNotificationAsync(db, companyId, inWindow.AddHours(-1).AddSeconds(-i));
            alreadyAuditedIds.Add(id);
        }

        // The genuinely missing one is newer than all the already-audited ones, so it sorts after
        // them in the (CreatedAt, Id) keyset order and only becomes reachable once the cursor has
        // advanced past the full first batch.
        var missingId = await SeedNotificationAsync(db, companyId, inWindow);

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader(alreadyAuditedIds);
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();
        Assert.Empty(auditPublisher.Published);
        Assert.DoesNotContain(missingId, existenceReader.Queried);

        await job.ExecuteAsync();
        var evt = Assert.Single(auditPublisher.Published);
        Assert.Equal(missingId, ((NotificationCreatedAuditEvent)evt).NotificationId);
    }

    [Fact]
    public async Task ExecuteAsync_Repairs_Missing_Audits_Scattered_Across_Multiple_Batches_Over_Successive_Runs()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        // Window is [lookback, cutoff) = [Now - LookbackHours, Now - GraceMinutes) — every seeded
        // CreatedAt must fall strictly inside this ~23h45m span for the row to be eligible at all.
        var lookback = Now.AddHours(-ReconcileMissingNotificationAuditsJob.LookbackHours);
        var cutoff = Now.AddMinutes(-ReconcileMissingNotificationAuditsJob.GraceMinutes);
        var windowSeconds = (cutoff - lookback).TotalSeconds;

        var batchSize = ReconcileMissingNotificationAuditsJob.BatchSizePerCompany;
        var total = (batchSize * 3) - 10; // spans 3 batches
        var spacingSeconds = (windowSeconds - 60) / total; // leave a margin below cutoff
        var alreadyAuditedIds = new List<Guid>();
        var missingIds = new List<Guid>();

        // Oldest first so ordering by CreatedAt ascending places index 0 in batch 1, etc.
        // Place a genuinely-missing notification near the start of each of the 3 batches.
        var missingOffsets = new HashSet<int> { 5, batchSize + 5, (batchSize * 2) + 5 };

        for (var i = 0; i < total; i++)
        {
            var createdAt = lookback.AddSeconds(i * spacingSeconds);
            var id = await SeedNotificationAsync(db, companyId, createdAt);
            if (missingOffsets.Contains(i))
            {
                missingIds.Add(id);
            }
            else
            {
                alreadyAuditedIds.Add(id);
            }
        }

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader(alreadyAuditedIds);
        var job = BuildJob(db, auditPublisher, existenceReader);

        // One execution per batch.
        await job.ExecuteAsync();
        await job.ExecuteAsync();
        await job.ExecuteAsync();

        var publishedIds = auditPublisher.Published.Cast<NotificationCreatedAuditEvent>()
            .Select(e => e.NotificationId).ToList();
        foreach (var missingId in missingIds)
        {
            Assert.Contains(missingId, publishedIds);
        }

        Assert.Equal(missingIds.Count, publishedIds.Count);
    }

    [Fact]
    public async Task ExecuteAsync_Small_Company_Backlog_Is_Not_Starved_By_Large_Company_Backlog_In_Same_Run()
    {
        // Each company gets its own up-to-BatchSizePerCompany allowance per run — company B's small
        // backlog must be fully resolved in the very first execution regardless of company A's size.
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var inWindow = InWindow();

        var companyAAuditedIds = new List<Guid>();
        for (var i = 0; i < 500; i++)
        {
            companyAAuditedIds.Add(await SeedNotificationAsync(db, companyA, inWindow.AddHours(-1).AddSeconds(-i)));
        }
        var companyAMissingId = await SeedNotificationAsync(db, companyA, inWindow);

        var companyBAuditedIds = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            companyBAuditedIds.Add(await SeedNotificationAsync(db, companyB, inWindow.AddHours(-1).AddSeconds(-i)));
        }
        var companyBMissingId = await SeedNotificationAsync(db, companyB, inWindow);

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader([.. companyAAuditedIds, .. companyBAuditedIds]);
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();

        var publishedIds = auditPublisher.Published.Cast<NotificationCreatedAuditEvent>()
            .Select(e => e.NotificationId).ToList();

        Assert.Contains(companyBMissingId, publishedIds);
        Assert.DoesNotContain(companyAMissingId, publishedIds); // company A hasn't reached it yet — separate batch budget, not starvation of B
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Requery_Already_Scanned_Ids_Once_Window_Is_Exhausted()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var inWindow = InWindow();

        var ids = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            ids.Add(await SeedNotificationAsync(db, companyId, inWindow.AddSeconds(-i)));
        }

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader(ids);
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();
        var firstRunQueried = existenceReader.Queried.ToList();
        Assert.Equal(ids.Count, firstRunQueried.Count);

        // Second run: cursor has caught up to the end of the window (no candidates ahead of it), so
        // the candidate query returns zero rows and the cursor is reset rather than re-scanned.
        await job.ExecuteAsync();

        var secondRunQueried = existenceReader.Queried.Skip(firstRunQueried.Count).ToList();
        Assert.Empty(secondRunQueried);
    }

    [Fact]
    public async Task ExecuteAsync_Concurrent_Runs_Each_Publish_Deterministic_Content_For_Same_Missing_Notification()
    {
        // Two job instances built against DbContexts pointing at the same in-memory database name,
        // racing (via Task.WhenAll) on the same missing-audit notification. Because the cursor is
        // durable and shared via the underlying store, whichever job's SaveChangesAsync commits
        // first "wins" the row: the loser may see the cursor already advanced past it (candidates
        // empty -> nothing to check/publish) or may still see it as a candidate and independently
        // publish. Either outcome is safe — this test does not assert exactly one publish call
        // happens (that depends on scheduling); instead it asserts the safety property that actually
        // matters: whichever job(s) do publish always publish identical, correct content for the
        // same notification, which is exactly the property that makes NotificationCreatedAuditEvent's
        // deterministic EventId dedup safe at the real infrastructure layer (DbAuditEventPublisher's
        // unique EventId constraint — see NotificationsAuditTests) even under a genuine race.
        var dbName = Guid.NewGuid().ToString("N");
        await using var db1 = new NotificationsDbContext(
            new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(dbName).Options);

        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var id = Guid.NewGuid();
        db1.Notifications.Add(Notification.Create(
            id, companyId, employeeId, "Leave approved", null, Guid.NewGuid(), InWindow(), NotificationType.LeaveApproved));
        await db1.SaveChangesAsync();

        await using var db2 = new NotificationsDbContext(
            new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(dbName).Options);

        var publisher1 = new FakeAuditPublisher();
        var publisher2 = new FakeAuditPublisher();
        var job1 = BuildJob(db1, publisher1, new FakeAuditEventExistenceReader());
        var job2 = BuildJob(db2, publisher2, new FakeAuditEventExistenceReader());

        await Task.WhenAll(job1.ExecuteAsync(), job2.ExecuteAsync());

        var allPublished = publisher1.Published.Concat(publisher2.Published)
            .Cast<NotificationCreatedAuditEvent>()
            .ToList();

        // At least one of the two racing runs must have found and repaired the missing audit.
        Assert.NotEmpty(allPublished);
        Assert.All(allPublished, e => Assert.Equal(id, e.NotificationId));
        Assert.All(allPublished, e => Assert.Equal(companyId, e.CompanyId));
        Assert.All(allPublished, e => Assert.Equal(employeeId, e.RecipientEmployeeId));
    }

    [Fact]
    public async Task ExecuteAsync_Retry_After_Simulated_Crash_Skips_Republish_But_Still_Advances_Cursor()
    {
        // Simulates the crash/retry safety net described in the job's XML doc: a prior run is
        // presumed to have successfully published the creation audit for this notification (the
        // durable audit store already reflects it — modelled here by seeding the existence reader
        // with the id up front) but then crashed before its own cursor SaveChangesAsync could
        // commit (modelled by not persisting any cursor row before this run starts). This run
        // ("the retry") must: (a) not republish (existence check finds it already audited), and
        // (b) still make durable cursor progress past it despite never having repaired anything
        // itself — proving the cursor advances on scan, not on repair, so a subsequent run doesn't
        // re-scan the same row forever.
        var dbName = Guid.NewGuid().ToString("N");
        await using var db1 = new NotificationsDbContext(
            new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(dbName).Options);

        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        db1.Notifications.Add(Notification.Create(
            id, companyId, Guid.NewGuid(), "Test", null, Guid.NewGuid(), InWindow(), NotificationType.LeaveApproved));
        await db1.SaveChangesAsync();

        var publisher1 = new FakeAuditPublisher();
        var existenceReader1 = new FakeAuditEventExistenceReader([id]);
        var job1 = BuildJob(db1, publisher1, existenceReader1);
        await job1.ExecuteAsync();

        Assert.Empty(publisher1.Published); // no duplicate publish — already durably audited
        Assert.Contains(id, existenceReader1.Queried);

        // A subsequent run finds nothing new to scan (batch returns 0 rows) since the window's only
        // notification has already been scanned/advanced past by the "retry" run above — proving the
        // cursor persisted even though that run repaired nothing itself.
        var publisher3 = new FakeAuditPublisher();
        var existenceReader3 = new FakeAuditEventExistenceReader([id]);
        await using var db3 = new NotificationsDbContext(
            new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(dbName).Options);
        var job3 = BuildJob(db3, publisher3, existenceReader3);
        await job3.ExecuteAsync();

        Assert.Empty(existenceReader3.Queried); // nothing left ahead of the cursor to re-check
    }

    [Fact]
    public async Task ExecuteAsync_Stale_Cursor_Predating_Current_Lookback_Start_Resumes_From_Window_Start()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var inWindow = InWindow();
        var id = await SeedNotificationAsync(db, companyId, inWindow);

        // Seed a cursor row directly with a resume point far before the current lookback window —
        // simulating a cursor left over from a previous run whose window has since slid forward.
        db.NotificationAuditReconciliationCursors.Add(
            NotificationAuditReconciliationCursor.Create(
                companyId, Now.AddDays(-30), Guid.NewGuid(), Now.AddDays(-30)));
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var existenceReader = new FakeAuditEventExistenceReader();
        var job = BuildJob(db, auditPublisher, existenceReader);

        await job.ExecuteAsync();

        // Treated as resume-from-start rather than being permanently stuck past the current window.
        Assert.Contains(id, existenceReader.Queried);
        var evt = Assert.Single(auditPublisher.Published);
        Assert.Equal(id, ((NotificationCreatedAuditEvent)evt).NotificationId);
    }
}
