using HR.Modules.Notifications;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class TaskCreatorNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Creates_Notification_When_Task_Has_Assigned_Employee()
    {
        await using var ctx = BuildContext();
        var notificationWriter = new FakeNotificationWriter();
        var companyId = Guid.NewGuid();
        var assignedEmployeeId = Guid.NewGuid();

        var creator = new TaskCreator(ctx, notificationWriter, new FakeClock(Now.UtcDateTime), new FakeAuditPublisher());

        await creator.CreateAsync(
            companyId, Guid.NewGuid(),
            "Review leave request", "Some detail",
            TaskPriority.Medium, TaskSource.Leave,
            new DateOnly(2026, 7, 1),
            assignedEmployeeId: assignedEmployeeId,
            assignedUserId: null,
            sourceEntityId: null,
            CancellationToken.None);

        var notification = Assert.Single(notificationWriter.Written);
        Assert.Equal(companyId, notification.CompanyId);
        Assert.Equal(assignedEmployeeId, notification.EmployeeId);
        Assert.Equal("New task assigned: Review leave request", notification.Title);
        Assert.Equal("Some detail", notification.Body);
        Assert.Equal(NotificationType.TaskAssigned, notification.Type);
    }

    [Fact]
    public async Task Does_Not_Create_Notification_When_No_Assigned_Employee()
    {
        await using var ctx = BuildContext();
        var notificationWriter = new FakeNotificationWriter();

        var creator = new TaskCreator(ctx, notificationWriter, new FakeClock(Now.UtcDateTime), new FakeAuditPublisher());

        await creator.CreateAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            "Unassigned task", null,
            TaskPriority.Low, TaskSource.Manual,
            null,
            assignedEmployeeId: null,
            assignedUserId: null,
            sourceEntityId: null,
            CancellationToken.None);

        Assert.Empty(notificationWriter.Written);
    }

    [Fact]
    public async Task Notification_SourceEntityId_Matches_Created_Task()
    {
        await using var ctx = BuildContext();
        var notificationWriter = new FakeNotificationWriter();
        var companyId = Guid.NewGuid();
        var assignedEmployeeId = Guid.NewGuid();

        var creator = new TaskCreator(ctx, notificationWriter, new FakeClock(Now.UtcDateTime), new FakeAuditPublisher());

        await creator.CreateAsync(
            companyId, Guid.NewGuid(),
            "A task", null,
            TaskPriority.High, TaskSource.Manual,
            null,
            assignedEmployeeId: assignedEmployeeId,
            assignedUserId: null,
            sourceEntityId: null,
            CancellationToken.None);

        var task = await ctx.TaskItems.SingleAsync();
        var notification = Assert.Single(notificationWriter.Written);
        Assert.Equal(task.Id, notification.SourceEntityId);
    }

    [Fact]
    public async Task Notification_Title_Truncates_Long_Task_Title()
    {
        await using var ctx = BuildContext();
        var notificationWriter = new FakeNotificationWriter();
        var assignedEmployeeId = Guid.NewGuid();
        var longTitle = new string('x', 280);

        var creator = new TaskCreator(ctx, notificationWriter, new FakeClock(Now.UtcDateTime), new FakeAuditPublisher());

        await creator.CreateAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            longTitle, null,
            TaskPriority.Low, TaskSource.Manual,
            null,
            assignedEmployeeId: assignedEmployeeId,
            assignedUserId: null,
            sourceEntityId: null,
            CancellationToken.None);

        var notification = Assert.Single(notificationWriter.Written);
        Assert.True(notification.Title.Length <= 300);
    }

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new TasksDbContext(options);
    }
}
