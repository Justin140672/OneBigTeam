using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Features.MarkAllNotificationsRead;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

public class MarkAllNotificationsReadHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    private static Notification MakeNotification(Guid companyId, Guid employeeId, bool isRead = false)
    {
        var n = Notification.Create(
            Guid.NewGuid(), companyId, employeeId,
            "Task assigned", null, Guid.NewGuid(), Now);
        if (isRead) n.MarkAsRead();
        return n;
    }

    [Fact]
    public async Task Marks_All_Unread_Notifications_As_Read()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            MakeNotification(companyId, employeeId, isRead: false),
            MakeNotification(companyId, employeeId, isRead: false),
            MakeNotification(companyId, employeeId, isRead: true));
        await ctx.SaveChangesAsync();

        await new MarkAllNotificationsReadHandler(ctx).HandleAsync(
            new MarkAllNotificationsReadRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        var all = await ctx.Notifications
            .Where(n => n.CompanyId == companyId && n.EmployeeId == employeeId)
            .ToListAsync();
        Assert.All(all, n => Assert.True(n.IsRead));
    }

    [Fact]
    public async Task Does_Not_Affect_Other_Employees_Notifications()
    {
        await using var ctx = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();

        ctx.Notifications.AddRange(
            MakeNotification(companyId, employeeA),
            MakeNotification(companyId, employeeB));
        await ctx.SaveChangesAsync();

        await new MarkAllNotificationsReadHandler(ctx).HandleAsync(
            new MarkAllNotificationsReadRequest { CompanyId = companyId, EmployeeId = employeeA },
            CancellationToken.None);

        var bNotif = await ctx.Notifications.SingleAsync(n => n.EmployeeId == employeeB);
        Assert.False(bNotif.IsRead);
    }

    [Fact]
    public async Task Does_Not_Affect_Other_Companies_Notifications()
    {
        await using var ctx = BuildContext();
        var employeeId = Guid.NewGuid();
        var companyA   = Guid.NewGuid();
        var companyB   = Guid.NewGuid();

        ctx.Notifications.AddRange(
            MakeNotification(companyA, employeeId),
            MakeNotification(companyB, employeeId));
        await ctx.SaveChangesAsync();

        await new MarkAllNotificationsReadHandler(ctx).HandleAsync(
            new MarkAllNotificationsReadRequest { CompanyId = companyA, EmployeeId = employeeId },
            CancellationToken.None);

        var bNotif = await ctx.Notifications.SingleAsync(n => n.CompanyId == companyB);
        Assert.False(bNotif.IsRead);
    }

    [Fact]
    public async Task Is_Safe_When_No_Notifications_Exist()
    {
        await using var ctx = BuildContext();

        var ex = await Record.ExceptionAsync(() =>
            new MarkAllNotificationsReadHandler(ctx).HandleAsync(
                new MarkAllNotificationsReadRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
                CancellationToken.None));

        Assert.Null(ex);
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}
