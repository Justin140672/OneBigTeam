using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CompleteTask;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class CompleteTaskHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock Clock = new(FixedNow);

    private static readonly TaskCompletionDispatcher NoOpDispatcher =
        new(Enumerable.Empty<HR.SharedKernel.Contracts.ITaskCompletionAction>());

    private static CompleteTaskHandler BuildHandler(TasksDbContext context, FakeAuditPublisher? audit = null) =>
        new(context, Clock, audit ?? new FakeAuditPublisher(), NoOpDispatcher);

    private static TaskItem MakeTask(Guid companyId, TaskItemStatus status = TaskItemStatus.Open)
    {
        var t = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Task", null, TaskPriority.Medium, TaskSource.Manual,
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
            TaskPriority.Critical, TaskSource.Sickness,
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

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TasksDbContext(options);
    }
}
