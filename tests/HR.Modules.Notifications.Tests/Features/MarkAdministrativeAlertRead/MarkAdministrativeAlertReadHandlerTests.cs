using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Features.MarkAdministrativeAlertRead;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests.Features.MarkAdministrativeAlertRead;

public class MarkAdministrativeAlertReadHandlerTests
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

    [Fact]
    public async Task Marks_The_Alert_Read()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var alert = Seed(ctx, companyId);

        var result = await new MarkAdministrativeAlertReadHandler(ctx).HandleAsync(
            new MarkAdministrativeAlertReadRequest { CompanyId = companyId, AlertId = alert.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True((await ctx.AdministrativeAlerts.SingleAsync()).IsRead);
    }

    [Fact]
    public async Task Is_Idempotent_When_Already_Read()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var alert = Seed(ctx, companyId);
        alert.MarkAsRead();
        await ctx.SaveChangesAsync();

        var result = await new MarkAdministrativeAlertReadHandler(ctx).HandleAsync(
            new MarkAdministrativeAlertReadRequest { CompanyId = companyId, AlertId = alert.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True((await ctx.AdministrativeAlerts.SingleAsync()).IsRead);
    }

    [Fact]
    public async Task Returns_Failure_For_Alert_In_Another_Company()
    {
        await using var ctx = BuildContext();
        var alert = Seed(ctx, Guid.NewGuid());

        var result = await new MarkAdministrativeAlertReadHandler(ctx).HandleAsync(
            new MarkAdministrativeAlertReadRequest { CompanyId = Guid.NewGuid(), AlertId = alert.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}
