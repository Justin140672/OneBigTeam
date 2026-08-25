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

// OFF-07: note on concurrency coverage — TryCompletePlanAsync's row-lock guarantee (`SELECT ... FOR
// UPDATE` inside an explicit transaction) is a Postgres-specific mechanism. This test class uses
// EF Core's InMemory provider (see BuildContext below), which does not support transactions or raw
// SQL statements like FOR UPDATE, so the actual "two concurrent completions of a plan's last two
// mandatory tasks only complete the plan once" guarantee cannot be exercised here. That scenario is
// covered instead by the Aspire/Postgres integration test in
// HR.Integration.Tests/OffboardingCompletionRulesIntegrationTests.cs, which runs against a real
// Postgres instance.
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
        Guid? assetAssignmentId = null,
        bool requiresHrConfirmation = false,
        bool isMandatory = true)
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), companyId, planId, title, null,
            OffboardingTaskAssignTo.Employee, null, createdAt,
            assetAssignmentId: assetAssignmentId,
            requiresHrConfirmation: requiresHrConfirmation,
            isMandatory: isMandatory);

        if (status == OffboardingTaskStatus.Completed)
            task.Complete(createdAt);
        else if (status == OffboardingTaskStatus.Skipped)
            task.Skip(createdAt, "Skipped for test.", Guid.NewGuid());

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
            FakeAssetReturnService? assetReturnService = null,
            FakeHrAdministratorDirectory? hrAdministratorDirectory = null)
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
            hrAdministratorDirectory ?? new FakeHrAdministratorDirectory(),
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
        // OFF-07: Task C is explicitly non-mandatory here — a Skipped *mandatory* task would keep
        // the plan from completing (see ExecuteAsync_Completing_Optional_Tasks_Does_Not_Complete_Plan_While_Mandatory_Task_Outstanding
        // and the sibling test below), so this test only proves "final task completion" once every
        // remaining task is legitimately resolvable.
        SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Skipped, "Task C", isMandatory: false);
        await dbContext.SaveChangesAsync();

        var (action, _, _, auditPublisher, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        // OFF-08: completing the final task now publishes two audit entries — the task-level
        // OffboardingTaskCompletedAuditEvent (every completion) and the plan-level
        // OffboardingPlanCompletedAuditEvent (only when this was the resolving task).
        Assert.Equal(2, auditPublisher.Published.Count);

        var taskEvent = Assert.Single(auditPublisher.Published, e => e.EventType == "offboarding-task.completed");
        Assert.Equal("OffboardingTask", taskEvent.EntityType);
        Assert.Equal(taskToComplete.Id, taskEvent.EntityId);
        Assert.Equal(plan.EmployeeId, taskEvent.EmployeeId);
        Assert.Equal(context.CompletedBy, taskEvent.ActorEmployeeId);
        Assert.Equal(plan.Id, taskEvent.CorrelationId);

        var planEvent = Assert.Single(auditPublisher.Published, e => e.EventType == "offboarding-plan.completed");
        Assert.Equal("OffboardingPlan", planEvent.EntityType);
        Assert.Equal(plan.Id, planEvent.EntityId);
        Assert.Equal(plan.EmployeeId, planEvent.EmployeeId);
        // The person whose action resolved the plan's last task is attributed as the plan-completed
        // actor too — never assumed to be the leaving employee themself.
        Assert.Equal(context.CompletedBy, planEvent.ActorEmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_A_NonFinal_Task_Publishes_Only_The_TaskLevel_Audit_Event()
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

        // OFF-08: a per-task audit entry is always published on completion, even when it doesn't
        // resolve the whole plan — only the plan-level event is conditional on that.
        var published = Assert.Single(auditPublisher.Published);
        Assert.Equal("offboarding-task.completed", published.EventType);
        Assert.Equal(taskToComplete.Id, published.EntityId);
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
    public async Task ExecuteAsync_Skipped_Optional_Siblings_Count_As_Terminal_When_Completing_Final_Pending_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Completed, "Task B");
        // OFF-07: only a Skipped *non-mandatory* task counts as resolved for completion purposes.
        SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Skipped, "Task C", isMandatory: false);
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

    // ---- OFF-05: HR reconciliation flag clearing ----

    [Fact]
    public async Task ExecuteAsync_Completing_The_Sole_Outstanding_ReconciliationTask_Clears_RequiresHrReconciliation()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        plan.MarkHrReconciliationRequired(seedAt);
        var reconciliationTask = SeedTask(
            dbContext, companyId, plan.Id, seedAt, title: "Confirm return of asset (reconciliation)",
            requiresHrConfirmation: true);
        await dbContext.SaveChangesAsync();

        var (action, _, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, reconciliationTask.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.False(savedPlan.RequiresHrReconciliation);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_One_Of_Several_ReconciliationTasks_Leaves_RequiresHrReconciliation_True()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        plan.MarkHrReconciliationRequired(seedAt);
        var firstReconciliationTask = SeedTask(
            dbContext, companyId, plan.Id, seedAt, title: "Reconciliation task A", requiresHrConfirmation: true);
        SeedTask(
            dbContext, companyId, plan.Id, seedAt, title: "Reconciliation task B", requiresHrConfirmation: true);
        await dbContext.SaveChangesAsync();

        var (action, _, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, firstReconciliationTask.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.True(savedPlan.RequiresHrReconciliation);
    }

    [Fact]
    public async Task ExecuteAsync_Replaying_Completion_Of_An_Already_Completed_ReconciliationTask_Is_Idempotent()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        plan.MarkHrReconciliationRequired(seedAt);
        var reconciliationTask = SeedTask(
            dbContext, companyId, plan.Id, seedAt, title: "Reconciliation task", requiresHrConfirmation: true);
        await dbContext.SaveChangesAsync();

        var (action, _, taskCreator, auditPublisher, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, reconciliationTask.Id);

        // First completion clears the flag and (since this is the plan's only task) completes the plan.
        await action.ExecuteAsync(context, CancellationToken.None);

        var afterFirst = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.False(afterFirst.RequiresHrReconciliation);
        Assert.Equal(OffboardingStatus.Completed, afterFirst.Status);
        Assert.Single(taskCreator.Created);
        // OFF-08: task-level + plan-level audit entries for this single resolving completion.
        Assert.Equal(2, auditPublisher.Published.Count);

        // Replaying the same completion (e.g. a retried Tasks-module callback) hits the
        // Status is Completed/Skipped early-return and must not throw, re-clear an already-clear
        // flag, or fire duplicate audit/HR-completion-review events.
        await action.ExecuteAsync(context, CancellationToken.None);

        var afterSecond = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.False(afterSecond.RequiresHrReconciliation);
        Assert.Equal(OffboardingStatus.Completed, afterSecond.Status);
        Assert.Single(taskCreator.Created);
        Assert.Equal(2, auditPublisher.Published.Count);
    }

    // ---- OFF-07: "Skip" outcome decision ----

    [Fact]
    public async Task ExecuteAsync_Skip_OutcomeDecision_With_Reason_Skips_The_Task_Instead_Of_Completing_It()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Optional handover note");
        await dbContext.SaveChangesAsync();

        var (action, _, _, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(
            companyId, task.Id, outcomeDecision: "Skip", outcomeReason: "No handover required.");

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OffboardingTaskStatus.Skipped, savedTask.Status);
        Assert.Null(savedTask.CompletedAt);
        Assert.Equal("No handover required.", savedTask.SkipReason);
        Assert.Equal(context.CompletedBy, savedTask.SkippedByUserId);
        Assert.Equal(Now, savedTask.SkippedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Skip_OutcomeDecision_Without_Reason_Leaves_Task_Outstanding_And_Does_Not_Throw(
        string? outcomeReason)
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Optional handover note");
        await dbContext.SaveChangesAsync();

        var (action, _, taskCreator, auditPublisher, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, task.Id, outcomeDecision: "Skip", outcomeReason: outcomeReason);

        var exception = await Record.ExceptionAsync(() => action.ExecuteAsync(context, CancellationToken.None));

        Assert.Null(exception);
        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == task.Id);
        Assert.Equal(OffboardingTaskStatus.Pending, savedTask.Status);
        Assert.Null(savedTask.SkipReason);
        Assert.Null(savedTask.SkippedByUserId);
        Assert.Null(savedTask.SkippedAt);
        Assert.Equal(seedAt, savedTask.UpdatedAt);
        Assert.Empty(taskCreator.Created);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Skipping_The_Final_Remaining_Mandatory_Task_Does_Not_Complete_The_Plan()
    {
        // OFF-07: a mandatory task that is Skipped (even with a valid reason/actor) is still an
        // unresolved material exit obligation — Skip is a legitimate way to close out the task
        // itself, but it must not let the plan auto-complete. Only a non-mandatory task's Skip can
        // stand in for Completed when deciding whether the plan is done.
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var taskToSkip = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Completed, "Task B");
        await dbContext.SaveChangesAsync();

        var (action, _, taskCreator, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(
            companyId, taskToSkip.Id, outcomeDecision: "Skip", outcomeReason: "Not applicable.");

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == taskToSkip.Id);
        Assert.Equal(OffboardingTaskStatus.Skipped, savedTask.Status);
        Assert.Equal("Not applicable.", savedTask.SkipReason);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.InProgress, savedPlan.Status);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Skipping_The_Final_Remaining_Optional_Task_Completes_The_Plan()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var taskToSkip = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A", isMandatory: false);
        SeedTask(dbContext, companyId, plan.Id, seedAt, OffboardingTaskStatus.Completed, "Task B");
        await dbContext.SaveChangesAsync();

        var (action, _, taskCreator, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(
            companyId, taskToSkip.Id, outcomeDecision: "Skip", outcomeReason: "Not applicable.");

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.Completed, savedPlan.Status);
        Assert.Single(taskCreator.Created);
    }

    // ---- OFF-07: mandatory-vs-optional completion gating ----

    [Fact]
    public async Task ExecuteAsync_Completing_Optional_Tasks_Does_Not_Complete_Plan_While_Mandatory_Task_Outstanding()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var optionalTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Optional task", null,
            OffboardingTaskAssignTo.Employee, null, seedAt, isMandatory: false);
        dbContext.OffboardingTasks.Add(optionalTask);
        // A mandatory task remains Pending — the plan must not complete no matter what happens to
        // the optional task above.
        SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Mandatory task");
        await dbContext.SaveChangesAsync();

        var (action, _, taskCreator, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, optionalTask.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.InProgress, savedPlan.Status);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_The_Last_Mandatory_Task_Completes_Plan_Even_With_Optional_Tasks_Present()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = SeedPlan(dbContext, companyId, seedAt, OffboardingStatus.InProgress);
        var lastMandatoryTask = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Last mandatory task");
        var completedOptional = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Completed optional task", null,
            OffboardingTaskAssignTo.Employee, null, seedAt, isMandatory: false);
        completedOptional.Complete(seedAt);
        var skippedOptional = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Skipped optional task", null,
            OffboardingTaskAssignTo.Employee, null, seedAt, isMandatory: false);
        skippedOptional.Skip(seedAt, "Not applicable.", Guid.NewGuid());
        dbContext.OffboardingTasks.AddRange(completedOptional, skippedOptional);
        await dbContext.SaveChangesAsync();

        var (action, _, taskCreator, _, _) = BuildAction(dbContext);
        var context = BuildTaskContext(companyId, lastMandatoryTask.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.Completed, savedPlan.Status);
        Assert.Single(taskCreator.Created);
    }

    // ---- OFF-07: HR completion-review task assignment ----

    [Fact]
    public async Task ExecuteAsync_Completing_Final_Task_Assigns_Review_Task_To_Lowest_Guid_HR_Administrator_With_HighPriority_And_DueDate()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var lastWorkingDay = new DateOnly(2026, 7, 1);

        var seedAt = Now.AddDays(-1);
        var plan = OffboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, lastWorkingDay, null, seedAt);
        plan.Start(seedAt);
        dbContext.OffboardingPlans.Add(plan);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        await dbContext.SaveChangesAsync();

        var higherGuidAdmin = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var lowerGuidAdmin = new Guid("00000000-0000-0000-0000-000000000001");
        var hrAdministratorDirectory = new FakeHrAdministratorDirectory([higherGuidAdmin, lowerGuidAdmin]);

        var (action, notifications, taskCreator, _, _) = BuildAction(
            dbContext, hrAdministratorDirectory: hrAdministratorDirectory);
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var created = Assert.Single(taskCreator.Created);
        Assert.Equal(lowerGuidAdmin, created.AssignedEmployeeId);
        Assert.Equal(TaskPriority.High, created.Priority);
        Assert.Equal(lastWorkingDay.AddDays(3), created.DueDate);

        // Every other HR administrator (not the assignee) gets an in-app notification.
        var notification = Assert.Single(notifications.Written);
        Assert.Equal(higherGuidAdmin, notification.EmployeeId);
        Assert.Equal(NotificationType.OffboardingCompleted, notification.Type);
    }

    [Fact]
    public async Task ExecuteAsync_Completing_Final_Task_With_No_HR_Administrators_Assigns_Review_Task_To_Nobody_And_Sends_No_Notifications()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 7, 1), null, seedAt);
        plan.Start(seedAt);
        dbContext.OffboardingPlans.Add(plan);
        var taskToComplete = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Task A");
        await dbContext.SaveChangesAsync();

        var (action, notifications, taskCreator, _, _) = BuildAction(
            dbContext, hrAdministratorDirectory: new FakeHrAdministratorDirectory([]));
        var context = BuildTaskContext(companyId, taskToComplete.Id);

        await action.ExecuteAsync(context, CancellationToken.None);

        var created = Assert.Single(taskCreator.Created);
        Assert.Null(created.AssignedEmployeeId);
        Assert.Empty(notifications.Written);
    }

    // OFF-08: task-level completion audit — actor must be the person who completed the
    // Tasks-module TaskItem (TaskCompletionContext.CompletedBy), never assumed to be the plan's
    // employee, and the event must carry the OffboardingTaskId/OffboardingPlanId/AssetAssignmentId
    // cross-module correlation fields.
    [Fact]
    public async Task ExecuteAsync_Publishes_OffboardingTaskCompletedAuditEvent_With_Correct_Actor_And_Ids()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var assetAssignmentId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 7, 1), null, seedAt);
        plan.Start(seedAt);
        dbContext.OffboardingPlans.Add(plan);
        var task = SeedTask(
            dbContext, companyId, plan.Id, seedAt, title: "Return laptop", assetAssignmentId: assetAssignmentId);
        // A second outstanding mandatory task so completing the first does not also complete the plan
        // — keeps this test focused on the task-level event only.
        SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Other task");
        await dbContext.SaveChangesAsync();

        var (action, _, _, auditPublisher, assetReturnService) = BuildAction(dbContext);
        var actorId = Guid.NewGuid();
        var context = BuildTaskContext(companyId, task.Id) with { CompletedBy = actorId };

        await action.ExecuteAsync(context, CancellationToken.None);

        var completedEvent = Assert.Single(
            auditPublisher.Published.OfType<HR.Modules.Offboarding.OffboardingTaskCompletedAuditEvent>());
        Assert.Equal(task.Id, completedEvent.OffboardingTaskId);
        Assert.Equal(plan.Id, completedEvent.OffboardingPlanId);
        Assert.Equal(employeeId, completedEvent.EmployeeId);
        Assert.Equal(actorId, completedEvent.ActorEmployeeId);
        Assert.Equal(assetAssignmentId, completedEvent.AssetAssignmentId);
    }

    // OFF-08: task-level skip audit — actor and reason must be carried through, and the plan's
    // aggregate OffboardingPlanCompletedAuditEvent must not fire since a skipped mandatory task
    // still blocks plan completion.
    [Fact]
    public async Task ExecuteAsync_Skip_Publishes_OffboardingTaskSkippedAuditEvent_With_Reason_And_Actor()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 7, 1), null, seedAt);
        plan.Start(seedAt);
        dbContext.OffboardingPlans.Add(plan);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Conduct exit interview");
        await dbContext.SaveChangesAsync();

        var (action, _, _, auditPublisher, _) = BuildAction(dbContext);
        var actorId = Guid.NewGuid();
        var context = BuildTaskContext(companyId, task.Id, outcomeDecision: "Skip", outcomeReason: "Not required.")
            with
        { CompletedBy = actorId };

        await action.ExecuteAsync(context, CancellationToken.None);

        var skippedEvent = Assert.Single(
            auditPublisher.Published.OfType<HR.Modules.Offboarding.OffboardingTaskSkippedAuditEvent>());
        Assert.Equal(task.Id, skippedEvent.OffboardingTaskId);
        Assert.Equal(plan.Id, skippedEvent.OffboardingPlanId);
        Assert.Equal(employeeId, skippedEvent.EmployeeId);
        Assert.Equal(actorId, skippedEvent.ActorEmployeeId);
        Assert.Equal("Not required.", skippedEvent.SkipReason);
    }

    // OFF-08: plan-completed event's actor must be whoever completed the final task, not the
    // affected (departing) employee.
    [Fact]
    public async Task ExecuteAsync_Completing_Final_Task_Publishes_PlanCompleted_With_Completer_As_Actor()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var seedAt = Now.AddDays(-1);
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 7, 1), null, seedAt);
        plan.Start(seedAt);
        dbContext.OffboardingPlans.Add(plan);
        var task = SeedTask(dbContext, companyId, plan.Id, seedAt, title: "Only task");
        await dbContext.SaveChangesAsync();

        var (action, _, _, auditPublisher, _) = BuildAction(dbContext);
        var actorId = Guid.NewGuid();
        var context = BuildTaskContext(companyId, task.Id) with { CompletedBy = actorId };

        await action.ExecuteAsync(context, CancellationToken.None);

        var planCompleted = Assert.Single(
            auditPublisher.Published.OfType<HR.Modules.Offboarding.OffboardingPlanCompletedAuditEvent>());
        Assert.Equal(actorId, planCompleted.ActorEmployeeId);
        Assert.NotEqual(employeeId, planCompleted.ActorEmployeeId);
    }
}
