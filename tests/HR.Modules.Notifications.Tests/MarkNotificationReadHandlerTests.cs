using HR.Modules.Notifications;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Features.MarkNotificationRead;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
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
        var employeeId   = Guid.NewGuid();
        var notification = Notification.Create(
            Guid.NewGuid(), companyId, employeeId,
            "Task assigned", null, Guid.NewGuid(), Now);
        ctx.Notifications.Add(notification);
        await ctx.SaveChangesAsync();
        var auditPublisher = new FakeAuditPublisher();

        var result = await new MarkNotificationReadHandler(ctx, auditPublisher, new FakeClock(Now.UtcDateTime)).HandleAsync(
            new MarkNotificationReadRequest { CompanyId = companyId, NotificationId = notification.Id, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await ctx.Notifications.FindAsync(notification.Id);
        Assert.True(saved!.IsRead);

        // NOT-05: audit
        var evt = Assert.Single(auditPublisher.Published);
        var read = Assert.IsType<NotificationReadAuditEvent>(evt);
        Assert.Equal(companyId,       read.CompanyId);
        Assert.Equal(notification.Id, read.NotificationId);
        Assert.Equal(employeeId,      read.RecipientEmployeeId);
        Assert.Equal(employeeId,      ((HR.SharedKernel.IAuditEvent)read).ActorEmployeeId);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Id()
    {
        await using var ctx = BuildContext();

        var result = await new MarkNotificationReadHandler(ctx, new FakeAuditPublisher(), new FakeClock(Now.UtcDateTime)).HandleAsync(
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

        var result = await new MarkNotificationReadHandler(ctx, new FakeAuditPublisher(), new FakeClock(Now.UtcDateTime)).HandleAsync(
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
        var employeeId   = Guid.NewGuid();
        var notification = Notification.Create(
            Guid.NewGuid(), companyId, employeeId,
            "Task", null, Guid.NewGuid(), Now);
        notification.MarkAsRead();
        ctx.Notifications.Add(notification);
        await ctx.SaveChangesAsync();
        var auditPublisher = new FakeAuditPublisher();

        var result = await new MarkNotificationReadHandler(ctx, auditPublisher, new FakeClock(Now.UtcDateTime)).HandleAsync(
            new MarkNotificationReadRequest { CompanyId = companyId, NotificationId = notification.Id, EmployeeId = employeeId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await ctx.Notifications.FindAsync(notification.Id);
        Assert.True(saved!.IsRead);

        // NOT-05: no duplicate audit event on a no-op mark-read of an already-read notification.
        Assert.Empty(auditPublisher.Published);
    }

    private static NotificationsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NotificationsDbContext(options);
    }
}
