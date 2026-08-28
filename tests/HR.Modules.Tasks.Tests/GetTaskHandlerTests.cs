using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Tasks.Features.GetTask;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class GetTaskHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    // Defaults to an HR-Administrator caller so tests unrelated to IAM-07 authorization
    // (pre-existing not-found/status-mapping behavior) don't need to wire up assignee/manager
    // relationships just to get past the authorization check — mirrors
    // CompleteTaskHandlerTests.BuildHandler's identical default.
    private static GetTaskHandler BuildHandler(TasksDbContext context) =>
        new(context, new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(HrAdministratorRoleId),
            new FakeDirectReportsReader()));

    [Fact]
    public async Task HandleAsync_Returns_Task_When_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var due = new DateOnly(2026, 9, 1);

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, createdBy,
            "Review contract", "Check all clauses",
            TaskPriority.High, TaskSource.Document, TaskActionType.Complete,
            due, assignedEmployee, null, Now);

        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = task.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var v = result.Value!;
        Assert.Equal(task.Id, v.Id);
        Assert.Equal(companyId, v.CompanyId);
        Assert.Equal("Review contract", v.Title);
        Assert.Equal("Check all clauses", v.Description);
        Assert.Equal("Open", v.Status);
        Assert.Equal("High", v.Priority);
        Assert.Equal("Document", v.Source);
        Assert.Equal(due, v.DueDate);
        Assert.Equal(assignedEmployee, v.AssignedEmployeeId);
        Assert.Null(v.AssignedUserId);
        Assert.Equal(createdBy, v.CreatedBy);
        Assert.Null(v.CompletedBy);
        Assert.Null(v.CompletedAt);
        Assert.Equal(Now, v.CreatedAt);
        Assert.Equal(Now, v.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Task_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context).HandleAsync(
            new GetTaskRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Task_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();

        var task = TaskItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Some task", null, TaskPriority.Low, TaskSource.Workflow, TaskActionType.Complete,
            null, null, null, Now);

        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new GetTaskRequest { CompanyId = Guid.NewGuid(), Id = task.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_CompletedBy_And_CompletedAt_For_Completed_Task()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var completedAt = Now.AddHours(2);

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Completed task", null, TaskPriority.Medium, TaskSource.System, TaskActionType.Complete,
            null, null, null, Now);
        task.Complete(completedBy, completedAt);

        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = task.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);
        Assert.Equal(completedBy, result.Value.CompletedBy);
        Assert.Equal(completedAt, result.Value.CompletedAt);
    }

    // ---- IAM-07: authorization matrix ----

    [Fact]
    public async Task HandleAsync_Allows_Assignee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, Now);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = new GetTaskHandler(context, new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader()));

        var result = await handler.HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = task.Id, CallerEmployeeId = assignedEmployee },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Allows_Assignees_Direct_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var manager = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, Now);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = new GetTaskHandler(context, new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader(assignedEmployee)));

        var result = await handler.HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = task.Id, CallerEmployeeId = manager },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Allows_Assignees_Skip_Level_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid(); // A
        var skipLevelManager = Guid.NewGuid(); // C, A's manager's manager

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, Now);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        // C's full descendant tree (via GetAllDescendantIdsAsync) includes A even though C is not
        // A's direct manager.
        var handler = new GetTaskHandler(context, new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader(assignedEmployee)));

        var result = await handler.HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = task.Id, CallerEmployeeId = skipLevelManager },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_For_Unrelated_Peer()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var unrelatedPeer = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, Now);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = new GetTaskHandler(context, new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader()));

        var result = await handler.HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = task.Id, CallerEmployeeId = unrelatedPeer },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_For_Unrelated_Peer_When_Task_Assigned_By_AssignedUserId_Only()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedUser = Guid.NewGuid();
        var unrelatedPeer = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, null, assignedUser, Now);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = new GetTaskHandler(context, new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader()));

        var result = await handler.HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = task.Id, CallerEmployeeId = unrelatedPeer },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Manager_Of_AssignedUserId_Only_Task()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedUser = Guid.NewGuid();
        var manager = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, null, assignedUser, Now);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = new GetTaskHandler(context, new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader(assignedUser)));

        var result = await handler.HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = task.Id, CallerEmployeeId = manager },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_For_Unassigned_Task_Requested_By_NonHrAdmin()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var caller = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, null, null, Now);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = new GetTaskHandler(context, new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(), new FakeDirectReportsReader()));

        var result = await handler.HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = task.Id, CallerEmployeeId = caller },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_HrAdministrator_To_View_Unassigned_Task()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var hrAdmin = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, null, null, Now);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = new GetTaskHandler(context, new TasksResourceAuthorizer(
            new FakeRoleAuthorizationService(HrAdministratorRoleId), new FakeDirectReportsReader()));

        var result = await handler.HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = task.Id, CallerEmployeeId = hrAdmin },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TasksDbContext(options);
    }
}
