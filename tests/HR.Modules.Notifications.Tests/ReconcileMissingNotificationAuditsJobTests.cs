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
}
