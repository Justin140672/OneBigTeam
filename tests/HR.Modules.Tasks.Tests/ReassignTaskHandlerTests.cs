using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.ReassignTask;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class ReassignTaskHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FakeClock Clock = new(FixedNow);

    private static TaskItem MakeTask(Guid companyId, Guid? assignedEmployeeId = null, Guid? assignedUserId = null)
    {
        return TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            "Original title", null, TaskPriority.Medium, TaskSource.Manual,
            null, assignedEmployeeId, assignedUserId, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Task_Does_Not_Exist()
    {
        await using var context = BuildContext();

        var result = await new ReassignTaskHandler(context, Clock).HandleAsync(
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

        var result = await new ReassignTaskHandler(context, Clock).HandleAsync(
            new ReassignTaskRequest { CompanyId = Guid.NewGuid(), Id = task.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Updates_AssignedEmployeeId_And_AssignedUserId()
    {
        await using var context = BuildContext();
        var companyId    = Guid.NewGuid();
        var newEmployee  = Guid.NewGuid();
        var newUser      = Guid.NewGuid();

        var task = MakeTask(companyId);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await new ReassignTaskHandler(context, Clock).HandleAsync(
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

        var result = await new ReassignTaskHandler(context, Clock).HandleAsync(
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

        var result = await new ReassignTaskHandler(context, Clock).HandleAsync(
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

        await new ReassignTaskHandler(context, Clock).HandleAsync(
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
        var companyId  = Guid.NewGuid();
        var createdBy  = Guid.NewGuid();
        var newEmployee = Guid.NewGuid();

        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, createdBy,
            "My task", "Description", TaskPriority.High, TaskSource.Leave,
            new DateOnly(2026, 9, 1), null, null, DateTimeOffset.UtcNow);

        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var result = await new ReassignTaskHandler(context, Clock).HandleAsync(
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

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TasksDbContext(options);
    }
}
