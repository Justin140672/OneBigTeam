using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.ReassignTask;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class ReassignTaskHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock Clock = new(FixedNow);
    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");

    private static ReassignTaskHandler BuildHandler(
        TasksDbContext context,
        FakeAuditPublisher? audit = null,
        FakeNotificationWriter? notif = null,
        FakeRoleAuthorizationService? authorizationService = null,
        FakeDirectReportsReader? directReportsReader = null) =>
        new(context, notif ?? new FakeNotificationWriter(), Clock, audit ?? new FakeAuditPublisher(),
            // DSH-01: defaults to an HR-Administrator caller so tests unrelated to reassignment
            // authorization don't need to wire up assignee/manager relationships.
            new TasksResourceAuthorizer(
                authorizationService ?? new FakeRoleAuthorizationService(HrAdministratorRoleId),
                directReportsReader ?? new FakeDirectReportsReader()));

    private static TaskItem MakeTask(Guid companyId, Guid? assignedEmployeeId = null, Guid? assignedUserId = null)
    {
        return TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Original title", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployeeId, assignedUserId, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Task_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context).HandleAsync(
            new ReassignTaskRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Task_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var task = MakeTask(Guid.NewGuid());
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ReassignTaskRequest { CompanyId = Guid.NewGuid(), Id = task.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Updates_AssignedEmployeeId_And_AssignedUserId()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var newEmployee = Guid.NewGuid();
        var newUser     = Guid.NewGuid();

        var task = MakeTask(companyId);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ReassignTaskRequest
            {
                CompanyId = companyId,
                Id = task.Id,
                AssignedEmployeeId = newEmployee,
                AssignedUserId = newUser
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newEmployee, result.Value!.AssignedEmployeeId);
        Assert.Equal(newUser, result.Value.AssignedUserId);
    }

    [Fact]
    public async Task HandleAsync_Clears_Assignment_When_Both_Are_Null()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var task = MakeTask(companyId, assignedEmployeeId: Guid.NewGuid(), assignedUserId: Guid.NewGuid());
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ReassignTaskRequest { CompanyId = companyId, Id = task.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.AssignedEmployeeId);
        Assert.Null(result.Value.AssignedUserId);
    }

    [Fact]
    public async Task HandleAsync_Updates_UpdatedAt()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var task = MakeTask(companyId);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ReassignTaskRequest { CompanyId = companyId, Id = task.Id, AssignedEmployeeId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(FixedNow), result.Value!.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Persists_Changes_To_Database()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var newEmployee = Guid.NewGuid();

        var task = MakeTask(companyId);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        await BuildHandler(context).HandleAsync(
            new ReassignTaskRequest { CompanyId = companyId, Id = task.Id, AssignedEmployeeId = newEmployee },
            CancellationToken.None);

        var persisted = await context.TaskItems.AsNoTracking()
            .SingleAsync(t => t.Id == task.Id);

        Assert.Equal(newEmployee, persisted.AssignedEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Full_Task_Snapshot()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var createdBy   = Guid.NewGuid();
        var newEmployee = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, createdBy,
            "My task", "Description", TaskPriority.High, TaskSource.Leave, TaskActionType.Approve,
            new DateOnly(2026, 9, 1), null, null, DateTimeOffset.UtcNow);

        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new ReassignTaskRequest { CompanyId = companyId, Id = task.Id, AssignedEmployeeId = newEmployee },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var r = result.Value!;
        Assert.Equal(task.Id, r.Id);
        Assert.Equal(companyId, r.CompanyId);
        Assert.Equal("My task", r.Title);
        Assert.Equal("Description", r.Description);
        Assert.Equal("Open", r.Status);
        Assert.Equal("High", r.Priority);
        Assert.Equal("Leave", r.Source);
        Assert.Equal(new DateOnly(2026, 9, 1), r.DueDate);
        Assert.Equal(newEmployee, r.AssignedEmployeeId);
        Assert.Null(r.AssignedUserId);
        Assert.Equal(createdBy, r.CreatedBy);
        Assert.Null(r.CompletedBy);
        Assert.Null(r.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_Publishes_TaskReassigned_Audit_Event()
    {
        await using var context = BuildContext();
        var companyId       = Guid.NewGuid();
        var actorUserId     = Guid.NewGuid();
        var previousEmployee = Guid.NewGuid();
        var newEmployee     = Guid.NewGuid();
        var audit           = new FakeAuditPublisher();

        var task = MakeTask(companyId, assignedEmployeeId: previousEmployee);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        await BuildHandler(context, audit).HandleAsync(
            new ReassignTaskRequest
            {
                CompanyId = companyId,
                Id = task.Id,
                AssignedEmployeeId = newEmployee,
                ActorUserId = actorUserId
            },
            CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("task.updated", evt.EventType);
        Assert.Equal(task.Id, evt.EntityId);
        Assert.Equal(actorUserId, evt.ActorUserId);
        Assert.Equal(newEmployee, evt.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Task_Not_Found()
    {
        await using var context = BuildContext();
        var audit = new FakeAuditPublisher();

        await BuildHandler(context, audit).HandleAsync(
            new ReassignTaskRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Writes_Notification_When_New_Employee_Assigned()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var newEmployee = Guid.NewGuid();
        var notif       = new FakeNotificationWriter();

        var task = MakeTask(companyId);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        await BuildHandler(context, notif: notif).HandleAsync(
            new ReassignTaskRequest { CompanyId = companyId, Id = task.Id, AssignedEmployeeId = newEmployee },
            CancellationToken.None);

        var written = Assert.Single(notif.Written);
        Assert.Equal(companyId,   written.CompanyId);
        Assert.Equal(newEmployee, written.EmployeeId);
        Assert.Equal(task.Id,     written.SourceEntityId);
        Assert.Equal(NotificationType.TaskAssigned, written.Type);
        Assert.Equal(NotificationPriority.Normal,   written.Priority);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Write_Notification_When_Assigned_To_Same_Employee()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var employeeId  = Guid.NewGuid();
        var notif       = new FakeNotificationWriter();

        var task = MakeTask(companyId, assignedEmployeeId: employeeId);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        await BuildHandler(context, notif: notif).HandleAsync(
            new ReassignTaskRequest { CompanyId = companyId, Id = task.Id, AssignedEmployeeId = employeeId },
            CancellationToken.None);

        Assert.Empty(notif.Written);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Write_Notification_When_Employee_Cleared()
    {
        await using var context = BuildContext();
        var companyId  = Guid.NewGuid();
        var notif      = new FakeNotificationWriter();

        var task = MakeTask(companyId, assignedEmployeeId: Guid.NewGuid());
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        await BuildHandler(context, notif: notif).HandleAsync(
            new ReassignTaskRequest { CompanyId = companyId, Id = task.Id, AssignedEmployeeId = null },
            CancellationToken.None);

        Assert.Empty(notif.Written);
    }

    // ── DSH-01: resource-ownership authorization ──────────────────────────────

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_For_Unrelated_Caller()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignee = Guid.NewGuid();
        var unrelated = Guid.NewGuid();

        var task = MakeTask(companyId, assignedEmployeeId: assignee);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new ReassignTaskRequest
            {
                CompanyId = companyId,
                Id = task.Id,
                AssignedEmployeeId = Guid.NewGuid(),
                ActorUserId = unrelated
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Manager_In_Assignees_Hierarchy()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignee = Guid.NewGuid();
        var manager = Guid.NewGuid();

        var task = MakeTask(companyId, assignedEmployeeId: assignee);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader(assignee));

        var result = await handler.HandleAsync(
            new ReassignTaskRequest
            {
                CompanyId = companyId,
                Id = task.Id,
                AssignedEmployeeId = Guid.NewGuid(),
                ActorUserId = manager
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_For_Unassigned_Task_When_Caller_Not_HrAdministrator()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var task = MakeTask(companyId);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new ReassignTaskRequest
            {
                CompanyId = companyId,
                Id = task.Id,
                AssignedEmployeeId = Guid.NewGuid(),
                ActorUserId = Guid.NewGuid()
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TasksDbContext(options);
    }
}
