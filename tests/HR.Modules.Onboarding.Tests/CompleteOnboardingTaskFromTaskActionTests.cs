using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Features.CompleteOnboardingTaskFromTask;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Onboarding.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Tests;

public class CompleteOnboardingTaskFromTaskActionTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static OnboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static OnboardingPlan SeedPlan(
        OnboardingDbContext dbContext,
        Guid companyId,
        DateTimeOffset createdAt,
        OnboardingStatus? status = null)
    {
        var plan = OnboardingPlan.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), DateOnly.FromDateTime(createdAt.Date), null, createdAt);

        if (status == OnboardingStatus.InProgress)
            plan.Start(createdAt);
        else if (status == OnboardingStatus.Completed)
            plan.Complete(createdAt);

        dbContext.OnboardingPlans.Add(plan);
        return plan;
    }

    private static OnboardingTask SeedTask(
        OnboardingDbContext dbContext,
        Guid companyId,
        Guid planId,
        DateTimeOffset createdAt,
        OnboardingTaskStatus status = OnboardingTaskStatus.Pending,
        string title = "Some task")
    {
        var task = OnboardingTask.Create(
            Guid.NewGuid(), companyId, planId, title, null,
            OnboardingTemplateTaskAssignTo.Unassigned, null, createdAt);

        if (status == OnboardingTaskStatus.Completed)
            task.Complete(createdAt);
        else if (status == OnboardingTaskStatus.Skipped)
            task.Skip(createdAt);

        dbContext.OnboardingTasks.Add(task);
        return task;
    }

    private static TaskCompletionContext BuildTaskContext(
        Guid companyId,
        Guid? sourceEntityId) =>
        new(
            companyId,
            Guid.NewGuid(),
            "Complete onboarding task — Test Employee",
            null,
            TaskSource.Onboarding,
            TaskActionType.Complete,
            null,
            Guid.NewGuid(),
            Now,
            sourceEntityId);

    private static (CompleteOnboardingTaskFromTaskAction Action, FakeNotificationWriter Notifications, FakeTaskCreator TaskCreator, FakeAuditPublisher AuditPublisher)
        BuildAction(OnboardingDbContext dbContext, Guid? managerId = null)
    {
        var (action, notifications, taskCreator, auditPublisher, _) = BuildActionWithIntegrationEvents(dbContext, managerId);
        return (action, notifications, taskCreator, auditPublisher);
    }

    private static (CompleteOnboardingTaskFromTaskAction Action, FakeNotificationWriter Notifications, FakeTaskCreator TaskCreator, FakeAuditPublisher AuditPublisher, Infrastructure.CapturingIntegrationEventPublisher IntegrationPublisher)
        BuildActionWithIntegrationEvents(OnboardingDbContext dbContext, Guid? managerId = null)
    {
        var notifications = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();
        var auditPublisher = new FakeAuditPublisher();
        var integrationPublisher = new Infrastructure.CapturingIntegrationEventPublisher();
        var action = new CompleteOnboardingTaskFromTaskAction(
            dbContext,
            new FakeClock(FixedUtcNow),
            new FakeManagerReader(managerId),
            new FakeEmployeeNameReader(),
            notifications,
            taskCreator,
            auditPublisher,
            integrationPublisher);
        return (action, notifications, taskCreator, auditPublisher, integrationPublisher);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_Last_Remaining_Task_Publishes_OnboardingCompleted_IntegrationEvent()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OnboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Completed, "Task B");
        await dbContext.SaveChangesAsync();

        var (action, _, _, _, integrationPublisher) = BuildActionWithIntegrationEvents(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var evt = Assert.IsType<OnboardingCompletedIntegrationEvent>(Assert.Single(integrationPublisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(plan.EmployeeId, evt.EmployeeId);
        Assert.Equal(plan.Id, evt.OnboardingPlanId);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_A_NonLast_Task_Does_Not_Publish_OnboardingCompleted_IntegrationEvent()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OnboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task B"); // still Pending afterwards
        await dbContext.SaveChangesAsync();

        var (action, _, _, _, integrationPublisher) = BuildActionWithIntegrationEvents(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Empty(integrationPublisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Completes_Matching_Task_And_Updates_UpdatedAt()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt);
        await dbContext.SaveChangesAsync();

        var (action, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, task.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var saved = await dbContext.OnboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OnboardingTaskStatus.Completed, saved.Status);
        Assert.Equal(Now, saved.UpdatedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_First_Task_Transitions_Plan_To_InProgress_Not_Completed()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task B");
        await dbContext.SaveChangesAsync();

        var (action, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedPlan = await dbContext.OnboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OnboardingStatus.InProgress, savedPlan.Status);
        Assert.Equal(Now, savedPlan.UpdatedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_Last_Remaining_Task_Transitions_Plan_To_Completed()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OnboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Completed, "Task B");
        await dbContext.SaveChangesAsync();

        var (action, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedPlan = await dbContext.OnboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OnboardingStatus.Completed, savedPlan.Status);
        Assert.Equal(Now, savedPlan.UpdatedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Skipped_Siblings_Count_As_Terminal_When_Completing_Final_Pending_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OnboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Completed, "Task B");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Skipped, "Task C");
        await dbContext.SaveChangesAsync();

        var (action, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedPlan = await dbContext.OnboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OnboardingStatus.Completed, savedPlan.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_SourceEntityId_Is_Null()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt);
        await dbContext.SaveChangesAsync();

        var (action, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, sourceEntityId: null);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OnboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OnboardingTaskStatus.Pending, savedTask.Status);
        Assert.Equal(seedAt, savedTask.UpdatedAt);

        var savedPlan = await dbContext.OnboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OnboardingStatus.NotStarted, savedPlan.Status);
        Assert.Equal(seedAt, savedPlan.UpdatedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_Task_Not_Found()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt);
        await dbContext.SaveChangesAsync();

        var (action, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, sourceEntityId: Guid.NewGuid());

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OnboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OnboardingTaskStatus.Pending, savedTask.Status);
        Assert.Equal(seedAt, savedTask.UpdatedAt);

        var savedPlan = await dbContext.OnboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OnboardingStatus.NotStarted, savedPlan.Status);
        Assert.Equal(seedAt, savedPlan.UpdatedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Is_Idempotent_When_Task_Already_Completed()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OnboardingStatus.InProgress);
        var alreadyCompletedTask = SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Completed, "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Completed, "Task B");
        await dbContext.SaveChangesAsync();

        var (action, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, alreadyCompletedTask.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OnboardingTasks.SingleAsync(t => t.Id == alreadyCompletedTask.Id);
        Assert.Equal(OnboardingTaskStatus.Completed, savedTask.Status);
        Assert.Equal(seedAt, savedTask.UpdatedAt);

        // Even though all sibling tasks are Completed, the plan must not be mutated
        // because the action returns early before re-checking plan completion.
        var savedPlan = await dbContext.OnboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OnboardingStatus.InProgress, savedPlan.Status);
        Assert.Equal(seedAt, savedPlan.UpdatedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Is_Idempotent_When_Task_Already_Skipped()
    {
        // Mirrors the already-Completed idempotency test, but exercises the other terminal
        // branch of the "Completed or Skipped" check.
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OnboardingStatus.InProgress);
        var alreadySkippedTask = SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Skipped, "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Completed, "Task B");
        await dbContext.SaveChangesAsync();

        var (action, notifications, taskCreator, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, alreadySkippedTask.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OnboardingTasks.SingleAsync(t => t.Id == alreadySkippedTask.Id);
        Assert.Equal(OnboardingTaskStatus.Skipped, savedTask.Status);
        Assert.Equal(seedAt, savedTask.UpdatedAt);

        var savedPlan = await dbContext.OnboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OnboardingStatus.InProgress, savedPlan.Status);
        Assert.Equal(seedAt, savedPlan.UpdatedAt);
        Assert.Empty(notifications.Written);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Completes_Task_And_Saves_When_Owning_Plan_Is_Missing()
    {
        // Orphaned task (its OnboardingPlanId does not resolve to a plan): the task must still be
        // completed and saved, and the method must return early without throwing on the null plan.
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var task = SeedTask(dbContext, companyId, Guid.NewGuid(), seedAt);
        await dbContext.SaveChangesAsync();

        var (action, notifications, taskCreator, auditPublisher) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, task.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OnboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OnboardingTaskStatus.Completed, savedTask.Status);
        Assert.Equal(Now, savedTask.UpdatedAt);
        Assert.Empty(notifications.Written);
        Assert.Empty(taskCreator.Created);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_Task_Belongs_To_Different_Company()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, otherCompanyId, seedAt);
        var task = SeedTask(dbContext, otherCompanyId, plan.Id, seedAt);
        await dbContext.SaveChangesAsync();

        var (action, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, task.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OnboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OnboardingTaskStatus.Pending, savedTask.Status);
        Assert.Equal(seedAt, savedTask.UpdatedAt);

        var savedPlan = await dbContext.OnboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OnboardingStatus.NotStarted, savedPlan.Status);
        Assert.Equal(seedAt, savedPlan.UpdatedAt);
    }

    // ── Notifications on plan start, HR review task on plan completion ────────

    [Fact]
    public async Task ExecuteAsync_Completing_First_Task_Notifies_Employee()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task B"); // still Pending afterwards
        await dbContext.SaveChangesAsync();

        var (action, notifications, taskCreator, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, task.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var employeeNotification = Assert.Single(notifications.Written);
        Assert.Equal(plan.EmployeeId, employeeNotification.EmployeeId);
        Assert.Equal(NotificationType.OnboardingStarted, employeeNotification.Type);
        Assert.Equal(plan.Id, employeeNotification.SourceEntityId);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_First_Task_Also_Notifies_Manager_When_Present()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt);
        await dbContext.SaveChangesAsync();

        var (action, notifications, _, _) = BuildAction(dbContext, managerId);
        var context = BuildTaskContext(companyId, task.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(2, notifications.Written.Count);
        Assert.Contains(notifications.Written, n => n.EmployeeId == plan.EmployeeId && n.Type == NotificationType.OnboardingStarted);
        Assert.Contains(notifications.Written, n => n.EmployeeId == managerId && n.Type == NotificationType.OnboardingStarted);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_A_NonFirst_NonLast_Task_Sends_No_Notifications_And_Creates_No_Tasks()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OnboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task B"); // still Pending afterwards
        await dbContext.SaveChangesAsync();

        var (action, notifications, taskCreator, _) = BuildAction(dbContext, Guid.NewGuid());
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Empty(notifications.Written);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_Last_Remaining_Task_Creates_Unassigned_HR_Review_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OnboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Completed, "Task B");
        await dbContext.SaveChangesAsync();

        var (action, notifications, taskCreator, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var created = Assert.Single(taskCreator.Created);
        Assert.Equal(TaskSource.Onboarding, created.Source);
        Assert.Equal(TaskActionType.Review, created.ActionType);
        Assert.Null(created.AssignedEmployeeId);
        Assert.Null(created.AssignedUserId);
        Assert.Equal(plan.Id, created.SourceEntityId);

        // The plan was already InProgress, so completing its last task does not re-trigger the
        // "onboarding started" notifications — only the HR review task is created.
        Assert.Empty(notifications.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_Last_Remaining_Task_Publishes_PlanCompletedAuditEvent()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OnboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Completed, "Task B");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Skipped, "Task C");
        await dbContext.SaveChangesAsync();

        var (action, _, _, auditPublisher) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var published = Assert.Single(auditPublisher.Published);
        Assert.Equal("onboarding-plan.completed", published.EventType);
        Assert.Equal("OnboardingPlan", published.EntityType);
        Assert.Equal(plan.Id, published.EntityId);
        Assert.Equal(plan.EmployeeId, published.EmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_A_NonLast_Task_Does_Not_Publish_PlanCompletedAuditEvent()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OnboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task B"); // still Pending afterwards
        await dbContext.SaveChangesAsync();

        var (action, _, _, auditPublisher) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_NoOp_Paths_Send_No_Notifications_And_Create_No_Tasks()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OnboardingStatus.InProgress);
        var alreadyCompletedTask = SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Completed, "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OnboardingTaskStatus.Completed, "Task B");
        await dbContext.SaveChangesAsync();

        var (action, notifications, taskCreator, _) = BuildAction(dbContext, Guid.NewGuid());
        var context = BuildTaskContext(companyId, alreadyCompletedTask.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Empty(notifications.Written);
        Assert.Empty(taskCreator.Created);
    }
}
