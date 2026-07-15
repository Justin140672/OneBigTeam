using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class OpenTaskBySourceEntityReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);

    private static TaskItem MakeTask(
        Guid companyId,
        Guid? sourceEntityId,
        TaskItemStatus status = TaskItemStatus.Open,
        string title = "Task")
    {
        var t = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            title, null, TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            null, null, null, Now, sourceEntityId);

        if (status == TaskItemStatus.InProgress) t.Start(Now);
        if (status == TaskItemStatus.Completed) t.Complete(Guid.NewGuid(), Now);
        if (status == TaskItemStatus.Cancelled) t.Cancel(Now);

        return t;
    }

    [Fact]
    public async Task GetOpenTaskIdsAsync_Returns_Task_Id_For_Open_Status()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.Open);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var reader = new OpenTaskBySourceEntityReader(context);
        var result = await reader.GetOpenTaskIdsAsync(companyId, [sourceEntityId], CancellationToken.None);

        Assert.Equal(task.Id, result[sourceEntityId]);
    }

    [Fact]
    public async Task GetOpenTaskIdsAsync_Returns_Task_Id_For_InProgress_Status()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.InProgress);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var reader = new OpenTaskBySourceEntityReader(context);
        var result = await reader.GetOpenTaskIdsAsync(companyId, [sourceEntityId], CancellationToken.None);

        Assert.Equal(task.Id, result[sourceEntityId]);
    }

    [Fact]
    public async Task GetOpenTaskIdsAsync_Omits_Completed_Task()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        context.TaskItems.Add(MakeTask(companyId, sourceEntityId, TaskItemStatus.Completed));
        await context.SaveChangesAsync();

        var reader = new OpenTaskBySourceEntityReader(context);
        var result = await reader.GetOpenTaskIdsAsync(companyId, [sourceEntityId], CancellationToken.None);

        Assert.False(result.ContainsKey(sourceEntityId));
    }

    [Fact]
    public async Task GetOpenTaskIdsAsync_Omits_Cancelled_Task()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        context.TaskItems.Add(MakeTask(companyId, sourceEntityId, TaskItemStatus.Cancelled));
        await context.SaveChangesAsync();

        var reader = new OpenTaskBySourceEntityReader(context);
        var result = await reader.GetOpenTaskIdsAsync(companyId, [sourceEntityId], CancellationToken.None);

        Assert.False(result.ContainsKey(sourceEntityId));
    }

    [Fact]
    public async Task GetOpenTaskIdsAsync_Omits_SourceEntityId_With_No_Task_At_All()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        // No tasks seeded at all.

        var reader = new OpenTaskBySourceEntityReader(context);
        var result = await reader.GetOpenTaskIdsAsync(companyId, [sourceEntityId], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOpenTaskIdsAsync_Isolates_By_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        context.TaskItems.Add(MakeTask(otherCompanyId, sourceEntityId, TaskItemStatus.Open));
        await context.SaveChangesAsync();

        var reader = new OpenTaskBySourceEntityReader(context);
        var result = await reader.GetOpenTaskIdsAsync(companyId, [sourceEntityId], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOpenTaskIdsAsync_Returns_Empty_Dictionary_For_Empty_Input_List()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.TaskItems.Add(MakeTask(companyId, Guid.NewGuid(), TaskItemStatus.Open));
        await context.SaveChangesAsync();

        var reader = new OpenTaskBySourceEntityReader(context);
        var result = await reader.GetOpenTaskIdsAsync(companyId, [], CancellationToken.None);

        Assert.Empty(result);
    }

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TasksDbContext(options);
    }
}
