using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests.Jobs;

/// <summary>
/// Follow-up E: <see cref="ReconcileStalledOperationalAlertEmailDeliveriesJob"/> — the periodic
/// backstop that re-enqueues <see cref="SendOperationalAlertEmailJob"/> for deliveries saved but
/// never queued (Pending past the grace period) or interrupted mid-send (Sending with an expired
/// lease), and permanently fails a delivery once its attempt budget is spent.
/// </summary>
public class ReconcileStalledOperationalAlertEmailDeliveriesJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private const int Grace = ReconcileStalledOperationalAlertEmailDeliveriesJob.PendingGraceMinutes;

    private static NotificationsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ReconcileStalledOperationalAlertEmailDeliveriesJob BuildJob(
        NotificationsDbContext db, RecordingBackgroundJobClient backgroundJobClient) =>
        new(db, new FakeClock(Now.UtcDateTime), backgroundJobClient,
            new FakeLogger<ReconcileStalledOperationalAlertEmailDeliveriesJob>());

    private static RaiseAdministrativeAlertCommand Command(Guid companyId) =>
        new(
            companyId,
            AdministrativeAlertSeverity.Warning,
            AdministrativeAlertCategory.ReportGeneration,
            "Organisation data export completed with missing files",
            "Missing file: secret.pdf",
            Now,
            $"report:export-missing-files:{Guid.NewGuid():N}",
            "OrganisationDataExport",
            Guid.NewGuid(),
            "Investigate storage",
            null,
            4);

    /// <summary>Seeds an alert plus a delivery row and lets the caller shape the delivery's state.</summary>
    private static async Task<Guid> SeedAsync(
        NotificationsDbContext db,
        DateTimeOffset createdAt,
        Action<OperationalAlertEmailDelivery> shape)
    {
        var companyId = Guid.NewGuid();
        var alert = AdministrativeAlert.Raise(Guid.NewGuid(), Command(companyId), createdAt);
        db.AdministrativeAlerts.Add(alert);

        var delivery = OperationalAlertEmailDelivery.Create(Guid.NewGuid(), alert.Id, companyId, createdAt);
        shape(delivery);
        db.OperationalAlertEmailDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        return alert.Id;
    }

    private static void NoChange(OperationalAlertEmailDelivery _) { }

    private static List<Guid> EnqueuedAlertIds(RecordingBackgroundJobClient client) =>
        client.CreatedJobs.Select(j => (Guid)j.Args[0]!).ToList();

    [Fact]
    public async Task Pending_Older_Than_Grace_Is_ReEnqueued_Once_With_The_Alert_Id()
    {
        await using var db = BuildContext();
        var alertId = await SeedAsync(db, Now.AddMinutes(-(Grace + 5)), NoChange);

        var client = new RecordingBackgroundJobClient();
        await BuildJob(db, client).ExecuteAsync();

        var job = Assert.Single(client.CreatedJobs);
        Assert.Equal(typeof(SendOperationalAlertEmailJob), job.Type);
        Assert.Equal(alertId, (Guid)job.Args[0]!);
    }

    [Fact]
    public async Task Pending_Newer_Than_Grace_Is_Not_ReEnqueued()
    {
        await using var db = BuildContext();
        await SeedAsync(db, Now.AddMinutes(-1), NoChange);

        var client = new RecordingBackgroundJobClient();
        await BuildJob(db, client).ExecuteAsync();

        Assert.Empty(client.CreatedJobs);
    }

    [Fact]
    public async Task Pending_Exactly_At_Grace_Cutoff_Is_Not_Yet_Eligible()
    {
        // Job uses a strict "CreatedAt < now - Grace" comparison — pin the exclusive boundary.
        await using var db = BuildContext();
        await SeedAsync(db, Now.AddMinutes(-Grace), NoChange);

        var client = new RecordingBackgroundJobClient();
        await BuildJob(db, client).ExecuteAsync();

        Assert.Empty(client.CreatedJobs);
    }

    [Fact]
    public async Task Sending_With_Expired_Lease_Is_ReEnqueued()
    {
        await using var db = BuildContext();
        // Claimed a while ago; the 10 minute lease is long gone.
        var claimedAt = Now.AddMinutes(-(OperationalAlertEmailDelivery.LeaseMinutes + 30));
        var alertId = await SeedAsync(db, Now.AddMinutes(-60), d =>
            Assert.True(d.Claim(Guid.NewGuid(), claimedAt).IsSuccess));

        var client = new RecordingBackgroundJobClient();
        await BuildJob(db, client).ExecuteAsync();

        Assert.Contains(alertId, EnqueuedAlertIds(client));
    }

    [Fact]
    public async Task Sending_With_Live_Lease_Is_Not_Touched()
    {
        await using var db = BuildContext();
        var alertId = await SeedAsync(db, Now.AddMinutes(-60), d =>
            Assert.True(d.Claim(Guid.NewGuid(), Now.AddMinutes(-1)).IsSuccess)); // lease still live

        var client = new RecordingBackgroundJobClient();
        await BuildJob(db, client).ExecuteAsync();

        Assert.Empty(client.CreatedJobs);
        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Sending, stored.Status);
    }

    [Theory]
    [InlineData((int)EmailDeliveryStatus.Sent)]
    [InlineData((int)EmailDeliveryStatus.Skipped)]
    [InlineData((int)EmailDeliveryStatus.Failed)]
    public async Task Terminal_Rows_Are_Never_ReEnqueued(int statusValue)
    {
        var status = (EmailDeliveryStatus)statusValue;
        await using var db = BuildContext();
        await SeedAsync(db, Now.AddMinutes(-(Grace + 120)), d =>
        {
            switch (status)
            {
                case EmailDeliveryStatus.Sent:
                    Assert.True(d.Claim(Guid.NewGuid(), Now.AddMinutes(-120)).IsSuccess);
                    d.MarkSent(Now.AddMinutes(-119));
                    break;
                case EmailDeliveryStatus.Skipped:
                    d.MarkSkipped("No internal operations recipient configured.");
                    break;
                case EmailDeliveryStatus.Failed:
                    d.MarkFailed("Delivery abandoned after repeated failed attempts.");
                    break;
            }
        });

        var client = new RecordingBackgroundJobClient();
        await BuildJob(db, client).ExecuteAsync();

        Assert.Empty(client.CreatedJobs);
    }

    [Fact]
    public async Task Eligible_Row_With_Exhausted_Attempts_Is_Marked_Failed_And_Not_ReEnqueued()
    {
        await using var db = BuildContext();
        var t = Now.AddDays(-1);
        var alertId = await SeedAsync(db, Now.AddMinutes(-(Grace + 5)), d =>
        {
            for (var i = 0; i < OperationalAlertEmailDelivery.MaxAttempts; i++)
            {
                Assert.True(d.Claim(Guid.NewGuid(), t).IsSuccess);
                d.ReleaseForRetry(t);
                t = t.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes + 1);
            }
        });

        var client = new RecordingBackgroundJobClient();
        await BuildJob(db, client).ExecuteAsync();

        Assert.Empty(client.CreatedJobs);
        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Failed, stored.Status);
        Assert.Equal("Delivery abandoned after repeated interruptions.", stored.FailureReason);
    }

    // Ticket 3L: ownership precedence in the reconcile sweep -----------------------------------

    [Fact]
    public async Task Sending_Row_With_A_Live_Lease_Is_Not_Marked_Failed_Even_When_Attempts_Are_Exhausted()
    {
        await using var db = BuildContext();
        var t = Now.AddDays(-1);
        var alertId = await SeedAsync(db, Now.AddMinutes(-60), d =>
        {
            for (var i = 0; i < OperationalAlertEmailDelivery.MaxAttempts - 1; i++)
            {
                Assert.True(d.Claim(Guid.NewGuid(), t).IsSuccess);
                d.ReleaseForRetry(t);
                t = t.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes + 1);
            }

            // Final attempt claimed just now — lease is live, budget is spent.
            Assert.True(d.Claim(Guid.NewGuid(), Now.AddMinutes(-1)).IsSuccess);
            Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, d.AttemptCount);
        });

        var client = new RecordingBackgroundJobClient();
        await BuildJob(db, client).ExecuteAsync();

        Assert.Empty(client.CreatedJobs);
        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Sending, stored.Status);
        Assert.Null(stored.FailureReason);
    }

    [Fact]
    public async Task Sending_Row_With_A_Genuinely_Expired_Lease_And_Exhausted_Attempts_Is_Marked_Failed()
    {
        await using var db = BuildContext();
        var t = Now.AddDays(-1);
        var alertId = await SeedAsync(db, Now.AddMinutes(-(Grace + 5)), d =>
        {
            for (var i = 0; i < OperationalAlertEmailDelivery.MaxAttempts - 1; i++)
            {
                Assert.True(d.Claim(Guid.NewGuid(), t).IsSuccess);
                d.ReleaseForRetry(t);
                t = t.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes + 1);
            }

            // Final attempt claimed a day ago and never completed — lease long expired, still Sending.
            Assert.True(d.Claim(Guid.NewGuid(), t).IsSuccess);
            Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, d.AttemptCount);
            Assert.Equal(EmailDeliveryStatus.Sending, d.Status);
        });

        var client = new RecordingBackgroundJobClient();
        await BuildJob(db, client).ExecuteAsync();

        Assert.Empty(client.CreatedJobs);
        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Failed, stored.Status);
        Assert.Equal("Delivery abandoned after repeated interruptions.", stored.FailureReason);
    }

    [Fact]
    public async Task Nothing_Eligible_Does_Not_Throw_And_Enqueues_Nothing()
    {
        await using var db = BuildContext();
        var client = new RecordingBackgroundJobClient();

        await BuildJob(db, client).ExecuteAsync();

        Assert.Empty(client.CreatedJobs);
    }

    [Fact]
    public async Task Already_Cancelled_Token_Throws()
    {
        await using var db = BuildContext();
        await SeedAsync(db, Now.AddMinutes(-(Grace + 5)), NoChange);
        var client = new RecordingBackgroundJobClient();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => BuildJob(db, client).ExecuteAsync(cts.Token));
    }

    [Fact]
    public async Task Running_Twice_Is_Idempotent_No_Error()
    {
        await using var db = BuildContext();
        await SeedAsync(db, Now.AddMinutes(-(Grace + 5)), NoChange);
        var client = new RecordingBackgroundJobClient();
        var job = BuildJob(db, client);

        await job.ExecuteAsync();
        await job.ExecuteAsync(); // row is still Pending — must not throw

        // Still eligible each run, so it is re-enqueued each time (idempotent at the send job).
        Assert.All(EnqueuedAlertIds(client), id => Assert.NotEqual(Guid.Empty, id));
    }
}
