using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Features.CreateOnboardingPlanOnEmployeeCreated;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Onboarding.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Onboarding.Tests;

public class CreateOnboardingPlanOnEmployeeCreatedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);

    private sealed class Harness : IDisposable
    {
        public Harness(FakeOnboardingTemplateReader? templateReader, Dictionary<Guid, string>? names)
        {
            DbContext = new OnboardingDbContext(new DbContextOptionsBuilder<OnboardingDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
            Handler = new EmployeeCreatedHandler(
                DbContext,
                TaskCreator,
                HrTaskCreator,
                new FakeEmployeeNameReader(names),
                templateReader ?? new FakeOnboardingTemplateReader(),
                new FakeClock(FixedUtcNow),
                Logger);
        }

        public OnboardingDbContext DbContext { get; }
        public FakeTaskCreator TaskCreator { get; } = new();
        public FakeHrTaskCreator HrTaskCreator { get; } = new();
        public CapturingLogger<EmployeeCreatedHandler> Logger { get; } = new();
        public EmployeeCreatedHandler Handler { get; }

        public void Dispose() => DbContext.Dispose();
    }

    private static EmployeeCreatedIntegrationEvent BuildEvent(
        Guid companyId,
        Guid employeeId,
        DateOnly startDate,
        Guid? managerId = null,
        Guid? positionProfileId = null,
        bool isImported = false,
        bool isInitialCompanyAdmin = false) =>
        new(
            CompanyId: companyId,
            EmployeeId: employeeId,
            StartDate: startDate,
            ManagerId: managerId,
            ProbationEndDate: startDate.AddDays(90),
            PositionProfileId: positionProfileId,
            DefaultLeavePolicyId: null,
            IsImported: isImported,
            IsInitialCompanyAdmin: isInitialCompanyAdmin);

    private static OnboardingTemplateTaskItem Item(
        string title,
        OnboardingTemplateTaskAssignTo assignTo,
        int dueDays = 0,
        int order = 1,
        TaskPriority priority = TaskPriority.Medium,
        string? description = "Description.") =>
        new(Guid.NewGuid(), title, description, priority, assignTo, dueDays, order);

    [Fact]
    public async Task IsInitialCompanyAdmin_True_Creates_No_Plan_No_Tasks_And_No_TaskCreator_Calls()
    {
        using var h = new Harness(null, null);

        await h.Handler.HandleAsync(
            BuildEvent(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), isInitialCompanyAdmin: true),
            CancellationToken.None);

        Assert.Empty(h.DbContext.OnboardingPlans);
        Assert.Empty(h.DbContext.OnboardingTasks);
        Assert.Empty(h.TaskCreator.Created);
        Assert.Empty(h.HrTaskCreator.Created);
    }

    [Fact]
    public async Task IsImported_True_Creates_No_Plan_No_Tasks_And_No_TaskCreator_Calls()
    {
        using var h = new Harness(null, null);

        await h.Handler.HandleAsync(
            BuildEvent(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), isImported: true),
            CancellationToken.None);

        Assert.Empty(h.DbContext.OnboardingPlans);
        Assert.Empty(h.DbContext.OnboardingTasks);
        Assert.Empty(h.TaskCreator.Created);
        Assert.Empty(h.HrTaskCreator.Created);
    }

    [Fact]
    public async Task PositionProfileTemplate_Takes_Precedence_Over_Company_Default()
    {
        var explicitId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var reader = new FakeOnboardingTemplateReader(
            templateId: explicitId,
            defaultTemplateId: defaultId,
            tasksByTemplate: new Dictionary<Guid, IReadOnlyList<OnboardingTemplateTaskItem>>
            {
                [explicitId] = [Item("Explicit task", OnboardingTemplateTaskAssignTo.Manager)],
                [defaultId] = [Item("Default task", OnboardingTemplateTaskAssignTo.Manager)],
            });
        using var h = new Harness(reader, null);

        await h.Handler.HandleAsync(
            BuildEvent(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        var task = Assert.Single(h.DbContext.OnboardingTasks);
        Assert.StartsWith("Explicit task", task.Title);
        Assert.Equal(0, reader.DefaultLookups);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Company_Default_Template_Is_Used_When_Profile_Has_No_Usable_Template(
        bool hasPositionProfile, bool linkedTemplateHasNoActiveTasks)
    {
        var linkedId = Guid.NewGuid();
        var defaultId = Guid.NewGuid();
        var tasksByTemplate = new Dictionary<Guid, IReadOnlyList<OnboardingTemplateTaskItem>>
        {
            [defaultId] =
            [
                Item("Default one", OnboardingTemplateTaskAssignTo.Manager, order: 1),
                Item("Default two", OnboardingTemplateTaskAssignTo.NewHire, order: 2),
            ],
        };
        if (linkedTemplateHasNoActiveTasks)
            tasksByTemplate[linkedId] = [];

        var reader = new FakeOnboardingTemplateReader(
            templateId: linkedTemplateHasNoActiveTasks ? linkedId : null,
            defaultTemplateId: defaultId,
            tasksByTemplate: tasksByTemplate);
        using var h = new Harness(reader, null);

        await h.Handler.HandleAsync(
            BuildEvent(
                Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), Guid.NewGuid(),
                hasPositionProfile ? Guid.NewGuid() : null),
            CancellationToken.None);

        Assert.Equal(2, h.DbContext.OnboardingTasks.Count());
        Assert.Equal(2, h.TaskCreator.Created.Count);
        Assert.Equal(1, reader.DefaultLookups);
        Assert.DoesNotContain(h.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_Usable_Template_Creates_Empty_Plan_Logs_Warning_And_No_Hardcoded_Tasks(bool defaultHasNoTasks)
    {
        var defaultId = Guid.NewGuid();
        var reader = new FakeOnboardingTemplateReader(
            defaultTemplateId: defaultHasNoTasks ? defaultId : null,
            tasksByTemplate: new Dictionary<Guid, IReadOnlyList<OnboardingTemplateTaskItem>> { [defaultId] = [] });
        using var h = new Harness(reader, null);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await h.Handler.HandleAsync(
            BuildEvent(companyId, employeeId, new DateOnly(2026, 7, 1), Guid.NewGuid()),
            CancellationToken.None);

        var plan = Assert.Single(h.DbContext.OnboardingPlans);
        Assert.Equal(companyId, plan.CompanyId);
        Assert.Equal(employeeId, plan.EmployeeId);
        Assert.Empty(h.DbContext.OnboardingTasks);
        Assert.Empty(h.TaskCreator.Created);
        Assert.Empty(h.HrTaskCreator.Created);
        Assert.Contains(h.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Template_Path_Creates_Task_Per_Template_Item_With_Correct_Mapping()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 7, 1);
        const string employeeName = "Alex Doe";

        var templateTasks = new List<OnboardingTemplateTaskItem>
        {
            Item("Laptop setup", OnboardingTemplateTaskAssignTo.NewHire, 0, 1, TaskPriority.High, "Set up laptop."),
            Item("Manager intro", OnboardingTemplateTaskAssignTo.Manager, 3, 2, TaskPriority.Medium, "Meet manager."),
            Item("Misc", OnboardingTemplateTaskAssignTo.Unassigned, 5, 3, TaskPriority.Low, "Anything."),
            Item("HR paperwork", OnboardingTemplateTaskAssignTo.Hr, 7, 4, TaskPriority.Critical, "Complete forms."),
        };

        using var h = new Harness(
            new FakeOnboardingTemplateReader(Guid.NewGuid(), templateTasks),
            new Dictionary<Guid, string> { [employeeId] = employeeName });

        await h.Handler.HandleAsync(
            BuildEvent(companyId, employeeId, startDate, managerId, Guid.NewGuid()),
            CancellationToken.None);

        var plan = Assert.Single(h.DbContext.OnboardingPlans);
        Assert.Equal(startDate, plan.StartDate);

        var tasks = h.DbContext.OnboardingTasks.ToList();
        Assert.Equal(4, tasks.Count);
        Assert.All(tasks, t => Assert.Equal(plan.Id, t.OnboardingPlanId));
        Assert.All(tasks, t => Assert.Equal(OnboardingTaskStatus.Pending, t.Status));
        Assert.Equal(3, h.TaskCreator.Created.Count);
        Assert.All(h.TaskCreator.Created, c => Assert.Equal(TaskSource.Onboarding, c.Source));
        Assert.All(h.TaskCreator.Created, c => Assert.Equal(TaskActionType.Complete, c.ActionType));

        AssertTask(tasks, h.TaskCreator, $"Laptop setup — {employeeName}", OnboardingTemplateTaskAssignTo.NewHire,
            "Set up laptop.", TaskPriority.High, startDate, employeeId, employeeId);
        AssertTask(tasks, h.TaskCreator, $"Manager intro — {employeeName}", OnboardingTemplateTaskAssignTo.Manager,
            "Meet manager.", TaskPriority.Medium, startDate.AddDays(3), managerId, managerId);
        AssertTask(tasks, h.TaskCreator, $"Misc — {employeeName}", OnboardingTemplateTaskAssignTo.Unassigned,
            "Anything.", TaskPriority.Low, startDate.AddDays(5), null, null);

        var hrTitle = $"HR paperwork — {employeeName}";
        var hrOnboardingTask = Assert.Single(tasks, t => t.Title == hrTitle);
        Assert.Equal(OnboardingTemplateTaskAssignTo.Hr, hrOnboardingTask.AssignTo);
        Assert.Equal(startDate.AddDays(7), hrOnboardingTask.DueDate);

        var hrCall = Assert.Single(h.HrTaskCreator.Created);
        Assert.Equal(hrTitle, hrCall.Title);
        Assert.Equal("Complete forms.", hrCall.Description);
        Assert.Equal(TaskPriority.Critical, hrCall.Priority);
        Assert.Equal(startDate.AddDays(7), hrCall.DueDate);
        Assert.Equal(TaskSource.Onboarding, hrCall.Source);
        Assert.Equal(TaskActionType.Complete, hrCall.ActionType);
        Assert.Equal(hrOnboardingTask.Id, hrCall.SourceEntityId);
        Assert.Equal(companyId, hrCall.CompanyId);
    }

    [Theory]
    [InlineData(OnboardingTemplateTaskAssignTo.NewHire)]
    [InlineData(OnboardingTemplateTaskAssignTo.Manager)]
    [InlineData(OnboardingTemplateTaskAssignTo.Unassigned)]
    [InlineData(OnboardingTemplateTaskAssignTo.Hr)]
    public async Task Template_Task_AssignTo_Maps_To_Correct_Assignee(OnboardingTemplateTaskAssignTo assignTo)
    {
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 7, 1);

        using var h = new Harness(
            new FakeOnboardingTemplateReader(Guid.NewGuid(), [Item("Assigned task", assignTo, dueDays: 2)]),
            null);

        await h.Handler.HandleAsync(
            BuildEvent(Guid.NewGuid(), employeeId, startDate, managerId, Guid.NewGuid()),
            CancellationToken.None);

        var task = Assert.Single(h.DbContext.OnboardingTasks);
        Assert.Equal(assignTo, task.AssignTo);
        Assert.Equal(startDate.AddDays(2), task.DueDate);

        if (assignTo == OnboardingTemplateTaskAssignTo.Hr)
        {
            Assert.Empty(h.TaskCreator.Created);
            var hrCall = Assert.Single(h.HrTaskCreator.Created);
            Assert.Equal(task.Id, hrCall.SourceEntityId);
            return;
        }

        Assert.Empty(h.HrTaskCreator.Created);
        var call = Assert.Single(h.TaskCreator.Created);
        var (expectedEmployeeId, expectedUserId) = assignTo switch
        {
            OnboardingTemplateTaskAssignTo.NewHire => ((Guid?)employeeId, (Guid?)employeeId),
            OnboardingTemplateTaskAssignTo.Manager => (managerId, managerId),
            _ => ((Guid?)null, (Guid?)null),
        };
        Assert.Equal(expectedEmployeeId, call.AssignedEmployeeId);
        Assert.Equal(expectedUserId, call.AssignedUserId);
        Assert.Equal(task.Id, call.SourceEntityId);
    }

    [Fact]
    public async Task Uses_Fallback_Employee_Name_When_Name_Not_Found()
    {
        using var h = new Harness(
            new FakeOnboardingTemplateReader(Guid.NewGuid(), [Item("Welcome", OnboardingTemplateTaskAssignTo.Manager)]),
            null);

        await h.Handler.HandleAsync(
            BuildEvent(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.Contains(h.TaskCreator.Created, c => c.Title == "Welcome — the new employee");
    }

    [Fact]
    public async Task Default_Template_Generates_All_Tasks_Preserving_Offsets_Owners_And_Descriptions()
    {
        var defaultId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 7, 1);
        var items = Enumerable.Range(1, 15)
            .Select(i => Item(
                $"Step {i}",
                i == 5
                    ? OnboardingTemplateTaskAssignTo.Hr
                    : i % 2 == 0 ? OnboardingTemplateTaskAssignTo.Manager : OnboardingTemplateTaskAssignTo.NewHire,
                dueDays: i * 2,
                order: i,
                description: $"Description {i}"))
            .ToList();

        using var h = new Harness(
            new FakeOnboardingTemplateReader(
                defaultTemplateId: defaultId,
                tasksByTemplate: new Dictionary<Guid, IReadOnlyList<OnboardingTemplateTaskItem>> { [defaultId] = items }),
            null);

        await h.Handler.HandleAsync(
            BuildEvent(Guid.NewGuid(), Guid.NewGuid(), startDate, Guid.NewGuid()),
            CancellationToken.None);

        var tasks = h.DbContext.OnboardingTasks.ToList();
        Assert.Equal(15, tasks.Count);
        Assert.Single(h.HrTaskCreator.Created);
        Assert.Equal(14, h.TaskCreator.Created.Count);
        foreach (var item in items)
        {
            var task = Assert.Single(tasks, t => t.Title.StartsWith($"{item.Title} — "));
            Assert.Equal(item.AssignTo, task.AssignTo);
            Assert.Equal(startDate.AddDays(item.DueDaysAfterStart), task.DueDate);
            Assert.Equal(item.Description, task.Description);
        }
    }

    private static void AssertTask(
        List<OnboardingTask> tasks,
        FakeTaskCreator taskCreator,
        string title,
        OnboardingTemplateTaskAssignTo assignTo,
        string? description,
        TaskPriority priority,
        DateOnly dueDate,
        Guid? assignedEmployeeId,
        Guid? assignedUserId)
    {
        var task = Assert.Single(tasks, t => t.Title == title);
        Assert.Equal(assignTo, task.AssignTo);
        Assert.Equal(description, task.Description);
        Assert.Equal(dueDate, task.DueDate);

        var call = Assert.Single(taskCreator.Created, c => c.Title == title);
        Assert.Equal(description, call.Description);
        Assert.Equal(priority, call.Priority);
        Assert.Equal(dueDate, call.DueDate);
        Assert.Equal(assignedEmployeeId, call.AssignedEmployeeId);
        Assert.Equal(assignedUserId, call.AssignedUserId);
        Assert.Equal(task.Id, call.SourceEntityId);
    }
}
