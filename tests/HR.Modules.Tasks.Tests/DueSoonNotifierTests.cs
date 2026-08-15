using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Tasks.Tests;

public class DueSoonNotifierTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);

    private sealed record Harness(
        DueSoonNotifier Notifier,
        FakeNotificationWriter NotificationWriter,
        ServiceProvider Provider) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private static Harness BuildHarness(string dbName)
    {
        var services = new ServiceCollection();

        services.AddDbContext<TasksDbContext>(options => options.UseInMemoryDatabase(dbName));

        var notificationWriter = new FakeNotificationWriter();
        services.AddSingleton<INotificationWriter>(notificationWriter);
        services.AddSingleton<IClock>(new FakeClock(FixedUtcNow));

        var provider = services.BuildServiceProvider();
        var notifier = new DueSoonNotifier(provider.GetRequiredService<IServiceScopeFactory>());

        return new Harness(notifier, notificationWriter, provider);
    }

    private static TaskItem CreateTask(
        Guid companyId,
        DateOnly dueDate,
        TaskSource source,
        TaskActionType actionType,
        Guid? assignedEmployeeId = null,
        Guid? sourceEntityId = null) =>
        TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Provide feedback: interview with Emma Clarke", null,
            TaskPriority.Medium, source, actionType,
            dueDate,
            assignedEmployeeId ?? Guid.NewGuid(),
            assignedEmployeeId ?? Guid.NewGuid(),
            Now,
            sourceEntityId);

    [Fact]
    public async Task CheckTaskAlertsAsync_Notifies_Assigned_Employee_Of_Overdue_Task()
    {
        var companyId = Guid.NewGuid();
        var assignedEmployeeId = Guid.NewGuid();

        using var harness = BuildHarness(Guid.NewGuid().ToString("N"));

        var task = CreateTask(
            companyId, Today.AddDays(-1),
            TaskSource.Recruitment, TaskActionType.Complete,
            assignedEmployeeId: assignedEmployeeId);

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
        }

        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);

        Assert.Contains(harness.NotificationWriter.Written, w =>
            w.EmployeeId == assignedEmployeeId &&
            w.Type == NotificationType.TaskOverdue &&
            w.SourceEntityId == task.Id);
    }

    [Fact]
    public async Task CheckTaskAlertsAsync_Does_Not_Duplicate_Overdue_Notification_When_Already_Sent()
    {
        var companyId = Guid.NewGuid();
        var assignedEmployeeId = Guid.NewGuid();

        using var harness = BuildHarness(Guid.NewGuid().ToString("N"));

        var task = CreateTask(
            companyId, Today.AddDays(-1),
            TaskSource.Asset, TaskActionType.Acknowledge,
            assignedEmployeeId: assignedEmployeeId);

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
        }

        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);
        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);

        Assert.Single(harness.NotificationWriter.Written, w => w.Type == NotificationType.TaskOverdue);
    }

    [Fact]
    public async Task CheckTaskAlertsAsync_Notifies_Assigned_Employee_When_Task_Due_Soon()
    {
        var companyId = Guid.NewGuid();
        var assignedEmployeeId = Guid.NewGuid();

        using var harness = BuildHarness(Guid.NewGuid().ToString("N"));

        var task = CreateTask(
            companyId, Today.AddDays(1),
            TaskSource.Leave, TaskActionType.Approve,
            assignedEmployeeId: assignedEmployeeId);

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
        }

        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);

        Assert.Contains(harness.NotificationWriter.Written, w =>
            w.EmployeeId == assignedEmployeeId &&
            w.Type == NotificationType.TaskDueSoon &&
            w.SourceEntityId == task.Id);
    }

    [Fact]
    public async Task CheckTaskAlertsAsync_Does_Not_Notify_When_Task_Not_Yet_Due_Soon()
    {
        var companyId = Guid.NewGuid();

        using var harness = BuildHarness(Guid.NewGuid().ToString("N"));

        var task = CreateTask(
            companyId, Today.AddDays(10),
            TaskSource.Recruitment, TaskActionType.Complete);

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
        }

        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);

        Assert.Empty(harness.NotificationWriter.Written);
    }

    [Fact]
    public async Task CheckTaskAlertsAsync_Notifies_When_Due_Exactly_At_DueSoon_Cutoff()
    {
        // DueSoonDays is 2 — the cutoff comparison is "<=", so a task due exactly on
        // Today+2 must still be treated as due-soon (off-by-one boundary check).
        var companyId = Guid.NewGuid();
        var assignedEmployeeId = Guid.NewGuid();

        using var harness = BuildHarness(Guid.NewGuid().ToString("N"));

        var task = CreateTask(
            companyId, Today.AddDays(2),
            TaskSource.Recruitment, TaskActionType.Complete,
            assignedEmployeeId: assignedEmployeeId);

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
        }

        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);

        Assert.Contains(harness.NotificationWriter.Written, w =>
            w.EmployeeId == assignedEmployeeId &&
            w.Type == NotificationType.TaskDueSoon &&
            w.SourceEntityId == task.Id);
    }

    [Fact]
    public async Task CheckTaskAlertsAsync_Does_Not_Notify_When_Due_One_Day_Past_DueSoon_Cutoff()
    {
        // One day beyond the Today+2 cutoff must not trigger a due-soon notification.
        var companyId = Guid.NewGuid();

        using var harness = BuildHarness(Guid.NewGuid().ToString("N"));

        var task = CreateTask(
            companyId, Today.AddDays(3),
            TaskSource.Recruitment, TaskActionType.Complete);

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
        }

        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);

        Assert.Empty(harness.NotificationWriter.Written);
    }

    [Fact]
    public async Task CheckTaskAlertsAsync_Treats_Due_Today_As_Due_Soon_Not_Overdue()
    {
        // DueDate == today must go down the "due soon / due today" branch, not the
        // strictly-less-than "overdue" branch, and the message should say "today".
        var companyId = Guid.NewGuid();
        var assignedEmployeeId = Guid.NewGuid();

        using var harness = BuildHarness(Guid.NewGuid().ToString("N"));

        var task = CreateTask(
            companyId, Today,
            TaskSource.Recruitment, TaskActionType.Complete,
            assignedEmployeeId: assignedEmployeeId);

        await using (var scope = harness.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
            db.TaskItems.Add(task);
            await db.SaveChangesAsync();
        }

        await harness.Notifier.CheckTaskAlertsAsync(CancellationToken.None);

        Assert.Contains(harness.NotificationWriter.Written, w =>
            w.EmployeeId == assignedEmployeeId &&
            w.Type == NotificationType.TaskDueSoon &&
            w.Title.Contains("today", StringComparison.OrdinalIgnoreCase) &&
            w.Body != null && w.Body.Contains("due today", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(harness.NotificationWriter.Written, w => w.Type == NotificationType.TaskOverdue);
    }
}
