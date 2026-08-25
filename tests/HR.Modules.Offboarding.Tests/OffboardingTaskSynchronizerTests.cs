using HR.Modules.Offboarding.Domain;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Modules.Offboarding.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Offboarding.Tests;

// OFF-03: this is the core cross-module-failure-injection seam — OffboardingTaskSynchronizer is
// the only place a Tasks-module TaskItem is actually created for an already-durable OffboardingTask
// row, and the only place a failure of that cross-module call is handled.
public class OffboardingTaskSynchronizerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static OffboardingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OffboardingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static OffboardingTaskSynchronizer BuildSynchronizer(
        OffboardingDbContext dbContext, FakeTaskCreator taskCreator) =>
        new(dbContext, taskCreator, new FakeClock(FixedUtcNow), NullLogger<OffboardingTaskSynchronizer>.Instance);

    private static OffboardingTask AddPendingTask(
        OffboardingDbContext dbContext, Guid companyId, Guid planId, string title,
        Guid? assignedEmployeeId = null, OffboardingTaskStatus status = OffboardingTaskStatus.Pending)
    {
        var task = OffboardingTask.Create(
            Guid.NewGuid(), companyId, planId, title, description: null,
            OffboardingTaskAssignTo.Employee, dueDate: new DateOnly(2026, 8, 1), now: Now,
            assignedEmployeeId: assignedEmployeeId);

        if (status == OffboardingTaskStatus.Completed)
            task.Complete(Now);
        else if (status == OffboardingTaskStatus.Skipped)
            task.Skip(Now, "Skipped for test.", Guid.NewGuid());

        dbContext.OffboardingTasks.Add(task);
        return task;
    }

    [Fact]
    public async Task SyncPlanAsync_Creates_TaskItem_And_Stamps_TaskItemCreatedAt_For_Each_Pending_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        AddPendingTask(dbContext, companyId, planId, "Task A");
        AddPendingTask(dbContext, companyId, planId, "Task B");
        await dbContext.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var synchronizer = BuildSynchronizer(dbContext, taskCreator);

        var syncedCount = await synchronizer.SyncPlanAsync(companyId, planId, CancellationToken.None);

        Assert.Equal(2, syncedCount);
        Assert.Equal(2, taskCreator.Created.Count);
        Assert.All(dbContext.OffboardingTasks, t => Assert.Equal(Now, t.TaskItemCreatedAt));
    }

    [Fact]
    public async Task SyncPlanAsync_Skips_Tasks_That_Already_Have_A_TaskItem()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var alreadySynced = AddPendingTask(dbContext, companyId, planId, "Already synced");
        alreadySynced.MarkTaskItemCreated(Now.AddDays(-1));
        AddPendingTask(dbContext, companyId, planId, "Still pending");
        await dbContext.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var synchronizer = BuildSynchronizer(dbContext, taskCreator);

        var syncedCount = await synchronizer.SyncPlanAsync(companyId, planId, CancellationToken.None);

        Assert.Equal(1, syncedCount);
        var call = Assert.Single(taskCreator.Created);
        Assert.Equal("Still pending", call.Title);
        Assert.Equal(Now.AddDays(-1), alreadySynced.TaskItemCreatedAt);
    }

    [Theory]
    [InlineData(4)] // Skipped
    [InlineData(3)] // Completed
    public async Task SyncPlanAsync_Never_Syncs_Skipped_Or_Completed_Tasks_Even_When_TaskItemCreatedAt_Is_Null(
        int statusValue)
    {
        var status = (OffboardingTaskStatus)statusValue;
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        AddPendingTask(dbContext, companyId, planId, "Not to be synced", status: status);
        await dbContext.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var synchronizer = BuildSynchronizer(dbContext, taskCreator);

        var syncedCount = await synchronizer.SyncPlanAsync(companyId, planId, CancellationToken.None);

        Assert.Equal(0, syncedCount);
        Assert.Empty(taskCreator.Created);
    }

    [Fact]
    public async Task SyncPlanAsync_Isolates_Failure_To_The_Failing_Task_And_Still_Syncs_The_Rest()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        AddPendingTask(dbContext, companyId, planId, "Fails");
        AddPendingTask(dbContext, companyId, planId, "Succeeds A");
        AddPendingTask(dbContext, companyId, planId, "Succeeds B");
        await dbContext.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        taskCreator.TitlesToFail.Add("Fails");
        var synchronizer = BuildSynchronizer(dbContext, taskCreator);

        var syncedCount = await synchronizer.SyncPlanAsync(companyId, planId, CancellationToken.None);

        Assert.Equal(2, syncedCount);
        Assert.Equal(2, taskCreator.Created.Count);

        var failedTask = await dbContext.OffboardingTasks.SingleAsync(t => t.Title == "Fails");
        Assert.Null(failedTask.TaskItemCreatedAt);

        Assert.All(
            dbContext.OffboardingTasks.Where(t => t.Title != "Fails"),
            t => Assert.NotNull(t.TaskItemCreatedAt));
    }

    [Fact]
    public async Task SyncPlanAsync_Retrying_After_A_Partial_Failure_Completes_The_Previously_Failed_Task()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        AddPendingTask(dbContext, companyId, planId, "Fails then succeeds");
        await dbContext.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        taskCreator.TitlesToFail.Add("Fails then succeeds");
        var synchronizer = BuildSynchronizer(dbContext, taskCreator);

        var firstAttemptCount = await synchronizer.SyncPlanAsync(companyId, planId, CancellationToken.None);
        Assert.Equal(0, firstAttemptCount);
        var task = await dbContext.OffboardingTasks.SingleAsync();
        Assert.Null(task.TaskItemCreatedAt);

        taskCreator.TitlesToFail.Clear();
        var secondAttemptCount = await synchronizer.SyncPlanAsync(companyId, planId, CancellationToken.None);

        Assert.Equal(1, secondAttemptCount);
        Assert.NotNull(task.TaskItemCreatedAt);
    }

    [Fact]
    public async Task SyncPlanAsync_Passes_AssignedEmployeeId_As_AssignedEmployeeId_And_AssignedUserId_And_CreatedBy_Is_SystemActor()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var assignedEmployeeId = Guid.NewGuid();
        AddPendingTask(dbContext, companyId, planId, "Return laptop", assignedEmployeeId);
        await dbContext.SaveChangesAsync();

        var taskCreator = new FakeTaskCreator();
        var synchronizer = BuildSynchronizer(dbContext, taskCreator);

        await synchronizer.SyncPlanAsync(companyId, planId, CancellationToken.None);

        var call = Assert.Single(taskCreator.Created);
        Assert.Equal(assignedEmployeeId, call.AssignedEmployeeId);
        Assert.Equal(assignedEmployeeId, call.AssignedUserId);
        Assert.Equal(OffboardingSystemActor.Id, call.CreatedBy);
        Assert.Equal(Guid.Empty, call.CreatedBy);
    }

    [Fact]
    public async Task SyncPlanAsync_Returns_Zero_And_Does_Nothing_When_No_Pending_Tasks_Exist()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();
        var planId = Guid.NewGuid();

        var taskCreator = new FakeTaskCreator();
        var synchronizer = BuildSynchronizer(dbContext, taskCreator);

        var syncedCount = await synchronizer.SyncPlanAsync(companyId, planId, CancellationToken.None);

        Assert.Equal(0, syncedCount);
        Assert.Empty(taskCreator.Created);
    }
}
