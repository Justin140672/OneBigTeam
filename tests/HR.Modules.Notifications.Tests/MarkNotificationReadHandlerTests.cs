using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Features.MarkNotificationRead;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

public class MarkNotificationReadHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Marks_Notification_As_Read()
    {
        await using var ctx = BuildContext();
        var companyId    = Guid.NewGuid();
        var notification = Notification.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task assigned", null, Guid.NewGuid(), Now);
        ctx.Notifications.Add(notification);
        await ctx.SaveChangesAsync();

        var result = await new MarkNotificationReadHandler(ctx).HandleAsync(
            new MarkNotificationReadRequest { CompanyId = companyId, NotificationId = notification.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await ctx.Notifications.FindAsync(notification.Id);
        Assert.True(saved!.IsRead);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Id()
    {
        await using var ctx = BuildContext();

        var result = await new MarkNotificationReadHandler(ctx).HandleAsync(
            new MarkNotificationReadRequest { CompanyId = Guid.NewGuid(), NotificationId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Returns_NotFound_When_CompanyId_Does_Not_Match()
    {
        await using var ctx = BuildContext();
        var notification = Notification.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Task", null, Guid.NewGuid(), Now);
        ctx.Notifications.Add(notification);
        await ctx.SaveChangesAsync();

        var result = await new MarkNotificationReadHandler(ctx).HandleAsync(
            new MarkNotificationReadRequest { CompanyId = Guid.NewGuid(), NotificationId = notification.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Is_Idempotent_When_Already_Read()
    {
        await using var ctx = BuildContext();
        var companyId    = Guid.NewGuid();
        var notification = Notification.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, Guid.NewGuid(), Now);
        notification.MarkAsRead();
        ctx.Notifications.Add(notification);
        await ctx.SaveChangesAsync();

        var result = await new MarkNotificationReadHandler(ctx).HandleAsync(
            new MarkNotificationReadRequest { CompanyId = companyId, NotificationId = notification.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await ctx.Notifications.FindAsync(notification.Id);
        Assert.True(saved!.IsRead);
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}
