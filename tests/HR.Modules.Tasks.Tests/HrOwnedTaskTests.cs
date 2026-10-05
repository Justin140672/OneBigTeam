using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.GetTask;
using HR.Modules.Tasks.Features.GetUnassignedTasks;
using HR.Modules.Tasks.Features.ReassignTask;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class HrOwnedTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    private static TasksDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static TasksResourceAuthorizer Authorizer(bool isHr) =>
        new(isHr ? new FakeRoleAuthorizationService(HrAdministratorRoleId) : new FakeRoleAuthorizationService(),
            new FakeDirectReportsReader());

    private static async Task<(Guid TaskId, FakeNotificationWriter Notifications, FakeAuditPublisher Audit)> CreateHrTaskAsync(
        TasksDbContext context, Guid companyId)
    {
        var notifications = new FakeNotificationWriter();
        var audit = new FakeAuditPublisher();
        var creator = new TaskCreator(context, notifications, new FakeClock(Now.UtcDateTime), audit);

        var id = await creator.CreateForHrAsync(
            companyId, Guid.NewGuid(), "Right-to-work checks — Alex", "Verify documents",
            TaskPriority.Critical, TaskSource.Onboarding, TaskActionType.Complete,
            new DateOnly(2026, 7, 2), Guid.NewGuid(), CancellationToken.None);

        return (id, notifications, audit);
    }

    [Fact]
    public async Task CreateForHrAsync_Creates_Task_Owned_By_HR_With_No_Individual_Assignee_Or_Notification()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var (id, notifications, _) = await CreateHrTaskAsync(context, companyId);

        var task = await context.TaskItems.SingleAsync(t => t.Id == id);
        Assert.True(task.AssignedToHr);
        Assert.Null(task.AssignedEmployeeId);
        Assert.Null(task.AssignedUserId);
        Assert.Equal(companyId, task.CompanyId);
        Assert.Equal(TaskPriority.Critical, task.Priority);
        Assert.Equal(TaskSource.Onboarding, task.Source);
        Assert.Equal(new DateOnly(2026, 7, 2), task.DueDate);
        Assert.Empty(notifications.Written);
    }

    [Fact]
    public async Task CreateAsync_Does_Not_Mark_Task_As_HR_Owned()
    {
        await using var context = BuildContext();
        var creator = new TaskCreator(context, new FakeNotificationWriter(), new FakeClock(Now.UtcDateTime), new FakeAuditPublisher());

        var id = await creator.CreateAsync(
            Guid.NewGuid(), Guid.NewGuid(), "Plain", null, TaskPriority.Low, TaskSource.Workflow,
            TaskActionType.Complete, null, null, null, null, CancellationToken.None);

        Assert.False((await context.TaskItems.SingleAsync(t => t.Id == id)).AssignedToHr);
    }

    [Fact]
    public void Create_Ignores_HR_Flag_When_An_Individual_Assignee_Is_Supplied()
    {
        var task = TaskItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "T", null, TaskPriority.Low, TaskSource.Workflow,
            TaskActionType.Complete, null, Guid.NewGuid(), null, Now, assignedToHr: true);

        Assert.False(task.AssignedToHr);
    }

    [Fact]
    public async Task Unassigned_Inbox_Lists_HR_Owned_Tasks_With_Flag_And_Plain_Unassigned_Without()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (hrTaskId, _, _) = await CreateHrTaskAsync(context, companyId);
        var unassigned = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Nobody", null, TaskPriority.Low, TaskSource.Workflow,
            TaskActionType.Complete, null, null, null, Now);
        var assignedToPerson = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Person", null, TaskPriority.Low, TaskSource.Workflow,
            TaskActionType.Complete, null, Guid.NewGuid(), null, Now);
        context.TaskItems.AddRange(unassigned, assignedToPerson);
        await context.SaveChangesAsync();

        var response = await new GetUnassignedTasksHandler(context).HandleAsync(
            new GetUnassignedTasksRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        Assert.True(Assert.Single(response.Items, i => i.Id == hrTaskId).AssignedToHr);
        Assert.False(Assert.Single(response.Items, i => i.Id == unassigned.Id).AssignedToHr);
    }

    [Fact]
    public async Task Unassigned_Inbox_Does_Not_Leak_HR_Tasks_Across_Companies()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        await CreateHrTaskAsync(context, Guid.NewGuid());

        var response = await new GetUnassignedTasksHandler(context).HandleAsync(
            new GetUnassignedTasksRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetTask_Allows_HR_Administrator_And_Reports_HR_Ownership()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (id, _, _) = await CreateHrTaskAsync(context, companyId);

        var result = await new GetTaskHandler(context, Authorizer(isHr: true)).HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = id, CallerEmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AssignedToHr);
        Assert.Null(result.Value.AssignedEmployeeId);
    }

    [Fact]
    public async Task GetTask_Forbids_Callers_Without_HR_Role()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (id, _, _) = await CreateHrTaskAsync(context, companyId);

        var result = await new GetTaskHandler(context, Authorizer(isHr: false)).HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = id, CallerEmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Reassign_Forbids_Callers_Without_HR_Role()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (id, _, _) = await CreateHrTaskAsync(context, companyId);
        var actor = Guid.NewGuid();

        var result = await BuildReassignHandler(context, isHr: false).HandleAsync(
            new ReassignTaskRequest { CompanyId = companyId, Id = id, AssignedEmployeeId = actor, AssignedUserId = actor, ActorUserId = actor },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.True((await context.TaskItems.SingleAsync(t => t.Id == id)).AssignedToHr);
    }

    [Fact]
    public async Task Claiming_An_HR_Task_Assigns_It_To_The_Claimant_And_Clears_HR_Ownership()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var (id, _, _) = await CreateHrTaskAsync(context, companyId);
        var hrUser = Guid.NewGuid();

        var result = await BuildReassignHandler(context, isHr: true).HandleAsync(
            new ReassignTaskRequest { CompanyId = companyId, Id = id, AssignedEmployeeId = hrUser, AssignedUserId = hrUser, ActorUserId = hrUser },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var task = await context.TaskItems.SingleAsync(t => t.Id == id);
        Assert.False(task.AssignedToHr);
        Assert.Equal(hrUser, task.AssignedEmployeeId);
    }

    private static ReassignTaskHandler BuildReassignHandler(TasksDbContext context, bool isHr) =>
        new(context, new FakeNotificationWriter(), new FakeClock(Now.UtcDateTime), new FakeAuditPublisher(), Authorizer(isHr));
}
