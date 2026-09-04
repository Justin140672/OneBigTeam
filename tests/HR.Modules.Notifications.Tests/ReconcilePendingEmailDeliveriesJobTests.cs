using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

/// <summary>
/// OBT-REM-12: <see cref="ReconcilePendingEmailDeliveriesJob"/> — periodic recovery for EmailDelivery
/// rows stuck Pending because their originating Hangfire enqueue never happened. See
/// EmailDeliveryJobTests for the send-path itself and NotificationWriterRepairTests for the
/// crashed-writer repair path this job's grace period backstops.
/// </summary>
public class ReconcilePendingEmailDeliveriesJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static NotificationsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ReconcilePendingEmailDeliveriesJob BuildJob(
        NotificationsDbContext db, RecordingBackgroundJobClient backgroundJobClient) =>
        new(db, new FakeClock(Now.UtcDateTime), backgroundJobClient, new FakeLogger<ReconcilePendingEmailDeliveriesJob>());

    private static async Task<Guid> SeedDeliveryAsync(
        NotificationsDbContext db, Guid companyId, DateTimeOffset createdAt, EmailDeliveryStatus status)
    {
        var notificationId = Guid.NewGuid();
        var notification = Notification.Create(
            notificationId, companyId, Guid.NewGuid(), "Test", null, Guid.NewGuid(), createdAt);
        db.Notifications.Add(notification);

        var delivery = EmailDelivery.Create(Guid.NewGuid(), companyId, notificationId, createdAt);
        switch (status)
        {
            case EmailDeliveryStatus.Sent:
                delivery.RecordAttempt(createdAt);
                delivery.MarkSent(createdAt);
                break;
            case EmailDeliveryStatus.Failed:
                delivery.RecordAttempt(createdAt);
                delivery.MarkFailed("Invalid recipient address.");
                break;
            case EmailDeliveryStatus.Skipped:
                delivery.MarkSkipped("Email notifications disabled for this company.");
                break;
        }
        db.EmailDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        return notificationId;
    }

    [Fact]
    public async Task ExecuteAsync_Only_ReEnqueues_Pending_Deliveries_Older_Than_GraceMinutes()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var staleId = await SeedDeliveryAsync(
            db, companyId, Now.AddMinutes(-(ReconcilePendingEmailDeliveriesJob.GraceMinutes + 5)), EmailDeliveryStatus.Pending);
        var freshId = await SeedDeliveryAsync(
            db, companyId, Now.AddMinutes(-1), EmailDeliveryStatus.Pending);

        var backgroundJobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, backgroundJobClient);

        await job.ExecuteAsync();

        var enqueuedIds = backgroundJobClient.CreatedJobs.Select(j => (Guid)j.Args[0]!).ToList();
        Assert.Contains(staleId, enqueuedIds);
        Assert.DoesNotContain(freshId, enqueuedIds);
    }

    [Fact]
    public async Task ExecuteAsync_Boundary_Exactly_At_GraceMinutes_Cutoff_Is_Not_Yet_Eligible()
    {
        // GraceMinutes uses a strict "<" comparison in the job (CreatedAt < cutoff) — a row created
        // exactly GraceMinutes ago has CreatedAt == cutoff, which is NOT < cutoff, so it must not be
        // picked up yet (pins the exclusive boundary).
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var boundaryId = await SeedDeliveryAsync(
            db, companyId, Now.AddMinutes(-ReconcilePendingEmailDeliveriesJob.GraceMinutes), EmailDeliveryStatus.Pending);

        var backgroundJobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, backgroundJobClient);

        await job.ExecuteAsync();

        var enqueuedIds = backgroundJobClient.CreatedJobs.Select(j => (Guid)j.Args[0]!).ToList();
        Assert.DoesNotContain(boundaryId, enqueuedIds);
    }

    [Fact]
    public async Task ExecuteAsync_One_Minute_Past_GraceMinutes_Cutoff_Is_Eligible()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var pastId = await SeedDeliveryAsync(
            db, companyId, Now.AddMinutes(-(ReconcilePendingEmailDeliveriesJob.GraceMinutes + 1)), EmailDeliveryStatus.Pending);

        var backgroundJobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, backgroundJobClient);

        await job.ExecuteAsync();

        var enqueuedIds = backgroundJobClient.CreatedJobs.Select(j => (Guid)j.Args[0]!).ToList();
        Assert.Contains(pastId, enqueuedIds);
    }

    // Theory parameters must be a publicly accessible type (xUnit requires public test methods),
    // but EmailDeliveryStatus is internal — pass the enum's underlying int value instead and cast.
    [Theory]
    [InlineData((int)EmailDeliveryStatus.Sent)]
    [InlineData((int)EmailDeliveryStatus.Failed)]
    [InlineData((int)EmailDeliveryStatus.Skipped)]
    public async Task ExecuteAsync_Never_ReEnqueues_Terminal_State_Deliveries(int statusValue)
    {
        var status = (EmailDeliveryStatus)statusValue;
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddMinutes(-(ReconcilePendingEmailDeliveriesJob.GraceMinutes + 30));
        var terminalId = await SeedDeliveryAsync(db, companyId, oldEnough, status);

        var backgroundJobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, backgroundJobClient);

        await job.ExecuteAsync();

        var enqueuedIds = backgroundJobClient.CreatedJobs.Select(j => (Guid)j.Args[0]!).ToList();
        Assert.DoesNotContain(terminalId, enqueuedIds);
        Assert.Empty(backgroundJobClient.CreatedJobs);
    }

    [Fact]
    public async Task ExecuteAsync_Is_Tenant_Scoped_Only_Enqueues_Deliveries_For_Their_Own_Company()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var oldEnough = Now.AddMinutes(-(ReconcilePendingEmailDeliveriesJob.GraceMinutes + 30));

        var idA = await SeedDeliveryAsync(db, companyA, oldEnough, EmailDeliveryStatus.Pending);
        var idB = await SeedDeliveryAsync(db, companyB, oldEnough, EmailDeliveryStatus.Pending);

        var backgroundJobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, backgroundJobClient);

        await job.ExecuteAsync();

        Assert.Equal(2, backgroundJobClient.CreatedJobs.Count);
        var byNotificationId = backgroundJobClient.CreatedJobs.ToDictionary(j => (Guid)j.Args[0]!, j => (Guid)j.Args[1]!);
        Assert.Equal(companyA, byNotificationId[idA]);
        Assert.Equal(companyB, byNotificationId[idB]);
    }

    [Fact]
    public async Task ExecuteAsync_Respects_BatchSizePerCompany_Cap()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddMinutes(-(ReconcilePendingEmailDeliveriesJob.GraceMinutes + 30));

        var total = ReconcilePendingEmailDeliveriesJob.BatchSizePerCompany + 10;
        for (var i = 0; i < total; i++)
        {
            await SeedDeliveryAsync(db, companyId, oldEnough.AddSeconds(-i), EmailDeliveryStatus.Pending);
        }

        var backgroundJobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, backgroundJobClient);

        await job.ExecuteAsync();

        Assert.Equal(ReconcilePendingEmailDeliveriesJob.BatchSizePerCompany, backgroundJobClient.CreatedJobs.Count);
    }

    [Fact]
    public async Task ExecuteAsync_Nothing_Eligible_Enqueues_Nothing_And_Does_Not_Throw()
    {
        await using var db = BuildContext();
        var backgroundJobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, backgroundJobClient);

        await job.ExecuteAsync(); // no rows at all

        Assert.Empty(backgroundJobClient.CreatedJobs);
    }

    // Cancellation ---------------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Already_Cancelled_Token_Throws_Before_Enqueuing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var oldEnough = Now.AddMinutes(-(ReconcilePendingEmailDeliveriesJob.GraceMinutes + 30));
        await SeedDeliveryAsync(db, companyId, oldEnough, EmailDeliveryStatus.Pending);

        var backgroundJobClient = new RecordingBackgroundJobClient();
        var job = BuildJob(db, backgroundJobClient);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.ExecuteAsync(cts.Token));
        Assert.Empty(backgroundJobClient.CreatedJobs);
    }
}
