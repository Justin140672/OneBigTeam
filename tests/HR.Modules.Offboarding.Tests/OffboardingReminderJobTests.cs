using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Jobs;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Tests;

public class OffboardingReminderJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 11, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static OffboardingPlan SeedPlan(
        OffboardingDbContext dbContext,
        Guid companyId,
        Guid employeeId)
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, Today, null, Now.AddDays(-7));
        plan.Start(Now.AddDays(-7));
        dbContext.OffboardingPlans.Add(plan);
        return plan;
    }

    private static OffboardingTask SeedTask(
        OffboardingDbContext dbContext,
        Guid companyId,
        Guid planId,
        OffboardingTaskAssignTo assignTo,
        DateOnly? dueDate,
        string title = "Some offboarding task")
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), companyId, planId, title, null, assignTo, dueDate, Now.AddDays(-7));
        dbContext.OffboardingTasks.Add(task);
        return task;
    }

    private static OffboardingReminderJob BuildJob(
        OffboardingDbContext dbContext, FakeNotificationWriter notifications, Guid? managerId = null) =>
        new(dbContext, new FakeManagerReader(managerId), notifications, new FakeClock(FixedUtcNow));

    [Fact]
    public async Task ExecuteAsync_Notifies_Manager_For_Overdue_Manager_Assigned_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var plan = SeedPlan(dbContext, companyId, employeeId);
        var task = SeedTask(dbContext, companyId, plan.Id, OffboardingTaskAssignTo.Manager, Today.AddDays(-1));
        await dbContext.SaveChangesAsync();

        var notifications = new FakeNotificationWriter();
        var job = BuildJob(dbContext, notifications, managerId);

        await job.ExecuteAsync();

        var notification = Assert.Single(notifications.Written);
        Assert.Equal(managerId, notification.EmployeeId);
        Assert.Equal(NotificationType.OffboardingTaskOverdue, notification.Type);
        Assert.Equal(task.Id, notification.SourceEntityId);
    }

    [Fact]
    public async Task ExecuteAsync_Sends_No_Notification_For_Manager_Assigned_Task_When_No_Manager()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(dbContext, companyId, employeeId);
        SeedTask(dbContext, companyId, plan.Id, OffboardingTaskAssignTo.Manager, Today.AddDays(-1));
        await dbContext.SaveChangesAsync();

        var notifications = new FakeNotificationWriter();
        var job = BuildJob(dbContext, notifications, managerId: null);

        await job.ExecuteAsync();

        Assert.Empty(notifications.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Notifies_Employee_For_Overdue_Employee_Assigned_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(dbContext, companyId, employeeId);
        var task = SeedTask(dbContext, companyId, plan.Id, OffboardingTaskAssignTo.Employee, Today.AddDays(-1));
        await dbContext.SaveChangesAsync();

        var notifications = new FakeNotificationWriter();
        var job = BuildJob(dbContext, notifications);

        await job.ExecuteAsync();

        var notification = Assert.Single(notifications.Written);
        Assert.Equal(plan.EmployeeId, notification.EmployeeId);
        Assert.Equal(NotificationType.OffboardingTaskOverdue, notification.Type);
        Assert.Equal(task.Id, notification.SourceEntityId);
    }

    [Fact]
    public async Task ExecuteAsync_Sends_No_Notification_For_Overdue_HR_Assigned_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(dbContext, companyId, employeeId);
        SeedTask(dbContext, companyId, plan.Id, OffboardingTaskAssignTo.HR, Today.AddDays(-1));
        await dbContext.SaveChangesAsync();

        var notifications = new FakeNotificationWriter();
        var job = BuildJob(dbContext, notifications, Guid.NewGuid());

        await job.ExecuteAsync();

        Assert.Empty(notifications.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Sends_No_Notification_For_Task_Not_Yet_Due()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(dbContext, companyId, employeeId);
        SeedTask(dbContext, companyId, plan.Id, OffboardingTaskAssignTo.Manager, Today.AddDays(1));
        await dbContext.SaveChangesAsync();

        var notifications = new FakeNotificationWriter();
        var job = BuildJob(dbContext, notifications, Guid.NewGuid());

        await job.ExecuteAsync();

        Assert.Empty(notifications.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Sends_No_Notification_For_Overdue_Completed_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(dbContext, companyId, employeeId);
        var task = SeedTask(dbContext, companyId, plan.Id, OffboardingTaskAssignTo.Manager, Today.AddDays(-1));
        task.Complete(Now);
        await dbContext.SaveChangesAsync();

        var notifications = new FakeNotificationWriter();
        var job = BuildJob(dbContext, notifications, Guid.NewGuid());

        await job.ExecuteAsync();

        Assert.Empty(notifications.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Sends_No_Notification_For_Overdue_Skipped_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = SeedPlan(dbContext, companyId, employeeId);
        var task = SeedTask(dbContext, companyId, plan.Id, OffboardingTaskAssignTo.Manager, Today.AddDays(-1));
        task.Skip(Now);
        await dbContext.SaveChangesAsync();

        var notifications = new FakeNotificationWriter();
        var job = BuildJob(dbContext, notifications, Guid.NewGuid());

        await job.ExecuteAsync();

        Assert.Empty(notifications.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Duplicate_Notification_When_Run_Twice()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var plan = SeedPlan(dbContext, companyId, employeeId);
        SeedTask(dbContext, companyId, plan.Id, OffboardingTaskAssignTo.Manager, Today.AddDays(-1));
        await dbContext.SaveChangesAsync();

        var notifications = new FakeNotificationWriter();

        // Run twice against the same writer — the second run must see the existing
        // notification via ExistsAsync and skip re-sending it.
        await BuildJob(dbContext, notifications, managerId).ExecuteAsync();
        await BuildJob(dbContext, notifications, managerId).ExecuteAsync();

        Assert.Single(notifications.Written, n => n.Type == NotificationType.OffboardingTaskOverdue);
    }

    [Fact]
    public async Task ExecuteAsync_Processes_Multiple_Overdue_Tasks_Across_Different_Plans()
    {
        await using var dbContext = BuildContext();

        var companyIdA = Guid.NewGuid();
        var employeeIdA = Guid.NewGuid();
        var planA = SeedPlan(dbContext, companyIdA, employeeIdA);
        var taskA = SeedTask(dbContext, companyIdA, planA.Id, OffboardingTaskAssignTo.Employee, Today.AddDays(-1), "Task A");

        var companyIdB = Guid.NewGuid();
        var employeeIdB = Guid.NewGuid();
        var planB = SeedPlan(dbContext, companyIdB, employeeIdB);
        var taskB = SeedTask(dbContext, companyIdB, planB.Id, OffboardingTaskAssignTo.Employee, Today.AddDays(-2), "Task B");

        await dbContext.SaveChangesAsync();

        var notifications = new FakeNotificationWriter();
        var job = BuildJob(dbContext, notifications);

        await job.ExecuteAsync();

        Assert.Equal(2, notifications.Written.Count);
        Assert.Contains(notifications.Written, n => n.EmployeeId == employeeIdA && n.SourceEntityId == taskA.Id);
        Assert.Contains(notifications.Written, n => n.EmployeeId == employeeIdB && n.SourceEntityId == taskB.Id);
    }
}
