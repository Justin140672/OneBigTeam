using HR.Modules.Employees.Contracts;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.ReassignOffboardingTasksOnManagerChanged;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Modules.Offboarding.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Offboarding.Tests;

public class ReassignOffboardingTasksOnManagerChangedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Repoints_Open_Manager_Tasks_Of_The_Employees_Active_Plan_To_The_New_Manager()
    {
        await using var dbContext = new OffboardingDbContext(
            new DbContextOptionsBuilder<OffboardingDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var oldManagerId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();

        var plan = OffboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 11, 1), null, Now);
        plan.Start(Now);
        var task = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Conduct exit interview", null,
            OffboardingTaskAssignTo.Manager, new DateOnly(2026, 11, 1), Now, assignedEmployeeId: oldManagerId);
        dbContext.OffboardingPlans.Add(plan);
        dbContext.OffboardingTasks.Add(task);
        await dbContext.SaveChangesAsync();

        var reassigner = new FakeTaskReassigner();
        var reconciler = new OffboardingManagerTaskAssigneeReconciler(
            dbContext, new FakeManagerReader(newManagerId), reassigner,
            new FakeHrAdministratorDirectory(), new FakeEmployeeNameReader(), new FakeNotificationWriter(),
            new FakeAuditPublisher(), new FakeClock(FixedUtcNow),
            NullLogger<OffboardingManagerTaskAssigneeReconciler>.Instance);
        var handler = new ReassignOffboardingTasksOnManagerChangedHandler(reconciler);

        await handler.HandleAsync(
            new EmployeeManagerChangedIntegrationEvent(companyId, employeeId, oldManagerId, newManagerId, Now),
            CancellationToken.None);

        Assert.Equal(newManagerId, (await dbContext.OffboardingTasks.SingleAsync(t => t.Id == task.Id)).AssignedEmployeeId);
    }
}
