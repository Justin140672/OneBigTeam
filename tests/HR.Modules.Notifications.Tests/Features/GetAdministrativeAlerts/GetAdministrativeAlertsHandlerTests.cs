using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Features.GetAdministrativeAlerts;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests.Features.GetAdministrativeAlerts;

public class GetAdministrativeAlertsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static NotificationsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static AdministrativeAlert Make(
        Guid companyId,
        AdministrativeAlertSeverity severity = AdministrativeAlertSeverity.Warning,
        AdministrativeAlertCategory category = AdministrativeAlertCategory.IntegrationDelivery,
        DateTimeOffset? occurredAt = null,
        string dedupKey = "d",
        bool read = false,
        AdministrativeAlertStatus status = AdministrativeAlertStatus.Open)
    {
        var alert = AdministrativeAlert.Raise(Guid.NewGuid(), new RaiseAdministrativeAlertCommand(
            companyId, severity, category, "summary", "detail", occurredAt ?? Now,
            dedupKey, null, null, null, null), Now);
        if (read) alert.MarkAsRead();
        if (status == AdministrativeAlertStatus.Acknowledged) alert.Acknowledge(Guid.NewGuid(), Now);
        if (status == AdministrativeAlertStatus.Resolved) alert.Resolve(Guid.NewGuid(), null, Now);
        return alert;
    }

    private static GetAdministrativeAlertsResponse Handle(NotificationsDbContext ctx, GetAdministrativeAlertsRequest request) =>
        new GetAdministrativeAlertsHandler(ctx).HandleAsync(request, CancellationToken.None).GetAwaiter().GetResult();

    [Fact]
    public async Task Returns_Only_Rows_For_The_Route_Company()
    {
        await using var ctx = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        ctx.AdministrativeAlerts.AddRange(
            Make(companyA, dedupKey: "a"),
            Make(companyB, dedupKey: "b"));
        await ctx.SaveChangesAsync();

        var result = Handle(ctx, new GetAdministrativeAlertsRequest { CompanyId = companyA });

        var item = Assert.Single(result.Items);
        Assert.Equal("summary", item.Summary);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task Filters_By_Severity_Category_Status_And_IsRead()
    {
        await using var ctx = BuildContext();
        var c = Guid.NewGuid();
        ctx.AdministrativeAlerts.AddRange(
            Make(c, severity: AdministrativeAlertSeverity.Critical, dedupKey: "1"),
            Make(c, severity: AdministrativeAlertSeverity.Info, dedupKey: "2"),
            Make(c, category: AdministrativeAlertCategory.Security, dedupKey: "3"),
            Make(c, read: true, dedupKey: "4"),
            Make(c, status: AdministrativeAlertStatus.Resolved, dedupKey: "5"));
        await ctx.SaveChangesAsync();

        Assert.Equal(AdministrativeAlertSeverity.Critical.ToString(),
            Assert.Single(Handle(ctx, new GetAdministrativeAlertsRequest { CompanyId = c, Severity = AdministrativeAlertSeverity.Critical }).Items).Severity);

        Assert.Equal(AdministrativeAlertCategory.Security.ToString(),
            Assert.Single(Handle(ctx, new GetAdministrativeAlertsRequest { CompanyId = c, Category = AdministrativeAlertCategory.Security }).Items).Category);

        Assert.Equal("Resolved",
            Assert.Single(Handle(ctx, new GetAdministrativeAlertsRequest { CompanyId = c, Status = AdministrativeAlertStatus.Resolved }).Items).Status);

        Assert.All(Handle(ctx, new GetAdministrativeAlertsRequest { CompanyId = c, IsRead = true }).Items, i => Assert.True(i.IsRead));
        Assert.All(Handle(ctx, new GetAdministrativeAlertsRequest { CompanyId = c, IsRead = false }).Items, i => Assert.False(i.IsRead));
    }

    [Fact]
    public async Task Filters_By_LastOccurredAt_Range_Inclusive_Boundaries()
    {
        await using var ctx = BuildContext();
        var c = Guid.NewGuid();
        var from = Now.AddDays(-2);
        var to = Now;
        ctx.AdministrativeAlerts.AddRange(
            Make(c, occurredAt: from.AddSeconds(-1), dedupKey: "before"),
            Make(c, occurredAt: from, dedupKey: "atfrom"),
            Make(c, occurredAt: to, dedupKey: "atto"),
            Make(c, occurredAt: to.AddSeconds(1), dedupKey: "after"));
        await ctx.SaveChangesAsync();

        var result = Handle(ctx, new GetAdministrativeAlertsRequest { CompanyId = c, OccurredFrom = from, OccurredTo = to });

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task Orders_Open_First_Then_Severity_Desc_Then_LastOccurredAt_Desc()
    {
        await using var ctx = BuildContext();
        var c = Guid.NewGuid();
        var ackHighRecent = Make(c, severity: AdministrativeAlertSeverity.Critical, occurredAt: Now, dedupKey: "ack", status: AdministrativeAlertStatus.Acknowledged);
        var openLow = Make(c, severity: AdministrativeAlertSeverity.Info, occurredAt: Now.AddDays(-1), dedupKey: "openlow");
        var openHighOld = Make(c, severity: AdministrativeAlertSeverity.Critical, occurredAt: Now.AddDays(-5), dedupKey: "openhighold");
        var openHighNew = Make(c, severity: AdministrativeAlertSeverity.Critical, occurredAt: Now, dedupKey: "openhighnew");
        ctx.AdministrativeAlerts.AddRange(ackHighRecent, openLow, openHighOld, openHighNew);
        await ctx.SaveChangesAsync();

        var items = Handle(ctx, new GetAdministrativeAlertsRequest { CompanyId = c }).Items;

        Assert.Equal(openHighNew.Id, items[0].Id);
        Assert.Equal(openHighOld.Id, items[1].Id);
        Assert.Equal(openLow.Id, items[2].Id);
        Assert.Equal(ackHighRecent.Id, items[3].Id);
    }

    [Fact]
    public async Task Paginates_And_Reports_TotalPages()
    {
        await using var ctx = BuildContext();
        var c = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            ctx.AdministrativeAlerts.Add(Make(c, occurredAt: Now.AddMinutes(-i), dedupKey: $"d{i}"));
        await ctx.SaveChangesAsync();

        var page2 = Handle(ctx, new GetAdministrativeAlertsRequest { CompanyId = c, PageNumber = 2, PageSize = 2 });

        Assert.Equal(2, page2.Items.Count);
        Assert.Equal(5, page2.TotalCount);
        Assert.Equal(3, page2.TotalPages);
    }

    [Fact]
    public async Task UnreadCount_Ignores_Filters_And_Paging_And_Excludes_Resolved()
    {
        await using var ctx = BuildContext();
        var c = Guid.NewGuid();
        ctx.AdministrativeAlerts.AddRange(
            Make(c, dedupKey: "u1"),
            Make(c, dedupKey: "u2"),
            Make(c, read: true, dedupKey: "r1"),
            Make(c, status: AdministrativeAlertStatus.Resolved, dedupKey: "res"));
        await ctx.SaveChangesAsync();

        var result = Handle(ctx, new GetAdministrativeAlertsRequest
        {
            CompanyId = c,
            Severity = AdministrativeAlertSeverity.Critical,
            PageSize = 1,
            PageNumber = 1,
        });

        Assert.Equal(2, result.UnreadCount);
    }

    [Fact]
    public async Task Empty_State_Returns_Zeroes()
    {
        await using var ctx = BuildContext();

        var result = Handle(ctx, new GetAdministrativeAlertsRequest { CompanyId = Guid.NewGuid() });

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.UnreadCount);
        Assert.Equal(0, result.TotalPages);
    }
}
