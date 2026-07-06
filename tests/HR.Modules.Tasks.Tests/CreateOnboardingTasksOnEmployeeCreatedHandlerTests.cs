using HR.Modules.Tasks.Features.CreateOnboardingTasksOnEmployeeCreated;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Tests;

public class CreateOnboardingTasksOnEmployeeCreatedHandlerTests
{
    private static readonly Guid CompanyId  = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ManagerId  = Guid.NewGuid();

    private static EmployeeCreatedIntegrationEvent MakeEvent(Guid? managerId = null) =>
        new(CompanyId, EmployeeId, new DateOnly(2026, 8, 1), managerId, new DateOnly(2026, 11, 1));

    [Fact]
    public async Task HandleAsync_Creates_Three_Onboarding_Tasks()
    {
        var taskCreator = new FakeTaskCreator();
        var nameReader  = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [EmployeeId] = "Priya Sharma" });
        var handler     = new EmployeeCreatedHandler(taskCreator, nameReader);

        await handler.HandleAsync(MakeEvent(ManagerId), CancellationToken.None);

        Assert.Equal(3, taskCreator.Created.Count);
        Assert.All(taskCreator.Created, t => Assert.Equal(TaskSource.Onboarding, t.Source));
        Assert.All(taskCreator.Created, t => Assert.Contains("Priya Sharma", t.Title));
    }

    [Fact]
    public async Task HandleAsync_Assigns_Workstation_Task_Unassigned()
    {
        var taskCreator = new FakeTaskCreator();
        var handler     = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader());

        await handler.HandleAsync(MakeEvent(ManagerId), CancellationToken.None);

        var workstationTask = taskCreator.Created.Single(t => t.Title.StartsWith("Set up workstation"));
        Assert.Null(workstationTask.AssignedEmployeeId);
        Assert.Equal(TaskPriority.High, workstationTask.Priority);
    }

    [Fact]
    public async Task HandleAsync_Assigns_Welcome_And_Induction_Tasks_To_Manager()
    {
        var taskCreator = new FakeTaskCreator();
        var handler     = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader());

        await handler.HandleAsync(MakeEvent(ManagerId), CancellationToken.None);

        var welcomeTask   = taskCreator.Created.Single(t => t.Title.StartsWith("Send welcome email"));
        var inductionTask = taskCreator.Created.Single(t => t.Title.StartsWith("Schedule welcome and induction"));

        Assert.Equal(ManagerId, welcomeTask.AssignedEmployeeId);
        Assert.Equal(ManagerId, inductionTask.AssignedEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Assigns_Nothing_When_No_Manager()
    {
        var taskCreator = new FakeTaskCreator();
        var handler     = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader());

        await handler.HandleAsync(MakeEvent(managerId: null), CancellationToken.None);

        Assert.All(taskCreator.Created, t => Assert.Null(t.AssignedEmployeeId));
    }

    [Fact]
    public async Task HandleAsync_Uses_StartDate_As_Due_Date_For_Workstation_And_Welcome_Tasks()
    {
        var taskCreator = new FakeTaskCreator();
        var handler     = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader());
        var evt = MakeEvent(ManagerId);

        await handler.HandleAsync(evt, CancellationToken.None);

        var workstationTask = taskCreator.Created.Single(t => t.Title.StartsWith("Set up workstation"));
        var welcomeTask     = taskCreator.Created.Single(t => t.Title.StartsWith("Send welcome email"));
        var inductionTask   = taskCreator.Created.Single(t => t.Title.StartsWith("Schedule welcome and induction"));

        Assert.Equal(evt.StartDate, workstationTask.DueDate);
        Assert.Equal(evt.StartDate, welcomeTask.DueDate);
        Assert.Equal(evt.StartDate.AddDays(7), inductionTask.DueDate);
    }
}
