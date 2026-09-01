using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Notifications.Tests;

public sealed class PurgeExpiredReadNotificationsJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 3, 0, 0, TimeSpan.Zero);
    private const int RetentionDays = PurgeExpiredReadNotificationsJob.DefaultRetentionDays; // 365
    private static readonly DateTimeOffset Cutoff = Now.AddDays(-RetentionDays);

    private readonly DbContextOptions<NotificationsDbContext> _options =
        new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private NotificationsDbContext NewContext() => new(_options);

    private static Notification Read(Guid companyId, DateTimeOffset createdAt, string title = "Title", string? body = "Body")
    {
        var n = Notification.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), title, body, Guid.NewGuid(), createdAt);
        n.MarkAsRead();
        return n;
    }

    private static Notification Unread(Guid companyId, DateTimeOffset createdAt)
        => Notification.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Unread", "Body", Guid.NewGuid(), createdAt);

    private static IConfiguration Config(bool? enabled = null, int? retentionDays = null)
    {
        var data = new Dictionary<string, string?>();
        if (enabled is not null) data["Notifications:Retention:Enabled"] = enabled.Value ? "true" : "false";
        if (retentionDays is not null) data["Notifications:Retention:RetentionDays"] = retentionDays.Value.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    private PurgeExpiredReadNotificationsJob BuildJob(
        NotificationsDbContext ctx,
        IConfiguration configuration,
        FakeLegalHoldStatusReader legalHold,
        FakeAuditPublisher audit,
        FakeAdministrativeAlertWriter? alerts = null)
        => new(
            ctx,
            new FakeClock(Now.UtcDateTime),
            configuration,
            legalHold,
            audit,
            alerts ?? new FakeAdministrativeAlertWriter(),
            NullLogger<PurgeExpiredReadNotificationsJob>.Instance);

    private static NotificationsRetentionRunAuditEvent SingleAuditFor(FakeAuditPublisher audit, Guid companyId)
        => Assert.IsType<NotificationsRetentionRunAuditEvent>(
            Assert.Single(audit.Published, e => e is NotificationsRetentionRunAuditEvent ev && ev.CompanyId == companyId));

    [Fact]
    public async Task Live_Retention_Boundary_Is_Strictly_Older_Than_Cutoff()
    {
        var companyId = Guid.NewGuid();
        var atCutoff = Read(companyId, Cutoff);
        var justOlder = Read(companyId, Cutoff.AddTicks(-1));

        await using (var seed = NewContext())
        {
            seed.Notifications.AddRange(atCutoff, justOlder);
            await seed.SaveChangesAsync();
        }

        var audit = new FakeAuditPublisher();
        await using (var ctx = NewContext())
        {
            await BuildJob(ctx, Config(enabled: true), new FakeLegalHoldStatusReader(), audit).ExecuteAsync();
        }

        await using var verify = NewContext();
        Assert.True(await verify.Notifications.AnyAsync(n => n.Id == atCutoff.Id));
        Assert.False(await verify.Notifications.AnyAsync(n => n.Id == justOlder.Id));

        var evt = SingleAuditFor(audit, companyId);
        Assert.False(evt.DryRun);
        Assert.False(evt.SkippedDueToLegalHold);
        Assert.Equal(1, evt.NotificationsDeleted);
    }

    [Fact]
    public async Task DryRun_By_Default_Deletes_Nothing_But_Audits_WouldDelete_Count()
    {
        var companyId = Guid.NewGuid();
        var old1 = Read(companyId, Cutoff.AddDays(-5));
        var old2 = Read(companyId, Cutoff.AddDays(-10));

        await using (var seed = NewContext())
        {
            seed.Notifications.AddRange(old1, old2);
            await seed.SaveChangesAsync();
        }

        var audit = new FakeAuditPublisher();
        await using (var ctx = NewContext())
        {
            await BuildJob(ctx, Config(), new FakeLegalHoldStatusReader(), audit).ExecuteAsync();
        }

        await using var verify = NewContext();
        Assert.Equal(2, await verify.Notifications.CountAsync());

        var evt = SingleAuditFor(audit, companyId);
        Assert.True(evt.DryRun);
        Assert.False(evt.SkippedDueToLegalHold);
        Assert.Equal(2, evt.NotificationsDeleted);
    }

    [Fact]
    public async Task Legal_Hold_Company_Is_Skipped_And_Audited_With_Flag()
    {
        var companyId = Guid.NewGuid();
        var old = Read(companyId, Cutoff.AddDays(-30));

        await using (var seed = NewContext())
        {
            seed.Notifications.Add(old);
            await seed.SaveChangesAsync();
        }

        var audit = new FakeAuditPublisher();
        await using (var ctx = NewContext())
        {
            await BuildJob(ctx, Config(enabled: true), new FakeLegalHoldStatusReader(companyId), audit).ExecuteAsync();
        }

        await using var verify = NewContext();
        Assert.True(await verify.Notifications.AnyAsync(n => n.Id == old.Id));

        var evt = SingleAuditFor(audit, companyId);
        Assert.True(evt.SkippedDueToLegalHold);
    }

    [Fact]
    public async Task Company_Isolation_Only_NonHeld_Company_Rows_Deleted_With_Correct_Counts()
    {
        var held = Guid.NewGuid();
        var free = Guid.NewGuid();
        var heldOld1 = Read(held, Cutoff.AddDays(-1));
        var heldOld2 = Read(held, Cutoff.AddDays(-2));
        var freeOld1 = Read(free, Cutoff.AddDays(-1));
        var freeOld2 = Read(free, Cutoff.AddDays(-2));
        var freeOld3 = Read(free, Cutoff.AddDays(-3));

        await using (var seed = NewContext())
        {
            seed.Notifications.AddRange(heldOld1, heldOld2, freeOld1, freeOld2, freeOld3);
            await seed.SaveChangesAsync();
        }

        var audit = new FakeAuditPublisher();
        await using (var ctx = NewContext())
        {
            await BuildJob(ctx, Config(enabled: true), new FakeLegalHoldStatusReader(held), audit).ExecuteAsync();
        }

        await using var verify = NewContext();
        Assert.Equal(2, await verify.Notifications.CountAsync(n => n.CompanyId == held));
        Assert.Equal(0, await verify.Notifications.CountAsync(n => n.CompanyId == free));

        Assert.True(SingleAuditFor(audit, held).SkippedDueToLegalHold);
        var freeEvt = SingleAuditFor(audit, free);
        Assert.False(freeEvt.SkippedDueToLegalHold);
        Assert.Equal(3, freeEvt.NotificationsDeleted);
    }

    [Fact]
    public async Task Unread_Notifications_Are_Never_Deleted()
    {
        var companyId = Guid.NewGuid();
        var oldUnread = Unread(companyId, Cutoff.AddDays(-100));
        var oldRead = Read(companyId, Cutoff.AddDays(-100));

        await using (var seed = NewContext())
        {
            seed.Notifications.AddRange(oldUnread, oldRead);
            await seed.SaveChangesAsync();
        }

        // Dry-run
        var dryAudit = new FakeAuditPublisher();
        await using (var ctx = NewContext())
        {
            await BuildJob(ctx, Config(), new FakeLegalHoldStatusReader(), dryAudit).ExecuteAsync();
        }
        await using (var verify = NewContext())
        {
            Assert.True(await verify.Notifications.AnyAsync(n => n.Id == oldUnread.Id));
        }

        // Live
        var liveAudit = new FakeAuditPublisher();
        await using (var ctx = NewContext())
        {
            await BuildJob(ctx, Config(enabled: true), new FakeLegalHoldStatusReader(), liveAudit).ExecuteAsync();
        }
        await using (var verify = NewContext())
        {
            Assert.True(await verify.Notifications.AnyAsync(n => n.Id == oldUnread.Id));
            Assert.False(await verify.Notifications.AnyAsync(n => n.Id == oldRead.Id));
        }
    }

    [Fact]
    public async Task Audit_Event_Contains_No_Notification_Content()
    {
        var companyId = Guid.NewGuid();
        const string secretTitle = "SENSITIVE-TITLE-SECRET";
        const string secretBody = "SENSITIVE-BODY-SECRET";

        await using (var seed = NewContext())
        {
            seed.Notifications.Add(Read(companyId, Cutoff.AddDays(-10), secretTitle, secretBody));
            await seed.SaveChangesAsync();
        }

        var audit = new FakeAuditPublisher();
        await using (var ctx = NewContext())
        {
            await BuildJob(ctx, Config(enabled: true), new FakeLegalHoldStatusReader(), audit).ExecuteAsync();
        }

        HR.SharedKernel.IAuditEvent evt = SingleAuditFor(audit, companyId);
        var serialized = System.Text.Json.JsonSerializer.Serialize(new
        {
            evt.Summary,
            evt.Metadata,
            evt.Before,
            evt.After,
        });

        Assert.DoesNotContain(secretTitle, serialized);
        Assert.DoesNotContain(secretBody, serialized);
    }

    [Fact]
    public async Task Retention_Days_Override_Changes_Eligibility()
    {
        var companyId = Guid.NewGuid();
        // 100 days old: safe under 365-day window, eligible under a 90-day window.
        var n = Read(companyId, Now.AddDays(-100));

        await using (var seed = NewContext())
        {
            seed.Notifications.Add(n);
            await seed.SaveChangesAsync();
        }

        var audit = new FakeAuditPublisher();
        await using (var ctx = NewContext())
        {
            await BuildJob(ctx, Config(enabled: true, retentionDays: 90), new FakeLegalHoldStatusReader(), audit).ExecuteAsync();
        }

        await using var verify = NewContext();
        Assert.Equal(0, await verify.Notifications.CountAsync());
        Assert.Equal(1, SingleAuditFor(audit, companyId).NotificationsDeleted);
    }
}
