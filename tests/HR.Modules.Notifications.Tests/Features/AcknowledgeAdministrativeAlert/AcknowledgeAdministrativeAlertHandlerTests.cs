using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Features.AcknowledgeAdministrativeAlert;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests.Features.AcknowledgeAdministrativeAlert;

public class AcknowledgeAdministrativeAlertHandlerTests
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

    private static AcknowledgeAdministrativeAlertHandler Build(NotificationsDbContext ctx, out FakeAuditPublisher audit)
    {
        audit = new FakeAuditPublisher();
        return new AcknowledgeAdministrativeAlertHandler(ctx, audit, new FakeClock(FixedUtcNow));
    }

    [Fact]
    public async Task Open_Alert_Is_Acknowledged_Marked_Read_And_Audited()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var alert = Seed(ctx, companyId);
        var handler = Build(ctx, out var audit);
        var actor = Guid.NewGuid();

        var outcome = await handler.HandleAsync(new AcknowledgeAdministrativeAlertRequest
        {
            CompanyId = companyId, AlertId = alert.Id, ActorUserId = actor,
        }, CancellationToken.None);

        Assert.Equal(AcknowledgeAdministrativeAlertOutcome.Acknowledged, outcome);
        var stored = await ctx.AdministrativeAlerts.SingleAsync();
        Assert.Equal(AdministrativeAlertStatus.Acknowledged, stored.Status);
        Assert.True(stored.IsRead);
        Assert.Equal(actor, stored.AcknowledgedByUserId);
        var evt = Assert.IsType<AdministrativeAlertAcknowledgedAuditEvent>(Assert.Single(audit.Published));
        Assert.Equal(actor, evt.ActorUserId);
    }

    [Fact]
    public async Task Already_Acknowledged_Returns_Conflict_And_Does_Not_Re_Audit()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var alert = Seed(ctx, companyId);
        alert.Acknowledge(Guid.NewGuid(), FixedUtcNow);
        await ctx.SaveChangesAsync();
        var handler = Build(ctx, out var audit);

        var outcome = await handler.HandleAsync(new AcknowledgeAdministrativeAlertRequest
        {
            CompanyId = companyId, AlertId = alert.Id, ActorUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.Equal(AcknowledgeAdministrativeAlertOutcome.Conflict, outcome);
        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task Unknown_Alert_Returns_NotFound()
    {
        await using var ctx = BuildContext();
        var handler = Build(ctx, out _);

        var outcome = await handler.HandleAsync(new AcknowledgeAdministrativeAlertRequest
        {
            CompanyId = Guid.NewGuid(), AlertId = Guid.NewGuid(), ActorUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.Equal(AcknowledgeAdministrativeAlertOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task Alert_In_Another_Company_Returns_NotFound()
    {
        await using var ctx = BuildContext();
        var alert = Seed(ctx, Guid.NewGuid());
        var handler = Build(ctx, out _);

        var outcome = await handler.HandleAsync(new AcknowledgeAdministrativeAlertRequest
        {
            CompanyId = Guid.NewGuid(), AlertId = alert.Id, ActorUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.Equal(AcknowledgeAdministrativeAlertOutcome.NotFound, outcome);
    }
}
