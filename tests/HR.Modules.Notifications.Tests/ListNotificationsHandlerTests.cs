using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Features.ListNotifications;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

public class ListNotificationsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    private static Notification MakeNotification(
        Guid companyId, Guid employeeId,
        string title = "A notification",
        bool isRead = false,
        DateTimeOffset? createdAt = null)
    {
        var n = Notification.Create(
            Guid.NewGuid(), companyId, employeeId,
            title, "Some body", Guid.NewGuid(),
            createdAt ?? Now);
        if (isRead) n.MarkAsRead();
        return n;
    }

    [Fact]
    public async Task Returns_Notifications_For_Employee()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherId    = Guid.NewGuid();

        ctx.Notifications.AddRange(
            MakeNotification(companyId, employeeId, "Mine A"),
            MakeNotification(companyId, employeeId, "Mine B"),
            MakeNotification(companyId, otherId,    "Someone else's"));
        await ctx.SaveChangesAsync();

        var result = await new ListNotificationsHandler(ctx).HandleAsync(
            new ListNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Contains(i.Title, new[] { "Mine A", "Mine B" }));
    }

    [Fact]
    public async Task Excludes_Notifications_From_Other_Companies()
    {
        await using var ctx = BuildContext();
        var employeeId = Guid.NewGuid();
        var companyA   = Guid.NewGuid();
        var companyB   = Guid.NewGuid();

        ctx.Notifications.AddRange(
            MakeNotification(companyA, employeeId, "Company A"),
            MakeNotification(companyB, employeeId, "Company B"));
        await ctx.SaveChangesAsync();

        var result = await new ListNotificationsHandler(ctx).HandleAsync(
            new ListNotificationsRequest { CompanyId = companyA, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Company A", result.Items[0].Title);
    }

    [Fact]
    public async Task UnreadCount_Reflects_Only_Unread_Items()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            MakeNotification(companyId, employeeId, isRead: false),
            MakeNotification(companyId, employeeId, isRead: false),
            MakeNotification(companyId, employeeId, isRead: true));
        await ctx.SaveChangesAsync();

        var result = await new ListNotificationsHandler(ctx).HandleAsync(
            new ListNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(2, result.UnreadCount);
    }

    [Fact]
    public async Task Returns_Empty_When_No_Notifications()
    {
        await using var ctx = BuildContext();

        var result = await new ListNotificationsHandler(ctx).HandleAsync(
            new ListNotificationsRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.UnreadCount);
    }

    [Fact]
    public async Task Orders_By_CreatedAt_Descending()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            MakeNotification(companyId, employeeId, "Oldest", createdAt: Now.AddHours(-3)),
            MakeNotification(companyId, employeeId, "Newest", createdAt: Now),
            MakeNotification(companyId, employeeId, "Middle", createdAt: Now.AddHours(-1)));
        await ctx.SaveChangesAsync();

        var result = await new ListNotificationsHandler(ctx).HandleAsync(
            new ListNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal("Newest", result.Items[0].Title);
        Assert.Equal("Middle", result.Items[1].Title);
        Assert.Equal("Oldest", result.Items[2].Title);
    }

    [Fact]
    public async Task Limits_To_50_Items()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        ctx.Notifications.AddRange(
            Enumerable.Range(0, 60).Select(i =>
                MakeNotification(companyId, employeeId, $"Notif {i}")));
        await ctx.SaveChangesAsync();

        var result = await new ListNotificationsHandler(ctx).HandleAsync(
            new ListNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.Equal(50, result.Items.Count);
    }

    [Fact]
    public async Task Maps_All_Fields_Correctly()
    {
        await using var ctx = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var sourceId   = Guid.NewGuid();

        var n = Notification.Create(
            Guid.NewGuid(), companyId, employeeId,
            "Task title", "Task body", sourceId, Now);
        ctx.Notifications.Add(n);
        await ctx.SaveChangesAsync();

        var result = await new ListNotificationsHandler(ctx).HandleAsync(
            new ListNotificationsRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(n.Id, item.Id);
        Assert.Equal("Task title", item.Title);
        Assert.Equal("Task body", item.Body);
        Assert.False(item.IsRead);
        Assert.Equal(sourceId, item.SourceEntityId);
        Assert.Equal(Now, item.CreatedAt);
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}
