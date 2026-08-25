using HR.Modules.Notifications;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Features.MarkAllNotificationsRead;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
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

        var unread1 = MakeNotification(companyId, employeeId, isRead: false);
        var unread2 = MakeNotification(companyId, employeeId, isRead: false);
        var alreadyRead = MakeNotification(companyId, employeeId, isRead: true);
        ctx.Notifications.AddRange(unread1, unread2, alreadyRead);
        await ctx.SaveChangesAsync();
        var auditPublisher = new FakeAuditPublisher();

        await new MarkAllNotificationsReadHandler(ctx, auditPublisher, new FakeClock(Now.UtcDateTime)).HandleAsync(
            new MarkAllNotificationsReadRequest { CompanyId = companyId, EmployeeId = employeeId },
            CancellationToken.None);

        var all = await ctx.Notifications
            .Where(n => n.CompanyId == companyId && n.EmployeeId == employeeId)
            .ToListAsync();
        Assert.All(all, n => Assert.True(n.IsRead));

        // NOT-05: one NotificationReadAuditEvent per notification actually transitioned from
        // unread to read — the already-read notification does not produce a duplicate event.
        Assert.Equal(2, auditPublisher.Published.Count);
        var readEvents = auditPublisher.Published.Cast<NotificationReadAuditEvent>().ToList();
        var publishedIds = readEvents.Select(e => e.NotificationId).OrderBy(id => id).ToList();
        var expectedIds = new[] { unread1.Id, unread2.Id }.OrderBy(id => id).ToList();
        Assert.Equal(expectedIds, publishedIds);
        Assert.All(readEvents, e =>
        {
            Assert.Equal(companyId,  e.CompanyId);
            Assert.Equal(employeeId, e.RecipientEmployeeId);
            Assert.Equal(employeeId, ((HR.SharedKernel.IAuditEvent)e).ActorEmployeeId);
        });
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

        await new MarkAllNotificationsReadHandler(ctx, new FakeAuditPublisher(), new FakeClock(Now.UtcDateTime)).HandleAsync(
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

        await new MarkAllNotificationsReadHandler(ctx, new FakeAuditPublisher(), new FakeClock(Now.UtcDateTime)).HandleAsync(
            new MarkAllNotificationsReadRequest { CompanyId = companyA, EmployeeId = employeeId },
            CancellationToken.None);

        var bNotif = await ctx.Notifications.SingleAsync(n => n.CompanyId == companyB);
        Assert.False(bNotif.IsRead);
    }

    [Fact]
    public async Task Is_Safe_When_No_Notifications_Exist()
    {
        await using var ctx = BuildContext();
        var auditPublisher = new FakeAuditPublisher();

        var ex = await Record.ExceptionAsync(() =>
            new MarkAllNotificationsReadHandler(ctx, auditPublisher, new FakeClock(Now.UtcDateTime)).HandleAsync(
                new MarkAllNotificationsReadRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid() },
                CancellationToken.None));

        Assert.Null(ex);
        // NOT-05: nothing unread => nothing published.
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
