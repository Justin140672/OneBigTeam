using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.StartOffboarding;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Modules.Offboarding.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Offboarding.Tests;

public class OffboardingPlanCoordinatorTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    // CancelOutstandingTasksAsync never touches StartOffboardingHandler — it's only a constructor
    // dependency of OffboardingPlanCoordinator because StartAsync (the sibling method) delegates to
    // it. Any validly-constructed instance works here since this test path never calls it.
    private static StartOffboardingHandler BuildUnusedStartOffboardingHandler(OffboardingDbContext dbContext) =>
        new(
            dbContext,
            new FakeClock(FixedUtcNow),
            new FakeEmployeeNameReader(),
            new FakeManagerReader(null),
            new FakeAssignedAssetReader(),
            new FakeOutstandingDocumentRequestReader(),
            new OffboardingTaskSynchronizer(
                dbContext, new FakeTaskCreator(), new FakeClock(FixedUtcNow),
                NullLogger<OffboardingTaskSynchronizer>.Instance),
            new FakeNotificationWriter(),
            new NoOpIntegrationEventPublisher(),
            new FakeCompanyLeavingSettingsReader(),
            new FakeHrAdministratorDirectory());

    private static OffboardingPlanCoordinator BuildCoordinator(
        OffboardingDbContext dbContext,
        FakeAuditPublisher? auditPublisher = null,
        FakeTaskCanceller? taskCanceller = null,
        FakeTaskRescheduler? taskRescheduler = null) =>
        new(
            BuildUnusedStartOffboardingHandler(dbContext),
            dbContext,
            taskCanceller ?? new FakeTaskCanceller(),
            taskRescheduler ?? new FakeTaskRescheduler(),
            new FakeClock(FixedUtcNow),
            auditPublisher ?? new FakeAuditPublisher(),
            NullLogger<OffboardingPlanCoordinator>.Instance);

    private static OffboardingPlan CreateActivePlan(Guid companyId, Guid employeeId, DateTimeOffset createdAt)
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 8, 1), null, createdAt);
        plan.Start(createdAt);
        return plan;
    }

    [Fact]
    public async Task CancelOutstandingTasksAsync_Skips_Outstanding_Tasks_And_Leaves_Completed_Tasks_Alone()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = CreateActivePlan(companyId, employeeId, Now.AddDays(-5));
        dbContext.OffboardingPlans.Add(plan);

        var pendingTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null, OffboardingTaskAssignTo.Employee, null, Now.AddDays(-5));
        var completedTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Conduct exit interview", null, OffboardingTaskAssignTo.Manager, null, Now.AddDays(-5));
        completedTask.Complete(Now.AddDays(-3));
        dbContext.OffboardingTasks.AddRange(pendingTask, completedTask);
        await dbContext.SaveChangesAsync();

        var coordinator = BuildCoordinator(dbContext);

        await coordinator.CancelOutstandingTasksAsync(companyId, employeeId, CancellationToken.None);

        var savedPendingTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == pendingTask.Id);
        Assert.Equal(OffboardingTaskStatus.Skipped, savedPendingTask.Status);

        var savedCompletedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == completedTask.Id);
        Assert.Equal(OffboardingTaskStatus.Completed, savedCompletedTask.Status);
    }

    [Fact]
    public async Task CancelOutstandingTasksAsync_Cancels_The_Plan()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = CreateActivePlan(companyId, employeeId, Now.AddDays(-5));
        dbContext.OffboardingPlans.Add(plan);
        await dbContext.SaveChangesAsync();

        var coordinator = BuildCoordinator(dbContext);

        await coordinator.CancelOutstandingTasksAsync(companyId, employeeId, CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(OffboardingStatus.Cancelled, savedPlan.Status);
    }

    [Fact]
    public async Task CancelOutstandingTasksAsync_Publishes_OffboardingPlanCancelledAuditEvent_With_Correct_Count()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = CreateActivePlan(companyId, employeeId, Now.AddDays(-5));
        dbContext.OffboardingPlans.Add(plan);

        var pendingTask1 = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null, OffboardingTaskAssignTo.Employee, null, Now.AddDays(-5));
        var pendingTask2 = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Revoke system access", null, OffboardingTaskAssignTo.Manager, null, Now.AddDays(-5));
        var completedTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Conduct exit interview", null, OffboardingTaskAssignTo.Manager, null, Now.AddDays(-5));
        completedTask.Complete(Now.AddDays(-3));
        dbContext.OffboardingTasks.AddRange(pendingTask1, pendingTask2, completedTask);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var coordinator = BuildCoordinator(dbContext, auditPublisher);

        await coordinator.CancelOutstandingTasksAsync(companyId, employeeId, CancellationToken.None);

        var auditEvent = Assert.IsType<OffboardingPlanCancelledAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(plan.Id, auditEvent.OffboardingPlanId);
        Assert.Equal(2, auditEvent.OutstandingTasksCancelled);
    }

    [Fact]
    public async Task CancelOutstandingTasksAsync_Skipped_Tasks_Already_Skipped_Are_Not_Recounted()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = CreateActivePlan(companyId, employeeId, Now.AddDays(-5));
        dbContext.OffboardingPlans.Add(plan);

        var alreadySkippedTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null, OffboardingTaskAssignTo.Employee, null, Now.AddDays(-5));
        alreadySkippedTask.Skip(Now.AddDays(-2));
        dbContext.OffboardingTasks.Add(alreadySkippedTask);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var coordinator = BuildCoordinator(dbContext, auditPublisher);

        await coordinator.CancelOutstandingTasksAsync(companyId, employeeId, CancellationToken.None);

        var auditEvent = Assert.IsType<OffboardingPlanCancelledAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(0, auditEvent.OutstandingTasksCancelled);
    }

    [Fact]
    public async Task CancelOutstandingTasksAsync_Is_NoOp_When_No_Active_Plan_Exists()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var auditPublisher = new FakeAuditPublisher();
        var coordinator = BuildCoordinator(dbContext, auditPublisher);

        await coordinator.CancelOutstandingTasksAsync(companyId, employeeId, CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task CancelOutstandingTasksAsync_Is_NoOp_When_Only_Plan_Is_Already_Terminal()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var completedPlan = CreateActivePlan(companyId, employeeId, Now.AddDays(-30));
        completedPlan.Complete(Now.AddDays(-10));
        dbContext.OffboardingPlans.Add(completedPlan);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var coordinator = BuildCoordinator(dbContext, auditPublisher);

        await coordinator.CancelOutstandingTasksAsync(companyId, employeeId, CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == completedPlan.Id);
        Assert.Equal(OffboardingStatus.Completed, savedPlan.Status);
    }

    // OFF-01: the cross-module Tasks-module sync is the entire point of this method — previously
    // only the local OffboardingTask rows were marked Skipped, leaving the real Tasks-module
    // TaskItems dangling Open. This pins that CancelManyBySourceEntitiesAsync is actually invoked
    // with the plan's own OffboardingTask ids and the correct source/action-type filter.
    [Fact]
    public async Task CancelOutstandingTasksAsync_Invokes_TaskCanceller_With_Plans_OffboardingTask_Ids()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = CreateActivePlan(companyId, employeeId, Now.AddDays(-5));
        dbContext.OffboardingPlans.Add(plan);

        var pendingTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null, OffboardingTaskAssignTo.Employee, null, Now.AddDays(-5));
        var completedTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Conduct exit interview", null, OffboardingTaskAssignTo.Manager, null, Now.AddDays(-5));
        completedTask.Complete(Now.AddDays(-3));
        dbContext.OffboardingTasks.AddRange(pendingTask, completedTask);
        await dbContext.SaveChangesAsync();

        var taskCanceller = new FakeTaskCanceller();
        var coordinator = BuildCoordinator(dbContext, taskCanceller: taskCanceller);

        await coordinator.CancelOutstandingTasksAsync(companyId, employeeId, CancellationToken.None);

        var call = Assert.Single(taskCanceller.CancelManyCalls);
        Assert.Equal(companyId, call.CompanyId);
        Assert.Equal(TaskSource.Offboarding, call.Source);
        Assert.Equal(TaskActionType.Complete, call.ActionType);
        Assert.Equal(
            new[] { pendingTask.Id, completedTask.Id }.OrderBy(id => id),
            call.SourceEntityIds.OrderBy(id => id));
    }

    // OFF-01: calling a second time on an already-Cancelled plan must still re-run the
    // Tasks-module sync (this is what makes the method safe as a reconciliation retry after a
    // partial failure) but must NOT publish a second audit event for the same cancellation.
    [Fact]
    public async Task CancelOutstandingTasksAsync_Called_Twice_ReInvokes_TaskCanceller_But_Does_Not_Duplicate_Audit_Event()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = CreateActivePlan(companyId, employeeId, Now.AddDays(-5));
        dbContext.OffboardingPlans.Add(plan);
        var pendingTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null, OffboardingTaskAssignTo.Employee, null, Now.AddDays(-5));
        dbContext.OffboardingTasks.Add(pendingTask);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var taskCanceller = new FakeTaskCanceller();
        var coordinator = BuildCoordinator(dbContext, auditPublisher, taskCanceller);

        await coordinator.CancelOutstandingTasksAsync(companyId, employeeId, CancellationToken.None);
        await coordinator.CancelOutstandingTasksAsync(companyId, employeeId, CancellationToken.None);

        Assert.Single(auditPublisher.Published);
        Assert.Equal(2, taskCanceller.CancelManyCalls.Count);
    }

    // OFF-01: a Completed plan is a terminal state that must never be touched — including never
    // even attempting the Tasks-module sync, since a completed offboarding plan's tasks are all
    // already resolved and must not be reopened/cancelled retroactively.
    [Fact]
    public async Task CancelOutstandingTasksAsync_Does_Not_Invoke_TaskCanceller_When_Plan_Already_Completed()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var completedPlan = CreateActivePlan(companyId, employeeId, Now.AddDays(-30));
        completedPlan.Complete(Now.AddDays(-10));
        dbContext.OffboardingPlans.Add(completedPlan);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var taskCanceller = new FakeTaskCanceller();
        var coordinator = BuildCoordinator(dbContext, auditPublisher, taskCanceller);

        await coordinator.CancelOutstandingTasksAsync(companyId, employeeId, CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
        Assert.Empty(taskCanceller.CancelManyCalls);
    }

    // OFF-02: RescheduleOutstandingTasksAsync tests below.

    [Fact]
    public async Task RescheduleOutstandingTasksAsync_Is_NoOp_When_No_Plan_Exists()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var auditPublisher = new FakeAuditPublisher();
        var taskRescheduler = new FakeTaskRescheduler();
        var coordinator = BuildCoordinator(dbContext, auditPublisher, taskRescheduler: taskRescheduler);

        await coordinator.RescheduleOutstandingTasksAsync(
            companyId, employeeId, new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
        Assert.Empty(taskRescheduler.RescheduleManyCalls);
    }

    [Fact]
    public async Task RescheduleOutstandingTasksAsync_Is_NoOp_When_Plan_Already_Completed()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var completedPlan = CreateActivePlan(companyId, employeeId, Now.AddDays(-30));
        completedPlan.Complete(Now.AddDays(-10));
        dbContext.OffboardingPlans.Add(completedPlan);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var taskRescheduler = new FakeTaskRescheduler();
        var coordinator = BuildCoordinator(dbContext, auditPublisher, taskRescheduler: taskRescheduler);

        await coordinator.RescheduleOutstandingTasksAsync(
            companyId, employeeId, new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
        Assert.Empty(taskRescheduler.RescheduleManyCalls);
        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == completedPlan.Id);
        Assert.Equal(new DateOnly(2026, 8, 1), savedPlan.LastWorkingDay);
    }

    [Fact]
    public async Task RescheduleOutstandingTasksAsync_Is_NoOp_When_Plan_Already_Cancelled()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var cancelledPlan = CreateActivePlan(companyId, employeeId, Now.AddDays(-30));
        cancelledPlan.Cancel("Withdrawn.", Now.AddDays(-10));
        dbContext.OffboardingPlans.Add(cancelledPlan);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var taskRescheduler = new FakeTaskRescheduler();
        var coordinator = BuildCoordinator(dbContext, auditPublisher, taskRescheduler: taskRescheduler);

        await coordinator.RescheduleOutstandingTasksAsync(
            companyId, employeeId, new DateOnly(2026, 8, 15), CancellationToken.None);

        Assert.Empty(auditPublisher.Published);
        Assert.Empty(taskRescheduler.RescheduleManyCalls);
        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == cancelledPlan.Id);
        Assert.Equal(new DateOnly(2026, 8, 1), savedPlan.LastWorkingDay);
    }

    [Fact]
    public async Task RescheduleOutstandingTasksAsync_Moves_LastWorkingDay_And_Outstanding_Tasks_Later()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = CreateActivePlan(companyId, employeeId, Now.AddDays(-5));
        dbContext.OffboardingPlans.Add(plan);

        var pendingTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null,
            OffboardingTaskAssignTo.Employee, new DateOnly(2026, 8, 1), Now.AddDays(-5));
        var completedTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Conduct exit interview", null,
            OffboardingTaskAssignTo.Manager, new DateOnly(2026, 8, 1), Now.AddDays(-5));
        completedTask.Complete(Now.AddDays(-3));
        var skippedTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Revoke system access", null,
            OffboardingTaskAssignTo.Manager, new DateOnly(2026, 8, 1), Now.AddDays(-5));
        skippedTask.Skip(Now.AddDays(-2));
        dbContext.OffboardingTasks.AddRange(pendingTask, completedTask, skippedTask);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var taskRescheduler = new FakeTaskRescheduler();
        var coordinator = BuildCoordinator(dbContext, auditPublisher, taskRescheduler: taskRescheduler);

        var newLastWorkingDay = new DateOnly(2026, 8, 20);

        await coordinator.RescheduleOutstandingTasksAsync(
            companyId, employeeId, newLastWorkingDay, CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(newLastWorkingDay, savedPlan.LastWorkingDay);

        var savedPendingTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == pendingTask.Id);
        Assert.Equal(newLastWorkingDay, savedPendingTask.DueDate);

        var savedCompletedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == completedTask.Id);
        Assert.Equal(new DateOnly(2026, 8, 1), savedCompletedTask.DueDate);

        var savedSkippedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == skippedTask.Id);
        Assert.Equal(new DateOnly(2026, 8, 1), savedSkippedTask.DueDate);

        var auditEvent = Assert.IsType<OffboardingPlanRescheduledAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(plan.Id, auditEvent.OffboardingPlanId);
        Assert.Equal(new DateOnly(2026, 8, 1), auditEvent.BeforeLastWorkingDay);
        Assert.Equal(newLastWorkingDay, auditEvent.AfterLastWorkingDay);
        Assert.Equal(1, auditEvent.OutstandingTasksRescheduled);

        var call = Assert.Single(taskRescheduler.RescheduleManyCalls);
        Assert.Equal(companyId, call.CompanyId);
        Assert.Equal(TaskSource.Offboarding, call.Source);
        Assert.Equal(TaskActionType.Complete, call.ActionType);
        Assert.Equal(newLastWorkingDay, call.NewDueDate);
        Assert.Equal(pendingTask.Id, Assert.Single(call.SourceEntityIds));
    }

    [Fact]
    public async Task RescheduleOutstandingTasksAsync_Moves_LastWorkingDay_And_Outstanding_Tasks_Earlier()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = CreateActivePlan(companyId, employeeId, Now.AddDays(-5));
        dbContext.OffboardingPlans.Add(plan);

        var pendingTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null,
            OffboardingTaskAssignTo.Employee, new DateOnly(2026, 8, 1), Now.AddDays(-5));
        dbContext.OffboardingTasks.Add(pendingTask);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var taskRescheduler = new FakeTaskRescheduler();
        var coordinator = BuildCoordinator(dbContext, auditPublisher, taskRescheduler: taskRescheduler);

        var newLastWorkingDay = new DateOnly(2026, 7, 10);

        await coordinator.RescheduleOutstandingTasksAsync(
            companyId, employeeId, newLastWorkingDay, CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(newLastWorkingDay, savedPlan.LastWorkingDay);

        var savedPendingTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == pendingTask.Id);
        Assert.Equal(newLastWorkingDay, savedPendingTask.DueDate);

        var auditEvent = Assert.IsType<OffboardingPlanRescheduledAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal(new DateOnly(2026, 8, 1), auditEvent.BeforeLastWorkingDay);
        Assert.Equal(newLastWorkingDay, auditEvent.AfterLastWorkingDay);

        var call = Assert.Single(taskRescheduler.RescheduleManyCalls);
        Assert.Equal(newLastWorkingDay, call.NewDueDate);
    }

    // OFF-02: there is no confirmation step in Offboarding — Employees' AmendLeavingProcess handler
    // already gates a backdated leaving date on ConfirmBackdatedLeavingDate before this event is even
    // published, so the coordinator must just process whatever (possibly past) date arrives.
    [Fact]
    public async Task RescheduleOutstandingTasksAsync_Processes_Backdated_LastWorkingDay_Without_SpecialCasing()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = CreateActivePlan(companyId, employeeId, Now.AddDays(-30));
        dbContext.OffboardingPlans.Add(plan);
        var pendingTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null,
            OffboardingTaskAssignTo.Employee, new DateOnly(2026, 8, 1), Now.AddDays(-30));
        dbContext.OffboardingTasks.Add(pendingTask);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var coordinator = BuildCoordinator(dbContext, auditPublisher);

        // FixedUtcNow is 2026-07-24, so this date is in the past relative to "now".
        var backdatedLastWorkingDay = new DateOnly(2026, 7, 1);

        await coordinator.RescheduleOutstandingTasksAsync(
            companyId, employeeId, backdatedLastWorkingDay, CancellationToken.None);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(backdatedLastWorkingDay, savedPlan.LastWorkingDay);

        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == pendingTask.Id);
        Assert.Equal(backdatedLastWorkingDay, savedTask.DueDate);

        Assert.Single(auditPublisher.Published);
    }

    [Fact]
    public async Task RescheduleOutstandingTasksAsync_Repeated_Call_With_Same_Date_Publishes_No_Additional_Audit_Event_But_Still_ReInvokes_TaskRescheduler()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var plan = CreateActivePlan(companyId, employeeId, Now.AddDays(-5));
        dbContext.OffboardingPlans.Add(plan);
        var pendingTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null,
            OffboardingTaskAssignTo.Employee, new DateOnly(2026, 8, 1), Now.AddDays(-5));
        dbContext.OffboardingTasks.Add(pendingTask);
        await dbContext.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var taskRescheduler = new FakeTaskRescheduler();
        var coordinator = BuildCoordinator(dbContext, auditPublisher, taskRescheduler: taskRescheduler);

        var newLastWorkingDay = new DateOnly(2026, 8, 20);

        await coordinator.RescheduleOutstandingTasksAsync(
            companyId, employeeId, newLastWorkingDay, CancellationToken.None);
        await coordinator.RescheduleOutstandingTasksAsync(
            companyId, employeeId, newLastWorkingDay, CancellationToken.None);

        Assert.Single(auditPublisher.Published);
        Assert.Equal(2, taskRescheduler.RescheduleManyCalls.Count);

        var savedPlan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(newLastWorkingDay, savedPlan.LastWorkingDay);
        var savedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == pendingTask.Id);
        Assert.Equal(newLastWorkingDay, savedTask.DueDate);
    }
}
