using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Features.CreateOnboardingPlanOnEmployeeCreated;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Onboarding.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Tests;

public class CreateOnboardingPlanOnEmployeeCreatedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);

    public enum FallbackTrigger
    {
        NoPositionProfileId,
        PositionProfileSetButNoTemplateLinked,
        TemplateLinkedButZeroActiveTasks
    }

    private static OnboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static EmployeeCreatedIntegrationEvent BuildEvent(
        Guid companyId,
        Guid employeeId,
        DateOnly startDate,
        Guid? managerId = null,
        Guid? positionProfileId = null,
        bool isImported = false) =>
        new(
            CompanyId: companyId,
            EmployeeId: employeeId,
            StartDate: startDate,
            ManagerId: managerId,
            ProbationEndDate: startDate.AddDays(90),
            PositionProfileId: positionProfileId,
            DefaultLeavePolicyId: null,
            IsImported: isImported);

    [Fact]
    public async Task IsImported_True_Creates_No_Plan_No_Tasks_And_No_TaskCreator_Calls()
    {
        await using var dbContext = BuildContext();
        var taskCreator = new FakeTaskCreator();
        var handler = new EmployeeCreatedHandler(
            dbContext,
            taskCreator,
            new FakeEmployeeNameReader(),
            new FakeOnboardingTemplateReader(),
            new FakeClock(FixedUtcNow));

        var e = BuildEvent(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), isImported: true);

        await handler.HandleAsync(e, CancellationToken.None);

        Assert.Empty(dbContext.OnboardingPlans);
        Assert.Empty(dbContext.OnboardingTasks);
        Assert.Empty(taskCreator.Created);
    }

    [Theory]
    [InlineData(FallbackTrigger.NoPositionProfileId, false)]
    [InlineData(FallbackTrigger.NoPositionProfileId, true)]
    [InlineData(FallbackTrigger.PositionProfileSetButNoTemplateLinked, false)]
    [InlineData(FallbackTrigger.PositionProfileSetButNoTemplateLinked, true)]
    [InlineData(FallbackTrigger.TemplateLinkedButZeroActiveTasks, false)]
    [InlineData(FallbackTrigger.TemplateLinkedButZeroActiveTasks, true)]
    public async Task Falls_Back_To_Three_Hardcoded_Tasks_When_No_Usable_Template(
        FallbackTrigger trigger, bool hasManager)
    {
        await using var dbContext = BuildContext();
        var taskCreator = new FakeTaskCreator();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = hasManager ? Guid.NewGuid() : (Guid?)null;
        var startDate = new DateOnly(2026, 7, 1);

        Guid? positionProfileId = trigger == FallbackTrigger.NoPositionProfileId
            ? null
            : Guid.NewGuid();

        var templateReader = trigger switch
        {
            FallbackTrigger.NoPositionProfileId => new FakeOnboardingTemplateReader(),
            FallbackTrigger.PositionProfileSetButNoTemplateLinked => new FakeOnboardingTemplateReader(),
            FallbackTrigger.TemplateLinkedButZeroActiveTasks =>
                new FakeOnboardingTemplateReader(Guid.NewGuid(), []),
            _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
        };

        const string employeeName = "Jamie Smith";
        var handler = new EmployeeCreatedHandler(
            dbContext,
            taskCreator,
            new FakeEmployeeNameReader(new Dictionary<Guid, string> { [employeeId] = employeeName }),
            templateReader,
            new FakeClock(FixedUtcNow));

        var e = BuildEvent(companyId, employeeId, startDate, managerId, positionProfileId);

        await handler.HandleAsync(e, CancellationToken.None);

        AssertFallbackTasksCreated(dbContext, taskCreator, companyId, employeeId, managerId, startDate, employeeName);
    }

    [Fact]
    public async Task Uses_Fallback_Employee_Name_When_Name_Not_Found()
    {
        await using var dbContext = BuildContext();
        var taskCreator = new FakeTaskCreator();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 7, 1);

        var handler = new EmployeeCreatedHandler(
            dbContext,
            taskCreator,
            new FakeEmployeeNameReader(),
            new FakeOnboardingTemplateReader(),
            new FakeClock(FixedUtcNow));

        var e = BuildEvent(companyId, employeeId, startDate);

        await handler.HandleAsync(e, CancellationToken.None);

        Assert.Contains(
            taskCreator.Created,
            c => c.Title == "Set up workstation and system access — the new employee");
    }

    [Fact]
    public async Task Templated_Path_Creates_Task_Per_Template_Item_With_Correct_Mapping()
    {
        await using var dbContext = BuildContext();
        var taskCreator = new FakeTaskCreator();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 7, 1);
        const string employeeName = "Alex Doe";

        var templateTasks = new List<OnboardingTemplateTaskItem>
        {
            new(Guid.NewGuid(), "Laptop setup", "Set up laptop.", TaskPriority.High, OnboardingTemplateTaskAssignTo.NewHire, 0, 1),
            new(Guid.NewGuid(), "Manager intro", "Meet manager.", TaskPriority.Medium, OnboardingTemplateTaskAssignTo.Manager, 3, 2),
            new(Guid.NewGuid(), "HR paperwork", "Complete forms.", TaskPriority.Low, OnboardingTemplateTaskAssignTo.Unassigned, 5, 3),
        };

        var handler = new EmployeeCreatedHandler(
            dbContext,
            taskCreator,
            new FakeEmployeeNameReader(new Dictionary<Guid, string> { [employeeId] = employeeName }),
            new FakeOnboardingTemplateReader(templateId, templateTasks),
            new FakeClock(FixedUtcNow));

        var e = BuildEvent(companyId, employeeId, startDate, managerId, positionProfileId);

        await handler.HandleAsync(e, CancellationToken.None);

        var plan = Assert.Single(dbContext.OnboardingPlans);
        Assert.Equal(companyId, plan.CompanyId);
        Assert.Equal(employeeId, plan.EmployeeId);
        Assert.Equal(startDate, plan.StartDate);
        Assert.Equal(OnboardingStatus.NotStarted, plan.Status);

        var tasks = dbContext.OnboardingTasks.ToList();
        Assert.Equal(3, tasks.Count);
        Assert.Equal(3, taskCreator.Created.Count);
        Assert.All(tasks, t => Assert.Equal(plan.Id, t.OnboardingPlanId));
        Assert.All(tasks, t => Assert.Equal(OnboardingTaskStatus.Pending, t.Status));
        Assert.All(taskCreator.Created, c => Assert.Equal(TaskSource.Onboarding, c.Source));
        Assert.All(taskCreator.Created, c => Assert.Equal(TaskActionType.Complete, c.ActionType));

        AssertTemplatedTaskAndCall(
            tasks, taskCreator, $"Laptop setup — {employeeName}",
            TaskPriority.High, startDate, employeeId, employeeId);
        AssertTemplatedTaskAndCall(
            tasks, taskCreator, $"Manager intro — {employeeName}",
            TaskPriority.Medium, startDate.AddDays(3), managerId, managerId);
        AssertTemplatedTaskAndCall(
            tasks, taskCreator, $"HR paperwork — {employeeName}",
            TaskPriority.Low, startDate.AddDays(5), null, null);
    }

    [Theory]
    [InlineData(OnboardingTemplateTaskAssignTo.NewHire)]
    [InlineData(OnboardingTemplateTaskAssignTo.Manager)]
    [InlineData(OnboardingTemplateTaskAssignTo.Unassigned)]
    public async Task Templated_Task_AssignTo_Maps_To_Correct_Assignee_Ids(
        OnboardingTemplateTaskAssignTo assignTo)
    {
        await using var dbContext = BuildContext();
        var taskCreator = new FakeTaskCreator();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 7, 1);

        var templateTasks = new List<OnboardingTemplateTaskItem>
        {
            new(Guid.NewGuid(), "Assigned task", "Description.", TaskPriority.Medium, assignTo, 2, 1),
        };

        var handler = new EmployeeCreatedHandler(
            dbContext,
            taskCreator,
            new FakeEmployeeNameReader(),
            new FakeOnboardingTemplateReader(templateId, templateTasks),
            new FakeClock(FixedUtcNow));

        var e = BuildEvent(companyId, employeeId, startDate, managerId, positionProfileId);

        await handler.HandleAsync(e, CancellationToken.None);

        var call = Assert.Single(taskCreator.Created);
        var (expectedEmployeeId, expectedUserId) = assignTo switch
        {
            OnboardingTemplateTaskAssignTo.NewHire => ((Guid?)employeeId, (Guid?)employeeId),
            OnboardingTemplateTaskAssignTo.Manager => (managerId, managerId),
            _ => ((Guid?)null, (Guid?)null),
        };

        Assert.Equal(expectedEmployeeId, call.AssignedEmployeeId);
        Assert.Equal(expectedUserId, call.AssignedUserId);

        var task = Assert.Single(dbContext.OnboardingTasks);
        Assert.Equal(assignTo, task.AssignTo);
        Assert.Equal(startDate.AddDays(2), task.DueDate);
        Assert.Equal(task.Id, call.SourceEntityId);
    }

    [Fact]
    public async Task Templated_Task_DueDate_Is_StartDate_Plus_DueDaysAfterStart()
    {
        await using var dbContext = BuildContext();
        var taskCreator = new FakeTaskCreator();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 7, 1);

        var templateTasks = new List<OnboardingTemplateTaskItem>
        {
            new(Guid.NewGuid(), "Delayed task", null, TaskPriority.Critical, OnboardingTemplateTaskAssignTo.Unassigned, 14, 1),
        };

        var handler = new EmployeeCreatedHandler(
            dbContext,
            taskCreator,
            new FakeEmployeeNameReader(),
            new FakeOnboardingTemplateReader(templateId, templateTasks),
            new FakeClock(FixedUtcNow));

        var e = BuildEvent(companyId, employeeId, startDate, positionProfileId: positionProfileId);

        await handler.HandleAsync(e, CancellationToken.None);

        var call = Assert.Single(taskCreator.Created);
        Assert.Equal(startDate.AddDays(14), call.DueDate);
        Assert.Equal(TaskPriority.Critical, call.Priority);

        var task = Assert.Single(dbContext.OnboardingTasks);
        Assert.Equal(startDate.AddDays(14), task.DueDate);
    }

    private static void AssertFallbackTasksCreated(
        OnboardingDbContext dbContext,
        FakeTaskCreator taskCreator,
        Guid companyId,
        Guid employeeId,
        Guid? managerId,
        DateOnly startDate,
        string employeeName)
    {
        var plan = Assert.Single(dbContext.OnboardingPlans);
        Assert.Equal(companyId, plan.CompanyId);
        Assert.Equal(employeeId, plan.EmployeeId);
        Assert.Equal(startDate, plan.StartDate);
        Assert.Equal(OnboardingStatus.NotStarted, plan.Status);

        var tasks = dbContext.OnboardingTasks.ToList();
        Assert.Equal(3, tasks.Count);
        Assert.Equal(3, taskCreator.Created.Count);
        Assert.All(tasks, t => Assert.Equal(plan.Id, t.OnboardingPlanId));
        Assert.All(tasks, t => Assert.Equal(OnboardingTaskStatus.Pending, t.Status));
        Assert.All(taskCreator.Created, c => Assert.Equal(TaskSource.Onboarding, c.Source));
        Assert.All(taskCreator.Created, c => Assert.Equal(TaskActionType.Complete, c.ActionType));

        var workstationTitle = $"Set up workstation and system access — {employeeName}";
        var welcomeTitle = $"Send welcome email and first-day details — {employeeName}";
        var inductionTitle = $"Schedule welcome and induction meeting — {employeeName}";

        AssertTaskAndCall(
            tasks, taskCreator, workstationTitle,
            OnboardingTemplateTaskAssignTo.Unassigned, startDate, TaskPriority.High, null, null);
        AssertTaskAndCall(
            tasks, taskCreator, welcomeTitle,
            OnboardingTemplateTaskAssignTo.Manager, startDate, TaskPriority.Medium, managerId, managerId);
        AssertTaskAndCall(
            tasks, taskCreator, inductionTitle,
            OnboardingTemplateTaskAssignTo.Manager, startDate.AddDays(7), TaskPriority.Medium, managerId, managerId);
    }

    private static void AssertTaskAndCall(
        List<OnboardingTask> tasks,
        FakeTaskCreator taskCreator,
        string title,
        OnboardingTemplateTaskAssignTo assignTo,
        DateOnly dueDate,
        TaskPriority priority,
        Guid? assignedEmployeeId,
        Guid? assignedUserId)
    {
        var task = Assert.Single(tasks, t => t.Title == title);
        Assert.Equal(assignTo, task.AssignTo);
        Assert.Equal(dueDate, task.DueDate);

        var call = Assert.Single(taskCreator.Created, c => c.Title == title);
        Assert.Equal(priority, call.Priority);
        Assert.Equal(dueDate, call.DueDate);
        Assert.Equal(assignedEmployeeId, call.AssignedEmployeeId);
        Assert.Equal(assignedUserId, call.AssignedUserId);
        Assert.Equal(task.Id, call.SourceEntityId);
    }

    private static void AssertTemplatedTaskAndCall(
        List<OnboardingTask> tasks,
        FakeTaskCreator taskCreator,
        string title,
        TaskPriority priority,
        DateOnly dueDate,
        Guid? assignedEmployeeId,
        Guid? assignedUserId)
    {
        var task = Assert.Single(tasks, t => t.Title == title);
        Assert.Equal(dueDate, task.DueDate);

        var call = Assert.Single(taskCreator.Created, c => c.Title == title);
        Assert.Equal(priority, call.Priority);
        Assert.Equal(dueDate, call.DueDate);
        Assert.Equal(assignedEmployeeId, call.AssignedEmployeeId);
        Assert.Equal(assignedUserId, call.AssignedUserId);
        Assert.Equal(task.Id, call.SourceEntityId);
    }
}
