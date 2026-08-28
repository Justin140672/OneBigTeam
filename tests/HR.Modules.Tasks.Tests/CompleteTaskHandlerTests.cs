using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CompleteTask;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class CompleteTaskHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock Clock = new(FixedNow);

    private static readonly TaskCompletionDispatcher NoOpDispatcher =
        new(Enumerable.Empty<ITaskCompletionAction>());

    private static readonly Guid HrAdministratorRoleId = new("00000000-0000-0000-0000-000000000004");
    private static readonly Guid CompanyAdministratorRoleId = new("00000000-0000-0000-0000-000000000006");

    private static CompleteTaskHandler BuildHandler(
        TasksDbContext context,
        FakeAuditPublisher? audit = null,
        FakeNotificationWriter? notif = null,
        FakeRoleAuthorizationService? authorizationService = null,
        FakeDirectReportsReader? directReportsReader = null) =>
        new(context, notif ?? new FakeNotificationWriter(), Clock, audit ?? new FakeAuditPublisher(), NoOpDispatcher,
            // Defaults to an HR-Administrator caller so tests unrelated to SEC-003/IAM-07
            // authorization (pre-existing behavior around completion/notification/audit) don't
            // need to wire up assignee/manager relationships just to get past the authorization
            // check.
            new TasksResourceAuthorizer(
                authorizationService ?? new FakeRoleAuthorizationService(HrAdministratorRoleId),
                directReportsReader ?? new FakeDirectReportsReader()));

    private static TaskItem MakeTask(Guid companyId, TaskItemStatus status = TaskItemStatus.Open)
    {
        var t = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, null, null, DateTimeOffset.UtcNow);

        if (status == TaskItemStatus.InProgress) t.Start(DateTimeOffset.UtcNow);
        if (status == TaskItemStatus.Completed)  t.Complete(Guid.NewGuid(), DateTimeOffset.UtcNow);
        if (status == TaskItemStatus.Cancelled)  t.Cancel(DateTimeOffset.UtcNow);

        return t;
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Task_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var result = await BuildHandler(context).HandleAsync(
            new CompleteTaskRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid(), CompletedBy = Guid.NewGuid() },
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
            new CompleteTaskRequest { CompanyId = Guid.NewGuid(), Id = task.Id, CompletedBy = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Task_Is_Cancelled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var task = MakeTask(companyId, TaskItemStatus.Cancelled);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Completes_An_Open_Task()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var task = MakeTask(companyId, TaskItemStatus.Open);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = completedBy },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);
        Assert.Equal(completedBy, result.Value.CompletedBy);
        Assert.Equal(new DateTimeOffset(FixedNow), result.Value.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_Completes_An_InProgress_Task()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var task = MakeTask(companyId, TaskItemStatus.InProgress);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = completedBy },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);
        Assert.Equal(completedBy, result.Value.CompletedBy);
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_When_Task_Already_Completed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var task = MakeTask(companyId, TaskItemStatus.Completed);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);
    }

    [Fact]
    public async Task HandleAsync_Persists_Changes_To_Database()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var task = MakeTask(companyId);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        await BuildHandler(context).HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = completedBy },
            CancellationToken.None);

        var persisted = await context.TaskItems.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskItemStatus.Completed, persisted.Status);
        Assert.Equal(completedBy, persisted.CompletedBy);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Full_Task_Snapshot()
    {
        await using var context = BuildContext();
        var companyId        = Guid.NewGuid();
        var completedBy      = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Complete me", "Details",
            TaskPriority.Critical, TaskSource.Sickness, TaskActionType.Complete,
            new DateOnly(2026, 7, 1), assignedEmployee, null, DateTimeOffset.UtcNow);

        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await BuildHandler(context).HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = completedBy },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var r = result.Value!;
        Assert.Equal(task.Id, r.Id);
        Assert.Equal("Complete me", r.Title);
        Assert.Equal("Details", r.Description);
        Assert.Equal("Completed", r.Status);
        Assert.Equal("Critical", r.Priority);
        Assert.Equal("Sickness", r.Source);
        Assert.Equal(new DateOnly(2026, 7, 1), r.DueDate);
        Assert.Equal(assignedEmployee, r.AssignedEmployeeId);
        Assert.Equal(completedBy, r.CompletedBy);
        Assert.Equal(new DateTimeOffset(FixedNow), r.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_Publishes_TaskCompleted_Audit_Event_With_Previous_Status()
    {
        await using var context = BuildContext();
        var companyId   = Guid.NewGuid();
        var completedBy = Guid.NewGuid();
        var audit       = new FakeAuditPublisher();

        var task = MakeTask(companyId, TaskItemStatus.InProgress);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        await BuildHandler(context, audit).HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = completedBy },
            CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("task.completed", evt.EventType);
        Assert.Equal(task.Id, evt.EntityId);
        Assert.Equal(completedBy, evt.ActorUserId);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_With_EmployeeId_From_Assignment()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployeeId = Guid.NewGuid();
        var audit = new FakeAuditPublisher();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployeeId, null, DateTimeOffset.UtcNow);
        task.Start(DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        await BuildHandler(context, audit).HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = Guid.NewGuid() },
            CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal(assignedEmployeeId, evt.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Task_Not_Found()
    {
        await using var context = BuildContext();
        var audit = new FakeAuditPublisher();

        await BuildHandler(context, audit).HandleAsync(
            new CompleteTaskRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid(), CompletedBy = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Task_Is_Cancelled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var audit     = new FakeAuditPublisher();

        var task = MakeTask(companyId, TaskItemStatus.Cancelled);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        await BuildHandler(context, audit).HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Writes_TaskCompleted_Notification_To_Assigned_Employee()
    {
        await using var context = BuildContext();
        var companyId        = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var notif            = new FakeNotificationWriter();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Onboarding checklist", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, DateTimeOffset.UtcNow);

        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        await BuildHandler(context, notif: notif).HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = Guid.NewGuid() },
            CancellationToken.None);

        var written = Assert.Single(notif.Written);
        Assert.Equal(companyId,        written.CompanyId);
        Assert.Equal(assignedEmployee, written.EmployeeId);
        Assert.Equal(task.Id,          written.SourceEntityId);
        Assert.Equal(NotificationType.TaskCompleted, written.Type);
        Assert.Equal(NotificationPriority.Normal,    written.Priority);
        Assert.Contains("Onboarding checklist",      written.Title);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Write_Notification_When_No_Employee_Assigned()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var notif     = new FakeNotificationWriter();

        var task = MakeTask(companyId);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        await BuildHandler(context, notif: notif).HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(notif.Written);
    }

    // ---- SEC-003: authorization matrix ----

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var unrelatedPeer = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = unrelatedPeer },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Assignee_By_AssignedEmployeeId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = assignedEmployee },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Allows_Assignee_By_AssignedUserId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedUser = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, null, assignedUser, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = assignedUser },
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
            null, assignedEmployee, null, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader(assignedEmployee));

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = manager },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_For_Manager_Who_Does_Not_Manage_Assignee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var otherManager = Guid.NewGuid();
        var someoneElsesReport = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader(someoneElsesReport));

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = otherManager },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_HrAdministrator_Even_When_Not_Assignee_Or_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var hrAdmin = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(HrAdministratorRoleId),
            directReportsReader: new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = hrAdmin },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_For_Unassigned_Task_Completed_By_NonHrAdmin()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var caller = Guid.NewGuid();

        var task = MakeTask(companyId); // AssignedEmployeeId and AssignedUserId both null
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = caller },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_HrAdministrator_To_Complete_Unassigned_Task()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var hrAdmin = Guid.NewGuid();

        var task = MakeTask(companyId); // AssignedEmployeeId and AssignedUserId both null
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(HrAdministratorRoleId),
            directReportsReader: new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = hrAdmin },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_Even_When_Task_Already_Completed()
    {
        // Authorization must be checked before the already-completed short-circuit; an
        // unauthorized caller must not be able to "complete" an already-completed task to
        // silently succeed.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var unrelatedPeer = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, DateTimeOffset.UtcNow);
        task.Complete(assignedEmployee, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = unrelatedPeer },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_Not_Forbidden_When_Cancelled_Task_Completed_By_Authorized_Assignee()
    {
        // Authorization is checked first, but for an authorized caller the pre-existing
        // cancelled-task Conflict behavior must be unchanged.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, DateTimeOffset.UtcNow);
        task.Cancel(DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = assignedEmployee },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Allows_Skip_Level_Manager_In_Three_Level_Hierarchy()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid(); // A
        var skipLevelManager = Guid.NewGuid(); // C, A's manager's manager

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        // C's full descendant tree (via GetAllDescendantIdsAsync) includes A even though C is not
        // A's direct manager.
        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader(assignedEmployee));

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = skipLevelManager },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Returns_Forbidden_For_CompanyAdministrator_Who_Is_Not_Assignee_Or_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var assignedEmployee = Guid.NewGuid();
        var companyAdmin = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, assignedEmployee, null, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(CompanyAdministratorRoleId),
            directReportsReader: new FakeDirectReportsReader());

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = companyAdmin },
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
            null, null, assignedUser, DateTimeOffset.UtcNow);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var handler = BuildHandler(
            context,
            authorizationService: new FakeRoleAuthorizationService(),
            directReportsReader: new FakeDirectReportsReader(assignedUser));

        var result = await handler.HandleAsync(
            new CompleteTaskRequest { CompanyId = companyId, Id = task.Id, CompletedBy = manager },
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
