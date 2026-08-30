using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Features.ResolveAdministrativeAlert;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests.Features.ResolveAdministrativeAlert;

public class ResolveAdministrativeAlertHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc);

    private static NotificationsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static AdministrativeAlert Seed(NotificationsDbContext ctx, Guid companyId)
    {
        var alert = AdministrativeAlert.Raise(Guid.NewGuid(), new RaiseAdministrativeAlertCommand(
            companyId, AdministrativeAlertSeverity.Warning, AdministrativeAlertCategory.Security,
            "s", "d", new DateTimeOffset(FixedUtcNow), "k", null, null, null, null), FixedUtcNow);
        ctx.AdministrativeAlerts.Add(alert);
        ctx.SaveChanges();
        return alert;
    }

    private static ResolveAdministrativeAlertHandler Build(NotificationsDbContext ctx, out FakeAuditPublisher audit)
    {
        audit = new FakeAuditPublisher();
        return new ResolveAdministrativeAlertHandler(ctx, audit, new FakeClock(FixedUtcNow));
    }

    [Fact]
    public async Task Open_Alert_Is_Resolved_And_Audited()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var alert = Seed(ctx, companyId);
        var handler = Build(ctx, out var audit);
        var actor = Guid.NewGuid();

        var outcome = await handler.HandleAsync(new ResolveAdministrativeAlertRequest
        {
            CompanyId = companyId, AlertId = alert.Id, ActorUserId = actor, ResolutionNote = "  handled  ",
        }, CancellationToken.None);

        Assert.Equal(ResolveAdministrativeAlertOutcome.Resolved, outcome);
        var stored = await ctx.AdministrativeAlerts.SingleAsync();
        Assert.Equal(AdministrativeAlertStatus.Resolved, stored.Status);
        Assert.Equal("handled", stored.ResolutionNote);
        var evt = Assert.IsType<AdministrativeAlertResolvedAuditEvent>(Assert.Single(audit.Published));
        Assert.Equal(actor, evt.ActorUserId);
    }

    [Fact]
    public async Task Acknowledged_Alert_Can_Be_Resolved_And_Audited()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var alert = Seed(ctx, companyId);
        alert.Acknowledge(Guid.NewGuid(), FixedUtcNow);
        await ctx.SaveChangesAsync();
        var handler = Build(ctx, out var audit);

        var outcome = await handler.HandleAsync(new ResolveAdministrativeAlertRequest
        {
            CompanyId = companyId, AlertId = alert.Id, ActorUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.Equal(ResolveAdministrativeAlertOutcome.Resolved, outcome);
        Assert.Single(audit.Published);
    }

    [Fact]
    public async Task Already_Resolved_Returns_Conflict_Without_Re_Auditing()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var alert = Seed(ctx, companyId);
        alert.Resolve(Guid.NewGuid(), null, FixedUtcNow);
        await ctx.SaveChangesAsync();
        var handler = Build(ctx, out var audit);

        var outcome = await handler.HandleAsync(new ResolveAdministrativeAlertRequest
        {
            CompanyId = companyId, AlertId = alert.Id, ActorUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.Equal(ResolveAdministrativeAlertOutcome.Conflict, outcome);
        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task Unknown_Or_Other_Company_Alert_Returns_NotFound()
    {
        await using var ctx = BuildContext();
        var alert = Seed(ctx, Guid.NewGuid());
        var handler = Build(ctx, out _);

        Assert.Equal(ResolveAdministrativeAlertOutcome.NotFound, await handler.HandleAsync(new ResolveAdministrativeAlertRequest
        {
            CompanyId = Guid.NewGuid(), AlertId = alert.Id, ActorUserId = Guid.NewGuid(),
        }, CancellationToken.None));
    }
}
