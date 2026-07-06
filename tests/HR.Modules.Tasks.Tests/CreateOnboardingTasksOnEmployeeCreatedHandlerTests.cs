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

    private static EmployeeCreatedIntegrationEvent MakeEvent(Guid? managerId = null, Guid? positionProfileId = null) =>
        new(CompanyId, EmployeeId, new DateOnly(2026, 8, 1), managerId, new DateOnly(2026, 11, 1), positionProfileId);

    // ── Fallback path (no template linked) — must remain byte-for-byte identical to the ──
    // ── original hardcoded 3-task behaviour, for backward compatibility.                 ──

    [Fact]
    public async Task HandleAsync_Creates_Three_Onboarding_Tasks_When_No_Template_Linked()
    {
        var taskCreator = new FakeTaskCreator();
        var nameReader  = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [EmployeeId] = "Priya Sharma" });
        var handler     = new EmployeeCreatedHandler(taskCreator, nameReader, new FakeOnboardingTemplateReader());

        await handler.HandleAsync(MakeEvent(ManagerId), CancellationToken.None);

        Assert.Equal(3, taskCreator.Created.Count);
        Assert.All(taskCreator.Created, t => Assert.Equal(TaskSource.Onboarding, t.Source));
        Assert.All(taskCreator.Created, t => Assert.Contains("Priya Sharma", t.Title));
    }

    [Fact]
    public async Task HandleAsync_Assigns_Workstation_Task_Unassigned_When_No_Template_Linked()
    {
        var taskCreator = new FakeTaskCreator();
        var handler     = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader(), new FakeOnboardingTemplateReader());

        await handler.HandleAsync(MakeEvent(ManagerId), CancellationToken.None);

        var workstationTask = taskCreator.Created.Single(t => t.Title.StartsWith("Set up workstation"));
        Assert.Null(workstationTask.AssignedEmployeeId);
        Assert.Equal(TaskPriority.High, workstationTask.Priority);
    }

    [Fact]
    public async Task HandleAsync_Assigns_Welcome_And_Induction_Tasks_To_Manager_When_No_Template_Linked()
    {
        var taskCreator = new FakeTaskCreator();
        var handler     = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader(), new FakeOnboardingTemplateReader());

        await handler.HandleAsync(MakeEvent(ManagerId), CancellationToken.None);

        var welcomeTask   = taskCreator.Created.Single(t => t.Title.StartsWith("Send welcome email"));
        var inductionTask = taskCreator.Created.Single(t => t.Title.StartsWith("Schedule welcome and induction"));

        Assert.Equal(ManagerId, welcomeTask.AssignedEmployeeId);
        Assert.Equal(ManagerId, inductionTask.AssignedEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Assigns_Nothing_When_No_Manager_And_No_Template_Linked()
    {
        var taskCreator = new FakeTaskCreator();
        var handler     = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader(), new FakeOnboardingTemplateReader());

        await handler.HandleAsync(MakeEvent(managerId: null), CancellationToken.None);

        Assert.All(taskCreator.Created, t => Assert.Null(t.AssignedEmployeeId));
    }

    [Fact]
    public async Task HandleAsync_Uses_StartDate_As_Due_Date_For_Workstation_And_Welcome_Tasks_When_No_Template_Linked()
    {
        var taskCreator = new FakeTaskCreator();
        var handler     = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader(), new FakeOnboardingTemplateReader());
        var evt = MakeEvent(ManagerId);

        await handler.HandleAsync(evt, CancellationToken.None);

        var workstationTask = taskCreator.Created.Single(t => t.Title.StartsWith("Set up workstation"));
        var welcomeTask     = taskCreator.Created.Single(t => t.Title.StartsWith("Send welcome email"));
        var inductionTask   = taskCreator.Created.Single(t => t.Title.StartsWith("Schedule welcome and induction"));

        Assert.Equal(evt.StartDate, workstationTask.DueDate);
        Assert.Equal(evt.StartDate, welcomeTask.DueDate);
        Assert.Equal(evt.StartDate.AddDays(7), inductionTask.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Hardcoded_Tasks_When_Employee_Has_No_PositionProfile()
    {
        var taskCreator = new FakeTaskCreator();
        var handler     = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader(), new FakeOnboardingTemplateReader());

        await handler.HandleAsync(MakeEvent(ManagerId, positionProfileId: null), CancellationToken.None);

        Assert.Equal(3, taskCreator.Created.Count);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Hardcoded_Tasks_When_PositionProfile_Has_No_Template()
    {
        var taskCreator = new FakeTaskCreator();
        var onboardingTemplateReader = new FakeOnboardingTemplateReader(templateIdForPositionProfile: null);
        var handler = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader(), onboardingTemplateReader);

        await handler.HandleAsync(MakeEvent(ManagerId, positionProfileId: Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(3, taskCreator.Created.Count);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Hardcoded_Tasks_When_Template_Has_No_Active_Tasks()
    {
        var taskCreator = new FakeTaskCreator();
        var onboardingTemplateReader = new FakeOnboardingTemplateReader(
            templateIdForPositionProfile: Guid.NewGuid(),
            tasks: []);
        var handler = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader(), onboardingTemplateReader);

        await handler.HandleAsync(MakeEvent(ManagerId, positionProfileId: Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(3, taskCreator.Created.Count);
        Assert.Contains(taskCreator.Created, t => t.Title.StartsWith("Set up workstation"));
    }

    // ── Templated path — tasks generated from a linked onboarding template. ──

    [Fact]
    public async Task HandleAsync_Creates_Tasks_From_Linked_Template()
    {
        var taskCreator = new FakeTaskCreator();
        var nameReader  = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [EmployeeId] = "Priya Sharma" });
        var templateId  = Guid.NewGuid();

        var templateTasks = new List<OnboardingTemplateTaskItem>
        {
            new(Guid.NewGuid(), "Set up dev environment", "Install tooling", TaskPriority.High, OnboardingTemplateTaskAssignTo.NewHire, 0, 0),
            new(Guid.NewGuid(), "Introduce to team", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 2, 1),
            new(Guid.NewGuid(), "File paperwork", null, TaskPriority.Low, OnboardingTemplateTaskAssignTo.Unassigned, 5, 2),
        };

        var onboardingTemplateReader = new FakeOnboardingTemplateReader(templateId, templateTasks);
        var handler = new EmployeeCreatedHandler(taskCreator, nameReader, onboardingTemplateReader);

        var evt = MakeEvent(ManagerId, positionProfileId: Guid.NewGuid());
        await handler.HandleAsync(evt, CancellationToken.None);

        Assert.Equal(3, taskCreator.Created.Count);
        Assert.All(taskCreator.Created, t => Assert.Equal(TaskSource.Onboarding, t.Source));
        Assert.All(taskCreator.Created, t => Assert.Equal(TaskActionType.Complete, t.ActionType));
        Assert.All(taskCreator.Created, t => Assert.Contains("Priya Sharma", t.Title));
    }

    [Fact]
    public async Task HandleAsync_Maps_AssignTo_NewHire_To_Employee()
    {
        var taskCreator = new FakeTaskCreator();
        var templateTasks = new List<OnboardingTemplateTaskItem>
        {
            new(Guid.NewGuid(), "Set up dev environment", null, TaskPriority.High, OnboardingTemplateTaskAssignTo.NewHire, 0, 0),
        };
        var onboardingTemplateReader = new FakeOnboardingTemplateReader(Guid.NewGuid(), templateTasks);
        var handler = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader(), onboardingTemplateReader);

        await handler.HandleAsync(MakeEvent(ManagerId, positionProfileId: Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(EmployeeId, task.AssignedEmployeeId);
        Assert.Equal(EmployeeId, task.AssignedUserId);
    }

    [Fact]
    public async Task HandleAsync_Maps_AssignTo_Manager_To_ManagerId()
    {
        var taskCreator = new FakeTaskCreator();
        var templateTasks = new List<OnboardingTemplateTaskItem>
        {
            new(Guid.NewGuid(), "Introduce to team", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 0, 0),
        };
        var onboardingTemplateReader = new FakeOnboardingTemplateReader(Guid.NewGuid(), templateTasks);
        var handler = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader(), onboardingTemplateReader);

        await handler.HandleAsync(MakeEvent(ManagerId, positionProfileId: Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(ManagerId, task.AssignedEmployeeId);
        Assert.Equal(ManagerId, task.AssignedUserId);
    }

    [Fact]
    public async Task HandleAsync_Maps_AssignTo_Unassigned_To_Null()
    {
        var taskCreator = new FakeTaskCreator();
        var templateTasks = new List<OnboardingTemplateTaskItem>
        {
            new(Guid.NewGuid(), "File paperwork", null, TaskPriority.Low, OnboardingTemplateTaskAssignTo.Unassigned, 0, 0),
        };
        var onboardingTemplateReader = new FakeOnboardingTemplateReader(Guid.NewGuid(), templateTasks);
        var handler = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader(), onboardingTemplateReader);

        await handler.HandleAsync(MakeEvent(ManagerId, positionProfileId: Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Null(task.AssignedEmployeeId);
        Assert.Null(task.AssignedUserId);
    }

    [Fact]
    public async Task HandleAsync_Computes_DueDate_From_StartDate_Plus_DueDaysAfterStart()
    {
        var taskCreator = new FakeTaskCreator();
        var templateTasks = new List<OnboardingTemplateTaskItem>
        {
            new(Guid.NewGuid(), "Follow up task", null, TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Unassigned, 10, 0),
        };
        var onboardingTemplateReader = new FakeOnboardingTemplateReader(Guid.NewGuid(), templateTasks);
        var handler = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader(), onboardingTemplateReader);

        var evt = MakeEvent(ManagerId, positionProfileId: Guid.NewGuid());
        await handler.HandleAsync(evt, CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(evt.StartDate.AddDays(10), task.DueDate);
    }

    [Fact]
    public async Task HandleAsync_Passes_Priority_Through_From_Template_Task()
    {
        var taskCreator = new FakeTaskCreator();
        var templateTasks = new List<OnboardingTemplateTaskItem>
        {
            new(Guid.NewGuid(), "Critical task", null, TaskPriority.Critical, OnboardingTemplateTaskAssignTo.Unassigned, 0, 0),
        };
        var onboardingTemplateReader = new FakeOnboardingTemplateReader(Guid.NewGuid(), templateTasks);
        var handler = new EmployeeCreatedHandler(taskCreator, new FakeEmployeeNameReader(), onboardingTemplateReader);

        await handler.HandleAsync(MakeEvent(ManagerId, positionProfileId: Guid.NewGuid()), CancellationToken.None);

        var task = Assert.Single(taskCreator.Created);
        Assert.Equal(TaskPriority.Critical, task.Priority);
    }
}
