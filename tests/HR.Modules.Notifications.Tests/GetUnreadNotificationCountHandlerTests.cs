using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Features.GetUnreadNotificationCount;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

public class GetUnreadNotificationCountHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    private static Notification Make(Guid companyId, Guid employeeId, bool isRead = false)
    {
        var n = Notification.Create(
            Guid.NewGuid(), companyId, employeeId,
            "A notification", null, Guid.NewGuid(), Now);
        if (isRead) n.MarkAsRead();
        return n;
    }

    [Fact]
    public async Task Returns_Zero_When_No_Notifications()
    {
        await using var ctx = BuildContext();

        var result = await new GetUnreadNotificationCountHandler(ctx).HandleAsync(
            new GetUnreadNotificationCountRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task Counts_Only_Unread_Notifications()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyId, employeeId, isRead: false),
            Make(companyId, employeeId, isRead: false),
            Make(companyId, employeeId, isRead: true));
        await ctx.SaveChangesAsync();

        var result = await new GetUnreadNotificationCountHandler(ctx).HandleAsync(
            new GetUnreadNotificationCountRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Excludes_Other_Employees()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyId, employeeA),
            Make(companyId, employeeA),
            Make(companyId, employeeB));
        await ctx.SaveChangesAsync();

        var result = await new GetUnreadNotificationCountHandler(ctx).HandleAsync(
            new GetUnreadNotificationCountRequest { CompanyId = companyId, EmployeeId = employeeA },
            CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Excludes_Other_Companies()
    {
        await using var ctx = BuildContext();
        var employeeId = Guid.NewGuid();
        var companyA   = Guid.NewGuid();
        var companyB   = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyA, employeeId),
            Make(companyB, employeeId));
        await ctx.SaveChangesAsync();

        var result = await new GetUnreadNotificationCountHandler(ctx).HandleAsync(
            new GetUnreadNotificationCountRequest { CompanyId = companyA, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(1, result.Count);
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}
