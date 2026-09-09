using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Notifications;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HR.Integration.Tests;

/// <summary>
/// Follow-up E regression coverage that needs a real PostgreSQL backend — only Postgres enforces the
/// (alert_id) unique index and surfaces the <c>xmin</c> optimistic-concurrency token that
/// <see cref="OperationalAlertEmailDelivery.Claim"/> relies on to make delivery exclusive. The EF Core
/// InMemory provider used by the module unit tests (SendOperationalAlertEmailJobTests,
/// ReconcileStalledOperationalAlertEmailDeliveriesJobTests) cannot raise
/// <see cref="DbUpdateConcurrencyException"/>, so the "two workers race the same alert" and
/// "save-after-send loses the row" paths are proven here.
///
/// <para>The integration <see cref="FakeEmailSender"/> cannot simulate a provider failure, so the
/// "fails transiently then succeeds" sequence (regression #4) lives in the module unit test
/// SendOperationalAlertEmailJobTests.Send_Fails_Once_Then_Succeeds_On_Retry_Delivers_Exactly_One_Email.</para>
/// </summary>
[Collection("Integration")]
public class OperationalAlertEmailDeliveryRecoveryTests
{
    private readonly ApiWebApplicationFactory _factory;

    public OperationalAlertEmailDeliveryRecoveryTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private const string Recipient = "ops@internal.example";
    private const string AdminBaseUrl = "https://admin.example";

    private FakeBackgroundJobClient Jobs =>
        (FakeBackgroundJobClient)_factory.Services.GetRequiredService<IBackgroundJobClient>();

    private static RaiseAdministrativeAlertCommand ReportCommand(Guid companyId) =>
        new(
            companyId,
            AdministrativeAlertSeverity.Warning,
            AdministrativeAlertCategory.ReportGeneration,
            "Organisation data export completed with missing files",
            "Missing file: secret-payslip.pdf",
            DateTimeOffset.UtcNow,
            $"rpt-{Guid.NewGuid():N}",
            "OrganisationDataExport",
            Guid.NewGuid(),
            "Investigate storage",
            null,
            2,
            AdministrativeAlertReason.MissingDocumentExport);

    /// <summary>Seeds an alert + delivery row directly (bypassing the writer's enqueue path).</summary>
    private async Task<Guid> SeedAsync(
        Guid companyId, DateTimeOffset createdAt, Action<OperationalAlertEmailDelivery>? shape = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var alert = AdministrativeAlert.Raise(Guid.NewGuid(), ReportCommand(companyId), createdAt);
        db.AdministrativeAlerts.Add(alert);

        var delivery = OperationalAlertEmailDelivery.Create(Guid.NewGuid(), alert.Id, companyId, createdAt);
        shape?.Invoke(delivery);
        db.OperationalAlertEmailDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        return alert.Id;
    }

    private SendOperationalAlertEmailJob BuildSendJob(IServiceProvider sp) =>
        ActivatorUtilities.CreateInstance<SendOperationalAlertEmailJob>(
            sp,
            Options.Create(new OperationalAlertEmailOptions
            {
                InternalRecipientEmail = Recipient,
                AdminAppBaseUrl = AdminBaseUrl,
            }));

    private int EmailCountFor(Guid companyId) =>
        _factory.EmailSender.Sent.Count(e => e.HtmlBody.Contains(companyId.ToString()));

    private async Task<OperationalAlertEmailDelivery> LoadAsync(Guid alertId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await db.OperationalAlertEmailDeliveries.AsNoTracking().SingleAsync(d => d.AlertId == alertId);
    }

    private async Task RunSendAsync(Guid alertId)
    {
        using var scope = _factory.Services.CreateScope();
        await BuildSendJob(scope.ServiceProvider).SendAsync(alertId);
    }

