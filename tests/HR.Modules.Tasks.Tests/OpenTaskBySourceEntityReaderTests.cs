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
        string title = "Task",
        TaskActionType actionType = TaskActionType.Complete)
    {
        var t = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            title, null, TaskPriority.Medium, TaskSource.Workflow, actionType,
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

    [Fact]
    public async Task GetOpenTaskIdsAsync_Without_ActionType_Filter_Matches_Any_ActionType()
    {
        // Default (null) behaviour is unchanged — an open task of ANY action type still matches
        // when no actionType filter is supplied. This preserves the original contract relied on by
        // the one existing caller, GetRecentLeaveRequestsHandler.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.Open, actionType: TaskActionType.Approve);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var reader = new OpenTaskBySourceEntityReader(context);
        var result = await reader.GetOpenTaskIdsAsync(companyId, [sourceEntityId], CancellationToken.None);

        Assert.Equal(task.Id, result[sourceEntityId]);
    }

    [Fact]
    public async Task GetOpenTaskIdsAsync_With_ActionType_Filter_Returns_Task_Id_When_ActionType_Matches()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var task = MakeTask(companyId, sourceEntityId, TaskItemStatus.Open, actionType: TaskActionType.Review);
        context.TaskItems.Add(task);
        await context.SaveChangesAsync();

        var reader = new OpenTaskBySourceEntityReader(context);
        var result = await reader.GetOpenTaskIdsAsync(
            companyId, [sourceEntityId], CancellationToken.None, TaskActionType.Review);

        Assert.Equal(task.Id, result[sourceEntityId]);
    }

    [Fact]
    public async Task GetOpenTaskIdsAsync_With_ActionType_Filter_Omits_SourceEntityId_When_Only_A_Different_ActionType_Is_Open()
    {
        // Core new behaviour: the source entity id must be OMITTED entirely (not mapped to null,
        // not matched to the wrong task) when its only open task is of a different action type.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        context.TaskItems.Add(
            MakeTask(companyId, sourceEntityId, TaskItemStatus.Open, actionType: TaskActionType.Acknowledge));
        await context.SaveChangesAsync();

        var reader = new OpenTaskBySourceEntityReader(context);
        var result = await reader.GetOpenTaskIdsAsync(
            companyId, [sourceEntityId], CancellationToken.None, TaskActionType.Review);

        Assert.False(result.ContainsKey(sourceEntityId));
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOpenTaskIdsAsync_With_ActionType_Filter_Distinguishes_Between_Two_Open_Tasks_Of_Different_ActionTypes_For_The_Same_SourceEntity()
    {
        // A single source entity (e.g. a Shared Company Document) can have multiple concurrent open
        // tasks of different action types. Prove each actionType-scoped call returns only its own
        // matching task id, never the other one.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var acknowledgeTask = MakeTask(
            companyId, sourceEntityId, TaskItemStatus.Open, actionType: TaskActionType.Acknowledge);
        var reviewTask = MakeTask(
            companyId, sourceEntityId, TaskItemStatus.Open, actionType: TaskActionType.Review);
        context.TaskItems.AddRange(acknowledgeTask, reviewTask);
        await context.SaveChangesAsync();

        var reader = new OpenTaskBySourceEntityReader(context);

        var reviewResult = await reader.GetOpenTaskIdsAsync(
            companyId, [sourceEntityId], CancellationToken.None, TaskActionType.Review);
        Assert.Equal(reviewTask.Id, reviewResult[sourceEntityId]);

        var acknowledgeResult = await reader.GetOpenTaskIdsAsync(
            companyId, [sourceEntityId], CancellationToken.None, TaskActionType.Acknowledge);
        Assert.Equal(acknowledgeTask.Id, acknowledgeResult[sourceEntityId]);
    }

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TasksDbContext(options);
    }
}
