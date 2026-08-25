using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.StartOffboarding;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Modules.Offboarding.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Offboarding.Tests;

public class StartOffboardingHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static StartOffboardingRequest BuildRequest(
        Guid companyId, Guid employeeId, DateOnly? lastWorkingDay = null, string? notes = null) =>
        new(companyId, employeeId, lastWorkingDay ?? new DateOnly(2026, 7, 15), notes);

    private sealed record Harness(
        StartOffboardingHandler Handler,
        FakeNotificationWriter Notifications,
        FakeTaskCreator TaskCreator,
        CapturingIntegrationEventPublisher IntegrationPublisher);

    private static Harness BuildHandler(
        OffboardingDbContext dbContext,
        Dictionary<Guid, string>? employeeNames = null,
        Guid? managerId = null,
        IReadOnlyList<AssignedAssetItem>? assignedAssets = null,
        IReadOnlyList<OutstandingDocumentRequestItem>? outstandingDocuments = null)
    {
        var notifications = new FakeNotificationWriter();
        var taskCreator = new FakeTaskCreator();
        var integrationPublisher = new CapturingIntegrationEventPublisher();
        var taskSynchronizer = new OffboardingTaskSynchronizer(
            dbContext,
            taskCreator,
            new FakeClock(FixedUtcNow),
            NullLogger<OffboardingTaskSynchronizer>.Instance);
        var handler = new StartOffboardingHandler(
            dbContext,
            new FakeClock(FixedUtcNow),
            new FakeEmployeeNameReader(employeeNames),
            new FakeManagerReader(managerId),
            new FakeAssignedAssetReader(assignedAssets),
            new FakeOutstandingDocumentRequestReader(outstandingDocuments),
            taskSynchronizer,
            notifications,
            integrationPublisher);
        return new Harness(handler, notifications, taskCreator, integrationPublisher);
    }

    [Fact]
    public async Task HandleAsync_Publishes_OffboardingStarted_IntegrationEvent_On_Successful_Start()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildHandler(dbContext, names);

        var request = BuildRequest(companyId, employeeId);

        var result = await harness.Handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.IsType<OffboardingStartedIntegrationEvent>(Assert.Single(harness.IntegrationPublisher.Published));
        Assert.Equal(companyId, evt.CompanyId);
        Assert.Equal(employeeId, evt.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_IntegrationEvent_When_Employee_Not_Found()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var harness = BuildHandler(dbContext, employeeNames: null);

        await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        Assert.Empty(harness.IntegrationPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Successful_Start_Creates_Plan_In_InProgress_Status()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildHandler(dbContext, names);

        var request = BuildRequest(companyId, employeeId, new DateOnly(2026, 8, 1), "Resigned.");

        var result = await harness.Handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal(new DateOnly(2026, 8, 1), result.Value.LastWorkingDay);
        Assert.Equal("InProgress", result.Value.Status);
        Assert.Equal("Resigned.", result.Value.Notes);

        var plan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == result.Value.Id);
        Assert.Equal(OffboardingStatus.InProgress, plan.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var harness = BuildHandler(dbContext, employeeNames: null);

        var result = await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(dbContext.OffboardingPlans);
    }

    [Theory]
    [InlineData(1)] // NotStarted
    [InlineData(2)] // InProgress
    public async Task HandleAsync_Returns_Conflict_When_Active_Plan_Already_Exists(int statusValue)
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };

        var existingPlan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 7, 1), null, Now.AddDays(-1));
        if ((OffboardingStatus)statusValue == OffboardingStatus.InProgress)
            existingPlan.Start(Now.AddDays(-1));
        dbContext.OffboardingPlans.Add(existingPlan);
        await dbContext.SaveChangesAsync();

        var harness = BuildHandler(dbContext, names);

        var result = await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Theory]
    [InlineData(true)]  // Completed
    [InlineData(false)] // Cancelled
    public async Task HandleAsync_Allows_New_Plan_When_Existing_Plan_Is_Terminal(bool isCompleted)
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };

        var existingPlan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 1, 1), null, Now.AddDays(-90));
        existingPlan.Start(Now.AddDays(-90));
        if (isCompleted)
            existingPlan.Complete(Now.AddDays(-60));
        else
            existingPlan.Cancel(null, Now.AddDays(-60));
        dbContext.OffboardingPlans.Add(existingPlan);
        await dbContext.SaveChangesAsync();

        var harness = BuildHandler(dbContext, names);

        var result = await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Creates_One_Task_And_One_TaskCreator_Call_Per_Assigned_Asset()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var assets = new List<AssignedAssetItem>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "MacBook Pro"),
            new(Guid.NewGuid(), Guid.NewGuid(), "iPhone 15"),
        };
        var harness = BuildHandler(dbContext, names, assignedAssets: assets);

        var request = BuildRequest(companyId, employeeId, new DateOnly(2026, 8, 1));

        var result = await harness.Handler.HandleAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var assetTasks = dbContext.OffboardingTasks
            .Where(t => t.AssignTo == OffboardingTaskAssignTo.Employee)
            .ToList();
        Assert.Equal(2, assetTasks.Count);
        Assert.Contains(assetTasks, t => t.Title == "Return asset: MacBook Pro");
        Assert.Contains(assetTasks, t => t.Title == "Return asset: iPhone 15");
        Assert.All(assetTasks, t => Assert.Equal(new DateOnly(2026, 8, 1), t.DueDate));

        var assetCalls = harness.TaskCreator.Created
            .Where(c => c.Title.StartsWith("Return asset:"))
            .ToList();
        Assert.Equal(2, assetCalls.Count);
        Assert.All(assetCalls, c =>
        {
            Assert.Equal(TaskSource.Offboarding, c.Source);
            Assert.Equal(TaskActionType.Complete, c.ActionType);
            Assert.Equal(employeeId, c.AssignedEmployeeId);
            Assert.Equal(employeeId, c.AssignedUserId);
            Assert.Equal(TaskPriority.Medium, c.Priority);
        });
    }

    [Fact]
    public async Task HandleAsync_Creates_No_Asset_Tasks_When_No_Assets_Assigned()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildHandler(dbContext, names);

        await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        Assert.Empty(dbContext.OffboardingTasks.Where(t => t.AssignTo == OffboardingTaskAssignTo.Employee));
    }

    [Fact]
    public async Task HandleAsync_Creates_Exactly_One_HR_Document_Review_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var outstanding = new List<OutstandingDocumentRequestItem>
        {
            new(Guid.NewGuid(), "Passport", null, true),
        };
        var harness = BuildHandler(dbContext, names, outstandingDocuments: outstanding);

        await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId, new DateOnly(2026, 8, 1)), CancellationToken.None);

        var hrTasks = dbContext.OffboardingTasks.Where(t => t.AssignTo == OffboardingTaskAssignTo.HR).ToList();
        var hrTask = Assert.Single(hrTasks);
        Assert.Equal("Review outstanding documents for employee exit", hrTask.Title);
        Assert.Equal(new DateOnly(2026, 8, 1), hrTask.DueDate);
        Assert.Contains("1 outstanding document request(s)", hrTask.Description);

        var hrCall = Assert.Single(harness.TaskCreator.Created, c => c.Title == "Review outstanding documents for employee exit");
        Assert.Null(hrCall.AssignedEmployeeId);
        Assert.Null(hrCall.AssignedUserId);
        Assert.Equal(TaskSource.Offboarding, hrCall.Source);
        Assert.Equal(TaskActionType.Complete, hrCall.ActionType);
    }

    [Fact]
    public async Task HandleAsync_HR_Task_Description_States_No_Outstanding_Requests_When_None()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildHandler(dbContext, names);

        await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        var hrTask = Assert.Single(dbContext.OffboardingTasks.Where(t => t.AssignTo == OffboardingTaskAssignTo.HR));
        Assert.Equal("No outstanding document requests.", hrTask.Description);
    }

    [Fact]
    public async Task HandleAsync_Creates_Exactly_Four_Manager_Checklist_Tasks()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildHandler(dbContext, names, managerId: managerId);

        await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId, new DateOnly(2026, 8, 1)), CancellationToken.None);

        var managerTasks = dbContext.OffboardingTasks.Where(t => t.AssignTo == OffboardingTaskAssignTo.Manager).ToList();
        Assert.Equal(4, managerTasks.Count);
        Assert.All(managerTasks, t => Assert.Equal(new DateOnly(2026, 8, 1), t.DueDate));
        Assert.Contains(managerTasks, t => t.Title.Contains("Conduct exit interview"));
        Assert.Contains(managerTasks, t => t.Title.Contains("Revoke system access"));
        Assert.Contains(managerTasks, t => t.Title.Contains("Arrange handover"));
        Assert.Contains(managerTasks, t => t.Title.Contains("Notify IT and Payroll"));

        var managerCalls = harness.TaskCreator.Created
            .Where(c => c.AssignedEmployeeId == managerId)
            .ToList();
        Assert.Equal(4, managerCalls.Count);
        Assert.All(managerCalls, c => Assert.Equal(managerId, c.AssignedUserId));
    }

    [Fact]
    public async Task HandleAsync_Manager_Checklist_Tasks_Have_Null_Assignees_When_No_Manager()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildHandler(dbContext, names, managerId: null);

        await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        var managerTasks = dbContext.OffboardingTasks.Where(t => t.AssignTo == OffboardingTaskAssignTo.Manager).ToList();
        Assert.Equal(4, managerTasks.Count);

        var managerCalls = harness.TaskCreator.Created
            .Where(c => c.Title.Contains("exit interview") || c.Title.Contains("system access")
                || c.Title.Contains("handover") || c.Title.Contains("IT and Payroll"))
            .ToList();
        Assert.Equal(4, managerCalls.Count);
        Assert.All(managerCalls, c =>
        {
            Assert.Null(c.AssignedEmployeeId);
            Assert.Null(c.AssignedUserId);
        });
    }

    [Fact]
    public async Task HandleAsync_Notifies_Employee_With_OffboardingStarted_Type()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildHandler(dbContext, names, managerId: null);

        var result = await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        var employeeNotification = Assert.Single(harness.Notifications.Written);
        Assert.Equal(employeeId, employeeNotification.EmployeeId);
        Assert.Equal(NotificationType.OffboardingStarted, employeeNotification.Type);
        Assert.Equal(result.Value!.Id, employeeNotification.SourceEntityId);
    }

    [Fact]
    public async Task HandleAsync_Also_Notifies_Manager_When_Present()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildHandler(dbContext, names, managerId: managerId);

        await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        Assert.Equal(2, harness.Notifications.Written.Count);
        Assert.Contains(harness.Notifications.Written, n => n.EmployeeId == employeeId && n.Type == NotificationType.OffboardingStarted);
        Assert.Contains(harness.Notifications.Written, n => n.EmployeeId == managerId && n.Type == NotificationType.OffboardingStarted);
    }

    [Fact]
    public async Task HandleAsync_Sends_No_Manager_Notification_When_No_Manager()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildHandler(dbContext, names, managerId: null);

        await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        Assert.Single(harness.Notifications.Written);
        Assert.DoesNotContain(harness.Notifications.Written, n => n.EmployeeId != employeeId);
    }

    [Fact]
    public async Task HandleAsync_GeneratedTaskIds_Match_Total_Task_Count()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var assets = new List<AssignedAssetItem> { new(Guid.NewGuid(), Guid.NewGuid(), "Monitor") };
        var harness = BuildHandler(dbContext, names, assignedAssets: assets);

        var result = await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        // 1 asset task + 1 HR document-review task + 4 manager checklist tasks = 6
        Assert.Equal(6, result.Value!.GeneratedTaskIds.Count);
        Assert.Equal(6, dbContext.OffboardingTasks.Count());
    }

    // OFF-03: DbUpdateException-on-race coverage for the unique partial index
    // (ix_offboarding_plans_company_id_employee_id_active) is deliberately NOT attempted at the unit
    // level here — EF Core's InMemory provider does not enforce unique indexes/constraints, so there
    // is no way to make SaveChangesAsync throw a DbUpdateException for a duplicate active plan under
    // this provider. That guarantee is instead exercised for real against Postgres in
    // HR.Integration.Tests (StartOffboardingEndpointTests — concurrent-request test), which is the
    // only place that can actually trigger the index and prove the catch path returns Conflict.

    // OFF-03
    [Fact]
    public async Task HandleAsync_Commits_Plan_And_Tasks_Before_Calling_TaskCreator()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildHandler(dbContext, names);

        var pendingAddedEntriesObservedDuringSync = new List<int>();
        harness.TaskCreator.OnCreateAsyncInvoked = () =>
        {
            var pendingAdded = dbContext.ChangeTracker.Entries()
                .Count(e => e.State == EntityState.Added);
            pendingAddedEntriesObservedDuringSync.Add(pendingAdded);
        };

        var result = await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(pendingAddedEntriesObservedDuringSync);
        // By the time any TaskCreator.CreateAsync call happens, the plan/tasks SaveChangesAsync has
        // already run — nothing should still be pending "Added" in the change tracker.
        Assert.All(pendingAddedEntriesObservedDuringSync, count => Assert.Equal(0, count));

        var plan = await dbContext.OffboardingPlans.SingleAsync(p => p.Id == result.Value!.Id);
        Assert.NotEqual(default, plan.Id);
    }

    // OFF-03
    [Fact]
    public async Task HandleAsync_Returns_Success_Even_When_TaskSynchronizer_Fails_For_One_Generated_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildHandler(dbContext, names);
        harness.TaskCreator.TitlesToFail.Add("Review outstanding documents for employee exit");

        var result = await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var failedTask = await dbContext.OffboardingTasks.SingleAsync(
            t => t.Title == "Review outstanding documents for employee exit");
        Assert.Null(failedTask.TaskItemCreatedAt);

        Assert.All(
            dbContext.OffboardingTasks.Where(t => t.Title != "Review outstanding documents for employee exit"),
            t => Assert.NotNull(t.TaskItemCreatedAt));
    }

    // OFF-03
    [Fact]
    public async Task HandleAsync_Sets_AssignedEmployeeId_On_Generated_Tasks()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var assets = new List<AssignedAssetItem> { new(Guid.NewGuid(), Guid.NewGuid(), "Monitor") };
        var harness = BuildHandler(dbContext, names, managerId: managerId, assignedAssets: assets);

        await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        var assetTask = await dbContext.OffboardingTasks.SingleAsync(t => t.AssignTo == OffboardingTaskAssignTo.Employee);
        Assert.Equal(employeeId, assetTask.AssignedEmployeeId);

        var hrTask = await dbContext.OffboardingTasks.SingleAsync(t => t.AssignTo == OffboardingTaskAssignTo.HR);
        Assert.Null(hrTask.AssignedEmployeeId);

        var managerTasks = dbContext.OffboardingTasks.Where(t => t.AssignTo == OffboardingTaskAssignTo.Manager);
        Assert.All(managerTasks, t => Assert.Equal(managerId, t.AssignedEmployeeId));
    }

    // OFF-03
    [Fact]
    public async Task HandleAsync_Manager_Tasks_Have_Null_AssignedEmployeeId_When_No_Manager()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildHandler(dbContext, names, managerId: null);

        await harness.Handler.HandleAsync(BuildRequest(companyId, employeeId), CancellationToken.None);

        var managerTasks = dbContext.OffboardingTasks.Where(t => t.AssignTo == OffboardingTaskAssignTo.Manager);
        Assert.All(managerTasks, t => Assert.Null(t.AssignedEmployeeId));
    }
}
