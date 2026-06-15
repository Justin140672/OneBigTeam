using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CreateTask;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class CreateTaskHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_Task_And_Returns_Response()
    {
        await using var context = BuildContext();
        var handler = new CreateTaskHandler(context, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreateTaskRequest
            {
                CompanyId = companyId,
                Title = "Onboard new employee",
                Description = "Complete all onboarding tasks",
                Priority = TaskPriority.High,
                Source = TaskSource.Onboarding,
                DueDate = new DateOnly(2026, 7, 1),
                AssignedEmployeeId = Guid.NewGuid(),
                AssignedUserId = Guid.NewGuid()
            } with { CreatedBy = createdBy },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);

        var v = result.Value!;
        Assert.NotEqual(Guid.Empty, v.Id);
        Assert.Equal(companyId, v.CompanyId);
        Assert.Equal("Onboard new employee", v.Title);
        Assert.Equal("Complete all onboarding tasks", v.Description);
        Assert.Equal("Open", v.Status);
        Assert.Equal("High", v.Priority);
        Assert.Equal("Onboarding", v.Source);
        Assert.Equal(new DateOnly(2026, 7, 1), v.DueDate);
        Assert.Equal(createdBy, v.CreatedBy);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), v.CreatedAt);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), v.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Persists_Task_To_Database()
    {
        await using var context = BuildContext();
        var handler = new CreateTaskHandler(context, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new CreateTaskRequest
            {
                CompanyId = companyId,
                Title = "Review contract",
                Priority = TaskPriority.Medium,
                Source = TaskSource.Document
            } with { CreatedBy = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.TaskItems.SingleAsync();
        Assert.Equal(result.Value!.Id, saved.Id);
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal("Review contract", saved.Title);
        Assert.Equal(TaskItemStatus.Open, saved.Status);
    }

    [Fact]
    public async Task HandleAsync_Trims_Title_And_Description()
    {
        await using var context = BuildContext();
        var handler = new CreateTaskHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateTaskRequest
            {
                CompanyId = Guid.NewGuid(),
                Title = "  Send welcome email  ",
                Description = "  Remember to attach handbook  ",
                Priority = TaskPriority.Low,
                Source = TaskSource.Manual
            } with { CreatedBy = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Send welcome email", result.Value!.Title);
        Assert.Equal("Remember to attach handbook", result.Value.Description);
    }

    [Fact]
    public async Task HandleAsync_Stores_Null_Description_When_Whitespace_Provided()
    {
        await using var context = BuildContext();
        var handler = new CreateTaskHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateTaskRequest
            {
                CompanyId = Guid.NewGuid(),
                Title = "Quick task",
                Description = "   ",
                Priority = TaskPriority.Low,
                Source = TaskSource.Manual
            } with { CreatedBy = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Description);
    }

    [Fact]
    public async Task HandleAsync_Creates_Task_Without_Optional_Fields()
    {
        await using var context = BuildContext();
        var handler = new CreateTaskHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            new CreateTaskRequest
            {
                CompanyId = Guid.NewGuid(),
                Title = "Minimal task",
                Priority = TaskPriority.Low,
                Source = TaskSource.System
            } with { CreatedBy = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var v = result.Value!;
        Assert.Null(v.Description);
        Assert.Null(v.DueDate);
        Assert.Null(v.AssignedEmployeeId);
        Assert.Null(v.AssignedUserId);
    }

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TasksDbContext(options);
    }
}
