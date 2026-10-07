using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Modules.Offboarding.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Offboarding.Tests;

public class OffboardingManagerTaskAssigneeReconcilerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static OffboardingManagerTaskAssigneeReconciler Build(
        OffboardingDbContext dbContext, Guid? managerId, FakeTaskReassigner reassigner) =>
        new(dbContext, new FakeManagerReader(managerId), reassigner, new FakeClock(FixedUtcNow),
            NullLogger<OffboardingManagerTaskAssigneeReconciler>.Instance);

    private static (OffboardingPlan Plan, OffboardingTask ManagerTask, OffboardingTask EmployeeTask, OffboardingTask HrTask)
        AddPlan(OffboardingDbContext dbContext, Guid companyId, Guid employeeId, Guid? taskAssignee, bool synced)
    {
        var plan = OffboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 11, 1), null, Now);
        plan.Start(Now);
        dbContext.OffboardingPlans.Add(plan);

        var managerTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Conduct exit interview", null,
            OffboardingTaskAssignTo.Manager, new DateOnly(2026, 11, 1), Now, assignedEmployeeId: taskAssignee);
        var employeeTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return asset", null,
            OffboardingTaskAssignTo.Employee, new DateOnly(2026, 11, 1), Now, assignedEmployeeId: employeeId);
        var hrTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Review documents", null,
            OffboardingTaskAssignTo.HR, new DateOnly(2026, 11, 1), Now);

        if (synced)
        {
            managerTask.MarkTaskItemCreated(Now);
            employeeTask.MarkTaskItemCreated(Now);
            hrTask.MarkTaskItemCreated(Now);
        }

        dbContext.OffboardingTasks.AddRange(managerTask, employeeTask, hrTask);
        return (plan, managerTask, employeeTask, hrTask);
    }

    [Fact]
    public async Task ReconcileEmployeeAsync_Assigns_Manager_Tasks_That_Were_Created_Without_An_Assignee()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var (_, managerTask, employeeTask, hrTask) = AddPlan(dbContext, companyId, employeeId, null, synced: true);
        await dbContext.SaveChangesAsync();
        var reassigner = new FakeTaskReassigner();

        await Build(dbContext, managerId, reassigner).ReconcileEmployeeAsync(companyId, employeeId, CancellationToken.None);

        Assert.Equal(managerId, (await dbContext.OffboardingTasks.SingleAsync(t => t.Id == managerTask.Id)).AssignedEmployeeId);
        Assert.Equal(employeeId, (await dbContext.OffboardingTasks.SingleAsync(t => t.Id == employeeTask.Id)).AssignedEmployeeId);
        Assert.Null((await dbContext.OffboardingTasks.SingleAsync(t => t.Id == hrTask.Id)).AssignedEmployeeId);

        var call = Assert.Single(reassigner.SourceCalls);
        Assert.Equal(managerId, call.ToEmployeeId);
        Assert.Equal([managerTask.Id], call.SourceEntityIds);
    }

    [Fact]
    public async Task ReconcileEmployeeAsync_Repoints_Manager_Tasks_To_The_Current_Manager_When_It_Has_Changed()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var oldManagerId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        var (_, managerTask, _, _) = AddPlan(dbContext, companyId, employeeId, oldManagerId, synced: true);
        await dbContext.SaveChangesAsync();

        await Build(dbContext, newManagerId, new FakeTaskReassigner())
            .ReconcileEmployeeAsync(companyId, employeeId, CancellationToken.None);

        Assert.Equal(newManagerId, (await dbContext.OffboardingTasks.SingleAsync(t => t.Id == managerTask.Id)).AssignedEmployeeId);
    }

    [Fact]
    public async Task ReconcileEmployeeAsync_Still_Repairs_The_Tasks_Module_When_The_Local_Assignee_Is_Already_Correct()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var (_, managerTask, _, _) = AddPlan(dbContext, companyId, employeeId, managerId, synced: true);
        await dbContext.SaveChangesAsync();
        var reassigner = new FakeTaskReassigner();

        await Build(dbContext, managerId, reassigner).ReconcileEmployeeAsync(companyId, employeeId, CancellationToken.None);

        var call = Assert.Single(reassigner.SourceCalls);
        Assert.Equal([managerTask.Id], call.SourceEntityIds);
    }

    [Fact]
    public async Task ReconcileEmployeeAsync_Does_Not_Touch_The_Tasks_Module_For_Tasks_That_Were_Never_Synced()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var (_, managerTask, _, _) = AddPlan(dbContext, companyId, employeeId, null, synced: false);
        await dbContext.SaveChangesAsync();
        var reassigner = new FakeTaskReassigner();

        await Build(dbContext, managerId, reassigner).ReconcileEmployeeAsync(companyId, employeeId, CancellationToken.None);

        Assert.Equal(managerId, (await dbContext.OffboardingTasks.SingleAsync(t => t.Id == managerTask.Id)).AssignedEmployeeId);
        Assert.Empty(Assert.Single(reassigner.SourceCalls).SourceEntityIds);
    }

    [Fact]
    public async Task ReconcileEmployeeAsync_Leaves_Everything_Alone_When_The_Employee_Has_No_Manager()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var previousManagerId = Guid.NewGuid();
        var (_, managerTask, _, _) = AddPlan(dbContext, companyId, employeeId, previousManagerId, synced: true);
        await dbContext.SaveChangesAsync();
        var reassigner = new FakeTaskReassigner();

        await Build(dbContext, null, reassigner).ReconcileEmployeeAsync(companyId, employeeId, CancellationToken.None);

        Assert.Equal(previousManagerId, (await dbContext.OffboardingTasks.SingleAsync(t => t.Id == managerTask.Id)).AssignedEmployeeId);
        Assert.Empty(reassigner.SourceCalls);
    }

    [Fact]
    public async Task ReconcileEmployeeAsync_Ignores_Resolved_Manager_Tasks_And_Cancelled_Plans()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var (plan, managerTask, _, _) = AddPlan(dbContext, companyId, employeeId, null, synced: true);
        managerTask.Complete(Now);
        await dbContext.SaveChangesAsync();
        var reassigner = new FakeTaskReassigner();

        await Build(dbContext, managerId, reassigner).ReconcileEmployeeAsync(companyId, employeeId, CancellationToken.None);

        Assert.Null((await dbContext.OffboardingTasks.SingleAsync(t => t.Id == managerTask.Id)).AssignedEmployeeId);
        Assert.Empty(reassigner.SourceCalls);

        plan.Cancel("withdrawn", Now);
        await dbContext.SaveChangesAsync();

        await Build(dbContext, managerId, reassigner).ReconcileEmployeeAsync(companyId, employeeId, CancellationToken.None);

        Assert.Empty(reassigner.SourceCalls);
    }

    [Fact]
    public async Task ReconcileAllActivePlansAsync_Repairs_Every_Active_Plan()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var (_, first, _, _) = AddPlan(dbContext, companyId, Guid.NewGuid(), null, synced: true);
        var (_, second, _, _) = AddPlan(dbContext, companyId, Guid.NewGuid(), null, synced: true);
        await dbContext.SaveChangesAsync();
        var reassigner = new FakeTaskReassigner();

        await Build(dbContext, managerId, reassigner).ReconcileAllActivePlansAsync(CancellationToken.None);

        Assert.Equal(managerId, (await dbContext.OffboardingTasks.SingleAsync(t => t.Id == first.Id)).AssignedEmployeeId);
        Assert.Equal(managerId, (await dbContext.OffboardingTasks.SingleAsync(t => t.Id == second.Id)).AssignedEmployeeId);
        Assert.Equal(2, reassigner.SourceCalls.Count);
    }
}
