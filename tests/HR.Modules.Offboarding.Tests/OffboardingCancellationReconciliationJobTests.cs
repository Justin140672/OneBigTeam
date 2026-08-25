using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Jobs;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Offboarding.Tests;

public class OffboardingCancellationReconciliationJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 25, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static OffboardingCancellationReconciliationJob BuildJob(
        OffboardingDbContext dbContext, FakeOffboardingPlanCoordinator coordinator) =>
        new(dbContext, coordinator, NullLogger<OffboardingCancellationReconciliationJob>.Instance);

    private static OffboardingPlan CreatePlan(
        Guid companyId, Guid employeeId, OffboardingStatus status)
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 8, 1), null, Now.AddDays(-10));
        plan.Start(Now.AddDays(-10));

        if (status == OffboardingStatus.Cancelled)
            plan.Cancel("Cancelled for test.", Now.AddDays(-1));
        else if (status == OffboardingStatus.Completed)
            plan.Complete(Now.AddDays(-1));

        return plan;
    }

    [Fact]
    public async Task ExecuteAsync_Reconciles_Cancelled_Plan()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        dbContext.OffboardingPlans.Add(CreatePlan(companyId, employeeId, OffboardingStatus.Cancelled));
        await dbContext.SaveChangesAsync();

        var coordinator = new FakeOffboardingPlanCoordinator();
        var job = BuildJob(dbContext, coordinator);

        await job.ExecuteAsync();

        var call = Assert.Single(coordinator.CancelOutstandingTasksCalls);
        Assert.Equal(companyId, call.CompanyId);
        Assert.Equal(employeeId, call.EmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Touch_Plan_That_Is_Not_Cancelled()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        dbContext.OffboardingPlans.Add(CreatePlan(companyId, employeeId, OffboardingStatus.InProgress));
        await dbContext.SaveChangesAsync();

        var coordinator = new FakeOffboardingPlanCoordinator();
        var job = BuildJob(dbContext, coordinator);

        await job.ExecuteAsync();

        Assert.Empty(coordinator.CancelOutstandingTasksCalls);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Touch_Completed_Plan()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        dbContext.OffboardingPlans.Add(CreatePlan(companyId, employeeId, OffboardingStatus.Completed));
        await dbContext.SaveChangesAsync();

        var coordinator = new FakeOffboardingPlanCoordinator();
        var job = BuildJob(dbContext, coordinator);

        await job.ExecuteAsync();

        Assert.Empty(coordinator.CancelOutstandingTasksCalls);
    }

    [Fact]
    public async Task ExecuteAsync_Processes_Multiple_Distinct_Employees_Independently()
    {
        await using var dbContext = BuildContext();
        var companyIdA = Guid.NewGuid();
        var employeeIdA = Guid.NewGuid();
        var companyIdB = Guid.NewGuid();
        var employeeIdB = Guid.NewGuid();

        dbContext.OffboardingPlans.Add(CreatePlan(companyIdA, employeeIdA, OffboardingStatus.Cancelled));
        dbContext.OffboardingPlans.Add(CreatePlan(companyIdB, employeeIdB, OffboardingStatus.Cancelled));
        await dbContext.SaveChangesAsync();

        var coordinator = new FakeOffboardingPlanCoordinator();
        var job = BuildJob(dbContext, coordinator);

        await job.ExecuteAsync();

        Assert.Equal(2, coordinator.CancelOutstandingTasksCalls.Count);
        Assert.Contains(coordinator.CancelOutstandingTasksCalls, c => c.CompanyId == companyIdA && c.EmployeeId == employeeIdA);
        Assert.Contains(coordinator.CancelOutstandingTasksCalls, c => c.CompanyId == companyIdB && c.EmployeeId == employeeIdB);
    }

    [Fact]
    public async Task ExecuteAsync_One_Employees_Failure_Does_Not_Stop_Others_From_Being_Processed()
    {
        await using var dbContext = BuildContext();
        var companyIdA = Guid.NewGuid();
        var employeeIdA = Guid.NewGuid();
        var companyIdB = Guid.NewGuid();
        var employeeIdB = Guid.NewGuid();

        dbContext.OffboardingPlans.Add(CreatePlan(companyIdA, employeeIdA, OffboardingStatus.Cancelled));
        dbContext.OffboardingPlans.Add(CreatePlan(companyIdB, employeeIdB, OffboardingStatus.Cancelled));
        await dbContext.SaveChangesAsync();

        var coordinator = new FakeOffboardingPlanCoordinator();
        coordinator.EmployeeIdsThatThrow.Add(employeeIdA);
        var job = BuildJob(dbContext, coordinator);

        await job.ExecuteAsync();

        Assert.Equal(2, coordinator.CancelOutstandingTasksCalls.Count);
        Assert.Contains(coordinator.CancelOutstandingTasksCalls, c => c.EmployeeId == employeeIdB);
    }
}
