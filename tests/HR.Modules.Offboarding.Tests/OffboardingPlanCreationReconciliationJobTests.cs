using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Features.StartOffboarding;
using HR.Modules.Offboarding.Jobs;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Modules.Offboarding.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Offboarding.Tests;

// OFF-03: this job is the recovery path for both halves of "starting a leaving process results in
// exactly one active offboarding plan, whose tasks all eventually get a Tasks-module counterpart" —
// missing plans (the automatic trigger never ran / failed outright) and partially-synced plans (the
// plan is durable but one or more OffboardingTask rows never got their TaskItem created).
public class OffboardingPlanCreationReconciliationJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 25, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed record Harness(
        OffboardingPlanCreationReconciliationJob Job,
        FakeTaskCreator TaskCreator);

    private static Harness BuildJob(
        OffboardingDbContext dbContext,
        IReadOnlyList<ActiveLeavingProcessItem>? inProgressLeavingProcesses = null,
        Dictionary<Guid, string>? employeeNames = null,
        FakeAssignedAssetReader? assignedAssetReader = null)
    {
        var taskCreator = new FakeTaskCreator();
        var taskSynchronizer = new OffboardingTaskSynchronizer(
            dbContext, taskCreator, new FakeClock(FixedUtcNow), NullLogger<OffboardingTaskSynchronizer>.Instance);

        var startOffboardingHandler = new StartOffboardingHandler(
            dbContext,
            new FakeClock(FixedUtcNow),
            new FakeEmployeeNameReader(employeeNames),
            new FakeManagerReader(null),
            assignedAssetReader ?? new FakeAssignedAssetReader(null),
            new FakeOutstandingDocumentRequestReader(null),
            taskSynchronizer,
            new FakeNotificationWriter(),
            new CapturingIntegrationEventPublisher(),
            new FakeCompanyLeavingSettingsReader(),
            new FakeHrAdministratorDirectory(),
            new FakeDirectReportsReader(),
            new FakeTaskReassigner());

        var activeLeavingProcessReader = new FakeActiveLeavingProcessReader(inProgressLeavingProcesses);

        var job = new OffboardingPlanCreationReconciliationJob(
            dbContext,
            activeLeavingProcessReader,
            assignedAssetReader ?? new FakeAssignedAssetReader(null),
            startOffboardingHandler,
            taskSynchronizer,
            new FakeClock(FixedUtcNow),
            NullLogger<OffboardingPlanCreationReconciliationJob>.Instance);

        return new Harness(job, taskCreator);
    }

    private static OffboardingPlan CreateActivePlan(OffboardingDbContext dbContext, Guid companyId, Guid employeeId)
    {
        var plan = OffboardingPlan.Create(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 9, 1), null, Now.AddDays(-1));
        plan.Start(Now.AddDays(-1));
        dbContext.OffboardingPlans.Add(plan);
        return plan;
    }

    [Fact]
    public async Task ExecuteAsync_Is_A_NoOp_When_Nothing_Is_Pending()
    {
        await using var dbContext = BuildContext();
        var harness = BuildJob(dbContext);

        await harness.Job.ExecuteAsync();

        Assert.Empty(dbContext.OffboardingPlans);
        Assert.Empty(harness.TaskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Missing_Plan_For_InProgress_Leaving_Process_With_No_Active_Plan()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildJob(
            dbContext,
            [new ActiveLeavingProcessItem(companyId, employeeId, new DateOnly(2026, 9, 1))],
            names);

        await harness.Job.ExecuteAsync();

        var plan = Assert.Single(dbContext.OffboardingPlans);
        Assert.Equal(companyId, plan.CompanyId);
        Assert.Equal(employeeId, plan.EmployeeId);
        Assert.Equal(OffboardingStatus.InProgress, plan.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Create_A_Second_Plan_When_An_Active_Plan_Already_Exists()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        CreateActivePlan(dbContext, companyId, employeeId);
        await dbContext.SaveChangesAsync();

        var names = new Dictionary<Guid, string> { [employeeId] = "Jamie Smith" };
        var harness = BuildJob(
            dbContext,
            [new ActiveLeavingProcessItem(companyId, employeeId, new DateOnly(2026, 9, 1))],
            names);

        await harness.Job.ExecuteAsync();

        Assert.Single(dbContext.OffboardingPlans);
    }

    [Fact]
    public async Task ExecuteAsync_One_Employees_Plan_Creation_Failure_Does_Not_Prevent_Another_From_Being_Created()
    {
        await using var dbContext = BuildContext();
        var companyIdA = Guid.NewGuid();
        var employeeIdA = Guid.NewGuid(); // no name registered -> StartOffboardingHandler returns NotFound (isolated failure)
        var companyIdB = Guid.NewGuid();
        var employeeIdB = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [employeeIdB] = "Jamie Smith" };

        var harness = BuildJob(
            dbContext,
            [
                new ActiveLeavingProcessItem(companyIdA, employeeIdA, new DateOnly(2026, 9, 1)),
                new ActiveLeavingProcessItem(companyIdB, employeeIdB, new DateOnly(2026, 9, 1)),
            ],
            names);

        await harness.Job.ExecuteAsync();

        var plan = Assert.Single(dbContext.OffboardingPlans);
        Assert.Equal(companyIdB, plan.CompanyId);
        Assert.Equal(employeeIdB, plan.EmployeeId);
    }

    [Fact]
    public async Task ExecuteAsync_Syncs_Outstanding_TaskItem_For_A_Partially_Synced_Plan()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var plan = CreateActivePlan(dbContext, companyId, employeeId);

        var pendingTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return laptop", null,
            OffboardingTaskAssignTo.Employee, new DateOnly(2026, 9, 1), Now, employeeId);
        dbContext.OffboardingTasks.Add(pendingTask);
        await dbContext.SaveChangesAsync();

        var harness = BuildJob(dbContext);

        await harness.Job.ExecuteAsync();

        var syncedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Id == pendingTask.Id);
        Assert.NotNull(syncedTask.TaskItemCreatedAt);
        Assert.Single(harness.TaskCreator.Created, c => c.Title == "Return laptop");
    }

    [Fact]
    public async Task ExecuteAsync_Leaves_A_Fully_Synced_Plan_Alone_With_No_Extra_TaskCreator_Calls()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var plan = CreateActivePlan(dbContext, companyId, employeeId);

        var syncedTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Already synced", null,
            OffboardingTaskAssignTo.Employee, new DateOnly(2026, 9, 1), Now, employeeId);
        syncedTask.MarkTaskItemCreated(Now);
        dbContext.OffboardingTasks.Add(syncedTask);
        await dbContext.SaveChangesAsync();

        var harness = BuildJob(dbContext);

        await harness.Job.ExecuteAsync();

        Assert.Empty(harness.TaskCreator.Created);
    }

    // ---- OFF-04: AddMissingAssetReturnTasksAsync ----

    [Fact]
    public async Task ExecuteAsync_Creates_AssetReturnTask_For_Asset_Assigned_After_Plan_Was_Created()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var plan = CreateActivePlan(dbContext, companyId, employeeId);
        await dbContext.SaveChangesAsync();

        var newAssignmentId = Guid.NewGuid();
        var assignedAssetReader = new FakeAssignedAssetReader(
            [new AssignedAssetItem(newAssignmentId, Guid.NewGuid(), "Monitor")]);
        var harness = BuildJob(dbContext, assignedAssetReader: assignedAssetReader);

        await harness.Job.ExecuteAsync();

        var task = await dbContext.OffboardingTasks.SingleAsync(t => t.AssetAssignmentId == newAssignmentId);
        Assert.Equal(plan.Id, task.OffboardingPlanId);
        Assert.Contains("Monitor", task.Title);
        Assert.Equal(plan.LastWorkingDay, task.DueDate);
        Assert.NotNull(task.TaskItemCreatedAt); // synced within the same run
        Assert.Single(harness.TaskCreator.Created, c => c.Title.Contains("Monitor"));
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Duplicate_AssetReturnTask_For_Assignment_Already_Represented()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var plan = CreateActivePlan(dbContext, companyId, employeeId);

        var existingAssignmentId = Guid.NewGuid();
        var existingTask = OffboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, "Return asset: Laptop", null,
            OffboardingTaskAssignTo.Employee, plan.LastWorkingDay, Now, employeeId,
            assetAssignmentId: existingAssignmentId);
        existingTask.MarkTaskItemCreated(Now);
        dbContext.OffboardingTasks.Add(existingTask);
        await dbContext.SaveChangesAsync();

        var assignedAssetReader = new FakeAssignedAssetReader(
            [new AssignedAssetItem(existingAssignmentId, Guid.NewGuid(), "Laptop")]);
        var harness = BuildJob(dbContext, assignedAssetReader: assignedAssetReader);

        await harness.Job.ExecuteAsync();

        Assert.Single(dbContext.OffboardingTasks.Where(t => t.AssetAssignmentId == existingAssignmentId));
        Assert.Empty(harness.TaskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Is_A_NoOp_When_No_Assigned_Assets()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        CreateActivePlan(dbContext, companyId, employeeId);
        await dbContext.SaveChangesAsync();

        var assignedAssetReader = new FakeAssignedAssetReader([]);
        var harness = BuildJob(dbContext, assignedAssetReader: assignedAssetReader);

        await harness.Job.ExecuteAsync();

        Assert.Empty(dbContext.OffboardingTasks);
        Assert.Empty(harness.TaskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Is_A_NoOp_When_No_Active_Plans_Even_With_Assigned_Assets()
    {
        await using var dbContext = BuildContext();

        var assignedAssetReader = new FakeAssignedAssetReader(
            [new AssignedAssetItem(Guid.NewGuid(), Guid.NewGuid(), "Monitor")]);
        var harness = BuildJob(dbContext, assignedAssetReader: assignedAssetReader);

        await harness.Job.ExecuteAsync();

        Assert.Empty(dbContext.OffboardingTasks);
        Assert.Empty(harness.TaskCreator.Created);
    }
}