    private async Task RunReconcileAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<ReconcileStalledOperationalAlertEmailDeliveriesJob>(scope.ServiceProvider);
        await job.ExecuteAsync(CancellationToken.None);
    }

    private List<Guid> ReconcileEnqueuedAlertIds() =>
        Jobs.CreatedJobs
            .Where(j => j.Type == typeof(SendOperationalAlertEmailJob))
            .Select(j => j.Args.ElementAtOrDefault(0))
            .OfType<Guid>()
            .ToList();

    // #1 — duplicate jobs after the first claim committed -----------------------------------------

    [Fact]
    public async Task Second_SendAsync_After_First_Has_Committed_Is_A_No_Op()
    {
        var companyId = Guid.NewGuid();
        var alertId = await SeedAsync(companyId, DateTimeOffset.UtcNow);

        await RunSendAsync(alertId);
        await RunSendAsync(alertId); // delivery is now terminal (Sent) — must not send again

        Assert.Equal(1, EmailCountFor(companyId));
        var stored = await LoadAsync(alertId);
        Assert.Equal(EmailDeliveryStatus.Sent, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Null(stored.LeaseExpiresAt);
    }

    // #2 — delivery row saved but the enqueue never happened --------------------------------------

    [Fact]
    public async Task Reconcile_ReEnqueues_A_Pending_Row_That_Was_Never_Queued()
    {
        var companyId = Guid.NewGuid();
        var stale = DateTimeOffset.UtcNow.AddMinutes(-(ReconcileStalledOperationalAlertEmailDeliveriesJob.PendingGraceMinutes + 10));
        var alertId = await SeedAsync(companyId, stale); // no enqueue performed

        await RunReconcileAsync();

        Assert.Contains(alertId, ReconcileEnqueuedAlertIds());
    }

    // #3 — sender crashed before contacting Postmark: row stuck Sending with an expired lease ------

    [Fact]
    public async Task Reconcile_ReEnqueues_A_Sending_Row_With_An_Expired_Lease()
    {
        var companyId = Guid.NewGuid();
        var claimedAt = DateTimeOffset.UtcNow.AddMinutes(-(OperationalAlertEmailDelivery.LeaseMinutes + 30));
        var alertId = await SeedAsync(companyId, DateTimeOffset.UtcNow.AddHours(-1), d =>
            Assert.True(d.Claim(Guid.NewGuid(), claimedAt).IsSuccess));

        await RunReconcileAsync();

        Assert.Contains(alertId, ReconcileEnqueuedAlertIds());
        var stored = await LoadAsync(alertId);
        Assert.Equal(EmailDeliveryStatus.Sending, stored.Status); // reconcile re-enqueues, does not mutate
    }

    // #5 — send succeeds but persisting Sent fails: at-least-once, no lost alert -------------------

    [Fact]
    public async Task Send_Succeeds_But_Row_ReClaimed_Before_Sent_Persisted_Is_Swallowed_And_Resendable()
    {
        // Force the race: a first worker loads the row and claims it, but before it can persist the
        // Sent status a second worker (simulated here by a direct DB update from another context that
        // bumps xmin) mutates the row. The first worker's final SaveChangesAsync then throws
        // DbUpdateConcurrencyException, which the job must swallow (the email is already out).
        var companyId = Guid.NewGuid();
        var alertId = await SeedAsync(companyId, DateTimeOffset.UtcNow);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var job = BuildSendJob(scope.ServiceProvider);

        var delivery = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.True(delivery.Claim(Guid.NewGuid(), DateTimeOffset.UtcNow).IsSuccess);
        await db.SaveChangesAsync();

        // Another worker re-claims (bumps xmin) while "we" are sending.
        using (var otherScope = _factory.Services.CreateScope())
        {
            var otherDb = otherScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var other = await otherDb.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
            other.ReleaseForRetry(DateTimeOffset.UtcNow);
            await otherDb.SaveChangesAsync();
        }

        // Our worker now marks Sent and tries to persist -> concurrency exception, which the domain
        // method + caller path here reproduces via a direct save. The production job swallows the
        // equivalent DbUpdateConcurrencyException on its own final save; assert our row is not lost.
        delivery.MarkSent(DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());

        // Recovery: a later send can still deliver (duplicate email is acceptable — at-least-once).
        await RunSendAsync(alertId);

        Assert.True(EmailCountFor(companyId) >= 1);
        var finalState = await LoadAsync(alertId);
        Assert.Equal(EmailDeliveryStatus.Sent, finalState.Status);
    }

    // #6 — retry limit exhausted: no endless loop ------------------------------------------------

    [Fact]
    public async Task Exhausted_Delivery_Is_Failed_By_Send_And_Never_ReEnqueued_By_Reconcile()
    {
        var companyId = Guid.NewGuid();
        var stale = DateTimeOffset.UtcNow.AddMinutes(-(ReconcileStalledOperationalAlertEmailDeliveriesJob.PendingGraceMinutes + 10));
        var t = DateTimeOffset.UtcNow.AddDays(-1);
        var alertId = await SeedAsync(companyId, stale, d =>
        {
            for (var i = 0; i < OperationalAlertEmailDelivery.MaxAttempts; i++)
            {
                Assert.True(d.Claim(Guid.NewGuid(), t).IsSuccess);
                d.ReleaseForRetry(t);
                t = t.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes + 1);
            }
        });

        await RunSendAsync(alertId);

        var afterSend = await LoadAsync(alertId);
        Assert.Equal(EmailDeliveryStatus.Failed, afterSend.Status);
        Assert.Equal("Delivery abandoned after repeated failed attempts.", afterSend.FailureReason);
        Assert.Equal(0, EmailCountFor(companyId));

        await RunReconcileAsync();
        await RunReconcileAsync();

        Assert.DoesNotContain(alertId, ReconcileEnqueuedAlertIds());
    }

    // #7 — two workers race the same alert id: exactly one email ---------------------------------

    [Fact]
    public async Task Two_Parallel_SendAsync_For_The_Same_Alert_Deliver_Exactly_One_Email()
    {
        var companyId = Guid.NewGuid();
        var alertId = await SeedAsync(companyId, DateTimeOffset.UtcNow);

        await Task.WhenAll(TrySendAsync(alertId), TrySendAsync(alertId));

        Assert.Equal(1, EmailCountFor(companyId));
        var stored = await LoadAsync(alertId);
        Assert.Equal(EmailDeliveryStatus.Sent, stored.Status);
        Assert.Equal(1, stored.AttemptCount);

        async Task TrySendAsync(Guid id)
        {
            try
            {
                using var scope = _factory.Services.CreateScope();
                await BuildSendJob(scope.ServiceProvider).SendAsync(id);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Acceptable: the losing racer may surface the concurrency exception rather than
                // swallowing it depending on exactly where the two saves interleave. The invariant
                // under test is "exactly one email", asserted above.
            }
        }
    }

    // =========================================================================================
    // Ticket 3L: ownership precedence beats the attempt budget. A duplicate send execution or the
    // reconcile sweep must never fail / re-claim / touch a row while the real sender still holds a
    // LIVE lease on its final attempt — doing so raced MarkSent and silently dropped a delivered
    // email. These use two separate DbContexts per database (real Npgsql + xmin) like the tests
    // above, seed rows directly and clean them up in a finally block.
    // =========================================================================================

    /// <summary>
    /// Seeds a row paused mid-final-attempt: MaxAttempts - 1 interrupted attempts already burned, then
    /// a fresh claim taken "now" so the row is <see cref="EmailDeliveryStatus.Sending"/> with
    /// AttemptCount == MaxAttempts and a live ownership lease.
    /// </summary>
    private async Task<Guid> SeedFinalAttemptInFlightAsync(Guid companyId)
    {
        var alertId = await SeedAsync(companyId, DateTimeOffset.UtcNow.AddHours(-1), d =>
        {
            var t = DateTimeOffset.UtcNow.AddDays(-1);
            for (var i = 0; i < OperationalAlertEmailDelivery.MaxAttempts - 1; i++)
            {
                Assert.True(d.Claim(Guid.NewGuid(), t).IsSuccess);
                d.ReleaseForRetry(t);
                t = t.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes + 1);
            }

            Assert.True(d.Claim(Guid.NewGuid(), DateTimeOffset.UtcNow).IsSuccess); // live final lease
            Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, d.AttemptCount);
        });
        return alertId;
    }

    private async Task DeleteAsync(Guid alertId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var delivery = await db.OperationalAlertEmailDeliveries.SingleOrDefaultAsync(d => d.AlertId == alertId);
        if (delivery is not null)
            db.OperationalAlertEmailDeliveries.Remove(delivery);
        var alert = await db.AdministrativeAlerts.SingleOrDefaultAsync(a => a.Id == alertId);
        if (alert is not null)
            db.AdministrativeAlerts.Remove(alert);
        await db.SaveChangesAsync();
    }

    // #1-#3 — duplicate job against a live final attempt, then the real owner finishes ------------

    [Fact]
    public async Task Duplicate_Job_Against_A_Live_Final_Attempt_Is_A_No_Op_Then_The_Owner_Completes_With_One_Send()
    {
        var companyId = Guid.NewGuid();
        var alertId = await SeedFinalAttemptInFlightAsync(companyId);
        try
        {
            var before = await LoadAsync(alertId);

            // A duplicate SendOperationalAlertEmailJob runs in its own context/scope against the row.
            await RunSendAsync(alertId);

            Assert.Equal(0, EmailCountFor(companyId));
            var afterDuplicate = await LoadAsync(alertId);
            Assert.Equal(EmailDeliveryStatus.Sending, afterDuplicate.Status);
            Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, afterDuplicate.AttemptCount);
            Assert.Null(afterDuplicate.FailureReason);
            Assert.Null(afterDuplicate.SentAt);
            Assert.Equal(before.LeaseOwnerToken, afterDuplicate.LeaseOwnerToken);
            Assert.Equal(before.LeaseExpiresAt, afterDuplicate.LeaseExpiresAt);

            // The original owner (still holding the lease) now completes its send. Production does this
            // inline inside the same SendAsync call; here we drive the same effect against a fresh
            // context to prove the row was left in a completable state.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var owned = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
            await emailSender.SendAsync(Recipient, "[Operations] alert", $"<p>company {companyId}</p>", CancellationToken.None);
            owned.MarkSent(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();

            Assert.Equal(1, EmailCountFor(companyId));
            var final = await LoadAsync(alertId);
            Assert.Equal(EmailDeliveryStatus.Sent, final.Status);
            Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, final.AttemptCount);
            Assert.Null(final.LeaseExpiresAt);
        }
        finally
        {
            await DeleteAsync(alertId);
        }
    }

    // #4 — same, but the owner's provider call fails: the owner fails the row itself ---------------

    [Fact]
    public async Task Owner_Whose_Final_Send_Fails_Records_The_Failure_Itself_And_A_Duplicate_Never_Preempted_It()
    {
        var companyId = Guid.NewGuid();
        var alertId = await SeedFinalAttemptInFlightAsync(companyId);
        try
        {
            await RunSendAsync(alertId); // duplicate: no-op while the lease is live
            Assert.Equal(0, EmailCountFor(companyId));
            Assert.Equal(EmailDeliveryStatus.Sending, (await LoadAsync(alertId)).Status);

            // The integration FakeEmailSender cannot simulate a provider failure, so drive the owner's
            // own final-attempt failure outcome (MarkFailed with a sanitized reason) directly — the
            // point under test is that the duplicate above did not retire the row first.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var owned = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
            Assert.False(owned.HasAttemptsRemaining);
            owned.MarkFailed("Email provider error.");
            await db.SaveChangesAsync();

            var final = await LoadAsync(alertId);
            Assert.Equal(EmailDeliveryStatus.Failed, final.Status);
            Assert.Equal("Email provider error.", final.FailureReason);
            Assert.Equal(0, EmailCountFor(companyId));
        }
        finally
        {
            await DeleteAsync(alertId);
        }
    }

    // #5 — interrupted final attempt, lease then expires, reconcile retires it, no extra send ------

    [Fact]
    public async Task Reconcile_Fails_An_Interrupted_Final_Attempt_Once_Its_Lease_Has_Expired_Without_Sending()
    {
        var companyId = Guid.NewGuid();
        // Final attempt claimed long ago and never completed — Sending, AttemptCount == MaxAttempts,
        // lease already expired.
        var alertId = await SeedAsync(companyId, DateTimeOffset.UtcNow.AddHours(-2), d =>
        {
            var t = DateTimeOffset.UtcNow.AddDays(-1);
            for (var i = 0; i < OperationalAlertEmailDelivery.MaxAttempts; i++)
            {
                Assert.True(d.Claim(Guid.NewGuid(), t).IsSuccess);
                if (i < OperationalAlertEmailDelivery.MaxAttempts - 1)
                {
                    d.ReleaseForRetry(t);
                    t = t.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes + 1);
                }
            }

            Assert.Equal(EmailDeliveryStatus.Sending, d.Status);
            Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, d.AttemptCount);
        });
        try
        {
            await RunReconcileAsync();

            Assert.DoesNotContain(alertId, ReconcileEnqueuedAlertIds());
            Assert.Equal(0, EmailCountFor(companyId));
            var final = await LoadAsync(alertId);
            Assert.Equal(EmailDeliveryStatus.Failed, final.Status);
            Assert.Equal("Delivery abandoned after repeated interruptions.", final.FailureReason);
        }
        finally
        {
            await DeleteAsync(alertId);
        }
    }

    // #6 — duplicate send job racing the reconcile sweep against a live final attempt -------------

    [Fact]
    public async Task Duplicate_Send_And_Reconcile_Racing_A_Live_Final_Attempt_Respect_Ownership()
    {
        var companyId = Guid.NewGuid();
        var alertId = await SeedFinalAttemptInFlightAsync(companyId);
        try
        {
            await Task.WhenAll(RunSendSwallowingConcurrencyAsync(alertId), RunReconcileAsync());

            Assert.Equal(0, EmailCountFor(companyId));
            Assert.DoesNotContain(alertId, ReconcileEnqueuedAlertIds());
            var final = await LoadAsync(alertId);
            Assert.Equal(EmailDeliveryStatus.Sending, final.Status);
            Assert.Null(final.FailureReason);
            Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, final.AttemptCount);
        }
        finally
        {
            await DeleteAsync(alertId);
        }
    }

    private async Task RunSendSwallowingConcurrencyAsync(Guid alertId)
    {
        try
        {
            await RunSendAsync(alertId);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A losing racer may surface rather than swallow depending on save interleaving.
        }
    }

    // #7 — unchanged behaviour: bounded retry for earlier attempts, no-op for terminal rows -------

    [Fact]
    public async Task Reconcile_Still_ReEnqueues_An_Interrupted_Earlier_Attempt_Without_Failing_It()
    {
        var companyId = Guid.NewGuid();
        // One interrupted attempt (AttemptCount == 1 < MaxAttempts), lease expired.
        var claimedAt = DateTimeOffset.UtcNow.AddMinutes(-(OperationalAlertEmailDelivery.LeaseMinutes + 30));
        var alertId = await SeedAsync(companyId, DateTimeOffset.UtcNow.AddHours(-1), d =>
            Assert.True(d.Claim(Guid.NewGuid(), claimedAt).IsSuccess));
        try
        {
            await RunReconcileAsync();

            Assert.Contains(alertId, ReconcileEnqueuedAlertIds());
            var stored = await LoadAsync(alertId);
            Assert.Equal(EmailDeliveryStatus.Sending, stored.Status); // re-enqueued, not mutated / failed
            Assert.Null(stored.FailureReason);
        }
        finally
        {
            await DeleteAsync(alertId);
        }
    }

    [Theory]
    [InlineData((int)EmailDeliveryStatus.Sent)]
    [InlineData((int)EmailDeliveryStatus.Failed)]
    [InlineData((int)EmailDeliveryStatus.Skipped)]
    public async Task Terminal_Rows_Are_Untouched_By_Both_Jobs(int statusValue)
    {
        var status = (EmailDeliveryStatus)statusValue;
        var companyId = Guid.NewGuid();
        var stale = DateTimeOffset.UtcNow.AddMinutes(-(ReconcileStalledOperationalAlertEmailDeliveriesJob.PendingGraceMinutes + 30));
        var alertId = await SeedAsync(companyId, stale, d =>
        {
            switch (status)
            {
                case EmailDeliveryStatus.Sent:
                    Assert.True(d.Claim(Guid.NewGuid(), stale).IsSuccess);
                    d.MarkSent(stale.AddMinutes(1));
                    break;
                case EmailDeliveryStatus.Failed:
                    d.MarkFailed("Delivery abandoned after repeated failed attempts.");
                    break;
                case EmailDeliveryStatus.Skipped:
                    d.MarkSkipped("No internal operations recipient configured.");
                    break;
            }
        });
        try
        {
            await RunSendAsync(alertId);
            await RunReconcileAsync();

            Assert.Equal(0, EmailCountFor(companyId));
            Assert.DoesNotContain(alertId, ReconcileEnqueuedAlertIds());
            var stored = await LoadAsync(alertId);
            Assert.Equal(status, stored.Status);
        }
        finally
        {
            await DeleteAsync(alertId);
        }
    }
}
