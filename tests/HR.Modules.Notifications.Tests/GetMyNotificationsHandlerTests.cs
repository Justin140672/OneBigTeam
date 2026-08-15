using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Features.GetMyNotifications;
using HR.Modules.Notifications.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

public class GetMyNotificationsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    private static Notification Make(
        Guid companyId, Guid employeeId,
        string title = "A notification",
        bool isRead = false,
        NotificationPriority priority = NotificationPriority.Normal,
        DateTimeOffset? createdAt = null)
    {
        var n = Notification.Create(
            Guid.NewGuid(), companyId, employeeId,
            title, null, Guid.NewGuid(),
            createdAt ?? Now,
            priority: priority);
        if (isRead) n.MarkAsRead();
        return n;
    }

    [Fact]
    public async Task Returns_Only_Current_Employee_Notifications()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherId    = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyId, employeeId, "Mine"),
            Make(companyId, otherId,    "Not mine"));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Mine", item.Title);
    }

    [Fact]
    public async Task UnreadCount_Reflects_Only_Unread_Items()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyId, employeeId, isRead: false),
            Make(companyId, employeeId, isRead: false),
            Make(companyId, employeeId, isRead: true));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(2, result.UnreadCount);
    }

    [Fact]
    public async Task Orders_By_CreatedAt_Descending()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Make(companyId, employeeId, "Oldest", createdAt: Now.AddHours(-2)),
            Make(companyId, employeeId, "Newest", createdAt: Now),
            Make(companyId, employeeId, "Middle", createdAt: Now.AddHours(-1)));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal("Newest", result.Items[0].Title);
        Assert.Equal("Middle", result.Items[1].Title);
        Assert.Equal("Oldest", result.Items[2].Title);
    }

    [Fact]
    public async Task Maps_Priority_To_String()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.Add(Make(companyId, employeeId, priority: NotificationPriority.Urgent));
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal("Urgent", Assert.Single(result.Items).Priority);
    }

    [Fact]
    public async Task Limits_Results_To_Fifty_Most_Recent_Items()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var notifications = Enumerable.Range(0, 51)
            .Select(i => Make(companyId, employeeId, $"Item {i}", createdAt: Now.AddMinutes(-i)))
            .ToArray();
        ctx.Notifications.AddRange(notifications);
        await ctx.SaveChangesAsync();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(50, result.Items.Count);
        Assert.Equal("Item 0", result.Items[0].Title);
        Assert.DoesNotContain(result.Items, i => i.Title == "Item 50");
    }

    [Fact]
    public async Task Returns_Empty_When_No_Notifications()
    {
        await using var ctx = BuildContext();

        var result = await new GetMyNotificationsHandler(ctx).HandleAsync(
            new GetMyNotificationsRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.UnreadCount);
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}
