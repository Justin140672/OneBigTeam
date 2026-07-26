using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.StartOffboarding;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Modules.Offboarding.Tests.Infrastructure;
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
            new FakeTaskCreator(),
            new FakeNotificationWriter(),
            new NoOpIntegrationEventPublisher());

    private static OffboardingPlanCoordinator BuildCoordinator(
        OffboardingDbContext dbContext, FakeAuditPublisher? auditPublisher = null) =>
        new(
            BuildUnusedStartOffboardingHandler(dbContext),
            dbContext,
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
}
