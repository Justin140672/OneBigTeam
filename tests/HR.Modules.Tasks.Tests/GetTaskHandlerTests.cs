using HR.Modules.Tasks.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Tasks.Features.GetTask;
using HR.Modules.Tasks.Persistence;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class GetTaskHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

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

        var result = await new GetTaskHandler(context).HandleAsync(
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

        var result = await new GetTaskHandler(context).HandleAsync(
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

        var result = await new GetTaskHandler(context).HandleAsync(
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

        var result = await new GetTaskHandler(context).HandleAsync(
            new GetTaskRequest { CompanyId = companyId, Id = task.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value!.Status);
        Assert.Equal(completedBy, result.Value.CompletedBy);
        Assert.Equal(completedAt, result.Value.CompletedAt);
    }

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TasksDbContext(options);
    }
}
