using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.CompleteOffboardingTaskFromTask;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Offboarding.Tests;

public class CompleteOffboardingTaskFromTaskActionTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static OffboardingPlan SeedPlan(
        OffboardingDbContext dbContext,
        Guid companyId,
        DateTimeOffset createdAt,
        OffboardingStatus? status = null)
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), DateOnly.FromDateTime(createdAt.Date), null, createdAt);

        if (status == OffboardingStatus.InProgress)
            plan.Start(createdAt);
        else if (status == OffboardingStatus.Completed)
            plan.Complete(createdAt);

        dbContext.OffboardingPlans.Add(plan);
        return plan;
    }

    private static OffboardingTask SeedTask(
        OffboardingDbContext dbContext,
        Guid companyId,
        Guid planId,
        DateTimeOffset createdAt,
        OffboardingTaskStatus status = OffboardingTaskStatus.Pending,
        string title = "Some task",
        Guid? assetAssignmentId = null)
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), companyId, planId, title, null,
            OffboardingTaskAssignTo.Employee, null, createdAt,
            assetAssignmentId: assetAssignmentId);

        if (status == OffboardingTaskStatus.Completed)
            task.Complete(createdAt);
        else if (status == OffboardingTaskStatus.Skipped)
            task.Skip(createdAt);

        dbContext.OffboardingTasks.Add(task);
        return task;
    }

    private static TaskCompletionContext BuildTaskContext(
        Guid companyId,
        Guid? sourceEntityId,
        string? outcomeDecision = null,
        string? outcomeReason = null) =>
        new(
            companyId,
            Guid.NewGuid(),
            "Complete offboarding task — Test Employee",
            null,
            TaskSource.Offboarding,
            TaskActionType.Complete,
            null,
            Guid.NewGuid(),
            Now,
            sourceEntityId,
            outcomeDecision,
            outcomeReason);

    private static (CompleteOffboardingTaskFromTaskAction Action, FakeNotificationWriter Notifications, FakeTaskCreator TaskCreator, FakeAuditPublisher AuditPublisher, FakeAssetReturnService AssetReturnService)
        BuildAction(
            OffboardingDbContext dbContext,
            Dictionary<Guid, string>? names = null,
            FakeAssetReturnService? assetReturnService = null)
    {
        var notifications = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();
        var auditPublisher = new FakeAuditPublisher();
        assetReturnService ??= new FakeAssetReturnService();
        var action = new CompleteOffboardingTaskFromTaskAction(
            dbContext,
            new FakeClock(FixedUtcNow),
            new FakeEmployeeNameReader(names),
            notifications,
            taskCreator,
            assetReturnService,
            auditPublisher,
            new HR.Modules.Offboarding.Tests.Infrastructure.FakeIntegrationEventPublisher(),
            NullLogger<CompleteOffboardingTaskFromTaskAction>.Instance);
        return (action, notifications, taskCreator, auditPublisher, assetReturnService);
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

        var (action, _, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, task.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var saved = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OffboardingTaskStatus.Completed, saved.Status);
        Assert.Equal(Now, saved.CompletedAt);
        Assert.Equal(Now, saved.UpdatedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_A_NonFinal_Task_Does_Not_Complete_The_Plan()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task B"); // still Pending afterwards
        await dbContext.SaveChangesAsync();

        var (action, _, taskCreator, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.InProgress, savedPlan.Status);
        Assert.Equal(seedAt, savedPlan.UpdatedAt);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_Final_Remaining_Task_Transitions_Plan_To_Completed()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Completed, "Task B");
        await dbContext.SaveChangesAsync();

        var (action, _, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.Completed, savedPlan.Status);
        Assert.Equal(Now, savedPlan.UpdatedAt);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_Final_Remaining_Task_Publishes_PlanCompletedAuditEvent()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Completed, "Task B");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Skipped, "Task C");
        await dbContext.SaveChangesAsync();

        var (action, _, _, auditPublisher, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var published = Assert.Single(auditPublisher.Published);
        Assert.Equal("offboarding-plan.completed", published.EventType);
        Assert.Equal("OffboardingPlan", published.EntityType);
        Assert.Equal(plan.Id, published.EntityId);
        Assert.Equal(plan.EmployeeId, published.EmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_A_NonFinal_Task_Does_Not_Publish_PlanCompletedAuditEvent()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task B"); // still Pending afterwards
        await dbContext.SaveChangesAsync();

        var (action, _, _, auditPublisher, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_Final_Task_Creates_Unassigned_HR_Review_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        const string employeeName = "Jamie Smith";

        var seedAt = Now.AddDays(-1);
        var plan = OffboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, DateOnly.FromDateTime(seedAt.Date), null, seedAt);
        plan.Start(seedAt);
        dbContext.OffboardingPlans.Add(plan);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Completed, "Task B");
        await dbContext.SaveChangesAsync();

        var names = new Dictionary<Guid, string> { [employeeId] = employeeName };
        var (action, notifications, taskCreator, _, _) = BuildAction(dbContext, names);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var created = Assert.Single(taskCreator.Created);
        Assert.Equal(TaskSource.Offboarding, created.Source);
        Assert.Equal(TaskActionType.Review, created.ActionType);
        Assert.Null(created.AssignedEmployeeId);
        Assert.Null(created.AssignedUserId);
        Assert.Equal(plan.Id, created.SourceEntityId);
        Assert.Contains(employeeName, created.Title);

        // The action does not send notifications on task completion — only StartOffboarding does.
        Assert.Empty(notifications.Written);
    }

    [Fact]
    public async Task ExecuteAsync_Skipped_Siblings_Count_As_Terminal_When_Completing_Final_Pending_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Completed, "Task B");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Skipped, "Task C");
        await dbContext.SaveChangesAsync();

        var (action, _, taskCreator, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.Completed, savedPlan.Status);
        Assert.Single(taskCreator.Created);
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

        var (action, notifications, taskCreator, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, sourceEntityId: null);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OffboardingTaskStatus.Pending, savedTask.Status);
        Assert.Equal(seedAt, savedTask.UpdatedAt);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.NotStarted, savedPlan.Status);
        Assert.Equal(seedAt, savedPlan.UpdatedAt);
        Assert.Empty(notifications.Written);
        Assert.Empty(taskCreator.Created);
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

        var (action, _, taskCreator, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, sourceEntityId: Guid.NewGuid());

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OffboardingTaskStatus.Pending, savedTask.Status);
        Assert.Equal(seedAt, savedTask.UpdatedAt);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.NotStarted, savedPlan.Status);
        Assert.Equal(seedAt, savedPlan.UpdatedAt);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Is_Idempotent_When_Task_Already_Completed()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var alreadyCompletedTask = SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Completed, "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Completed, "Task B");
        await dbContext.SaveChangesAsync();

        var (action, _, taskCreator, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, alreadyCompletedTask.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == alreadyCompletedTask.Id);
        Assert.Equal(OffboardingTaskStatus.Completed, savedTask.Status);
        Assert.Equal(seedAt, savedTask.UpdatedAt);

        // Even though all sibling tasks are Completed, the plan must not be mutated
        // because the action returns early before re-checking plan completion.
        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.InProgress, savedPlan.Status);
        Assert.Equal(seedAt, savedPlan.UpdatedAt);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Is_Idempotent_When_Task_Already_Skipped()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var alreadySkippedTask = SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Skipped, "Task A");
        await dbContext.SaveChangesAsync();

        var (action, _, taskCreator, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, alreadySkippedTask.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == alreadySkippedTask.Id);
        Assert.Equal(OffboardingTaskStatus.Skipped, savedTask.Status);
        Assert.Empty(taskCreator.Created);
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

        var (action, _, taskCreator, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, task.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OffboardingTaskStatus.Pending, savedTask.Status);
        Assert.Equal(seedAt, savedTask.UpdatedAt);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.NotStarted, savedPlan.Status);
        Assert.Equal(seedAt, savedPlan.UpdatedAt);
        Assert.Empty(taskCreator.Created);
    }

    // ---- OFF-04: asset-return task completion ----

    [Fact]
    public async Task ExecuteAsync_AssetReturnTask_Calls_VerifiedReturn_With_Plan_EmployeeId_And_Completes_On_Success()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, DateOnly.FromDateTime(seedAt.Date), null, seedAt);
        plan.Start(seedAt);
        dbContext.OffboardingPlans.Add(plan);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Return asset: Laptop", assetAssignmentId: assignmentId);
        await dbContext.SaveChangesAsync();

        var assetReturnService = new FakeAssetReturnService { NextResult = AssetReturnResult.Success };
        var (action, _, _, _, _) = BuildAction(dbContext, assetReturnService: assetReturnService);
        var context = BuildTaskContext(companyId, task.Id, outcomeReason: "Handed in to IT");

        await action.ExecuteAsync(context, CancellationToken.None);

        var call = Assert.Single(assetReturnService.VerifiedCalls);
        Assert.Equal(companyId, call.CompanyId);
        Assert.Equal(assignmentId, call.AssignmentId);
        Assert.Equal(employeeId, call.ExpectedEmployeeId);
        Assert.Equal(AssetReturnOutcome.Returned, call.Outcome);
        Assert.Equal(context.CompletedBy, call.ReturnedBy);
        Assert.Equal("Handed in to IT", call.Notes);

        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OffboardingTaskStatus.Completed, savedTask.Status);
    }

    [Theory]
    [InlineData(AssetReturnResult.EmployeeMismatch)]
    [InlineData(AssetReturnResult.NotFound)]
    public async Task ExecuteAsync_AssetReturnTask_Not_Completed_And_Plan_Not_Completed_When_Return_Fails(
        AssetReturnResult failureResult)
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, DateOnly.FromDateTime(seedAt.Date), null, seedAt);
        plan.Start(seedAt);
        dbContext.OffboardingPlans.Add(plan);
        // This is the last outstanding task — if it were completed, the plan would complete too.
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Return asset: Laptop", assetAssignmentId: assignmentId);
        await dbContext.SaveChangesAsync();

        var assetReturnService = new FakeAssetReturnService { NextResult = failureResult };
        var (action, _, taskCreator, auditPublisher, _) = BuildAction(dbContext, assetReturnService: assetReturnService);
        var context = BuildTaskContext(companyId, task.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OffboardingTaskStatus.Pending, savedTask.Status);
        Assert.Null(savedTask.CompletedAt);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.InProgress, savedPlan.Status);

        Assert.Empty(taskCreator.Created);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_AssetReturnTask_Completes_When_Already_Returned()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, DateOnly.FromDateTime(seedAt.Date), null, seedAt);
        plan.Start(seedAt);
        dbContext.OffboardingPlans.Add(plan);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Return asset: Laptop", assetAssignmentId: assignmentId);
        await dbContext.SaveChangesAsync();

        var assetReturnService = new FakeAssetReturnService { NextResult = AssetReturnResult.AlreadyReturned };
        var (action, _, _, _, _) = BuildAction(dbContext, assetReturnService: assetReturnService);
        var context = BuildTaskContext(companyId, task.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OffboardingTaskStatus.Completed, savedTask.Status);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.Completed, savedPlan.Status);
    }

    [Theory]
    [InlineData("Lost", AssetReturnOutcome.Lost)]
    [InlineData("Damaged", AssetReturnOutcome.Damaged)]
    [InlineData(null, AssetReturnOutcome.Returned)]
    [InlineData("SomethingElse", AssetReturnOutcome.Returned)]
    public async Task ExecuteAsync_AssetReturnTask_Maps_OutcomeDecision_To_AssetReturnOutcome(
        string? outcomeDecision, AssetReturnOutcome expectedOutcome)
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, DateOnly.FromDateTime(seedAt.Date), null, seedAt);
        plan.Start(seedAt);
        dbContext.OffboardingPlans.Add(plan);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Return asset: Laptop", assetAssignmentId: assignmentId);
        await dbContext.SaveChangesAsync();

        var assetReturnService = new FakeAssetReturnService { NextResult = AssetReturnResult.Success };
        var (action, _, _, _, _) = BuildAction(dbContext, assetReturnService: assetReturnService);
        var context = BuildTaskContext(companyId, task.Id, outcomeDecision: outcomeDecision);

        await action.ExecuteAsync(context, CancellationToken.None);

        var call = Assert.Single(assetReturnService.VerifiedCalls);
        Assert.Equal(expectedOutcome, call.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_NonAssetReturnTask_Never_Calls_AssetReturnService()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Return badge"); // no AssetAssignmentId
        await dbContext.SaveChangesAsync();

        var assetReturnService = new FakeAssetReturnService();
        var (action, _, _, _, _) = BuildAction(dbContext, assetReturnService: assetReturnService);
        var context = BuildTaskContext(companyId, task.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Empty(assetReturnService.VerifiedCalls);
        Assert.Empty(assetReturnService.Calls);

        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OffboardingTaskStatus.Completed, savedTask.Status);
    }
}
