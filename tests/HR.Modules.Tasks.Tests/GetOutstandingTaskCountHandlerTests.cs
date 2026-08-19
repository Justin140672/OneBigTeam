using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.GetOutstandingTaskCount;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class GetOutstandingTaskCountHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    private static TaskItem MakeTask(
        Guid companyId,
        TaskSource source,
        TaskActionType actionType = TaskActionType.Complete,
        TaskItemStatus status = TaskItemStatus.Open,
        string title = "Task")
    {
        var t = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(),
            title, null, TaskPriority.Medium, source, actionType,
            null, Guid.NewGuid(), Guid.NewGuid(), Now);

        if (status == TaskItemStatus.InProgress) t.Start(Now);
        if (status == TaskItemStatus.Completed) t.Complete(Guid.NewGuid(), Now);
        if (status == TaskItemStatus.Cancelled) t.Cancel(Now);

        return t;
    }

    [Fact]
    public async Task HandleAsync_Counts_Outstanding_Tasks_Matching_Source_And_ActionType()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, TaskSource.Recruitment, TaskActionType.Complete, TaskItemStatus.Open, "Feedback A"),
            MakeTask(companyId, TaskSource.Recruitment, TaskActionType.Complete, TaskItemStatus.InProgress, "Feedback B"),
            MakeTask(companyId, TaskSource.Recruitment, TaskActionType.Review, TaskItemStatus.Open, "Prep for interview"),
            MakeTask(companyId, TaskSource.Asset, TaskActionType.Complete, TaskItemStatus.Open, "Unrelated task"));

        await context.SaveChangesAsync();

        var result = await new GetOutstandingTaskCountHandler(context).HandleAsync(
            new GetOutstandingTaskCountRequest { CompanyId = companyId, Source = TaskSource.Recruitment, ActionType = TaskActionType.Complete },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Completed_And_Cancelled_Tasks()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, TaskSource.Recruitment, TaskActionType.Complete, TaskItemStatus.Open, "Open feedback"),
            MakeTask(companyId, TaskSource.Recruitment, TaskActionType.Complete, TaskItemStatus.Completed, "Done feedback"),
            MakeTask(companyId, TaskSource.Recruitment, TaskActionType.Complete, TaskItemStatus.Cancelled, "Cancelled feedback"));

        await context.SaveChangesAsync();

        var result = await new GetOutstandingTaskCountHandler(context).HandleAsync(
            new GetOutstandingTaskCountRequest { CompanyId = companyId, Source = TaskSource.Recruitment, ActionType = TaskActionType.Complete },
            CancellationToken.None);

        Assert.Equal(1, result.Value!.Count);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Tasks_From_Other_Companies()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyA, TaskSource.Recruitment, TaskActionType.Complete, title: "Company A feedback"),
            MakeTask(companyB, TaskSource.Recruitment, TaskActionType.Complete, title: "Company B feedback"));

        await context.SaveChangesAsync();

        var result = await new GetOutstandingTaskCountHandler(context).HandleAsync(
            new GetOutstandingTaskCountRequest { CompanyId = companyA, Source = TaskSource.Recruitment, ActionType = TaskActionType.Complete },
            CancellationToken.None);

        Assert.Equal(1, result.Value!.Count);
    }

    [Fact]
    public async Task HandleAsync_Without_Filters_Counts_All_Outstanding_Tasks_For_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.TaskItems.AddRange(
            MakeTask(companyId, TaskSource.Recruitment, TaskActionType.Complete, title: "Feedback"),
            MakeTask(companyId, TaskSource.Asset, TaskActionType.Acknowledge, title: "Acknowledge asset"),
            MakeTask(companyId, TaskSource.Workflow, TaskActionType.Complete, TaskItemStatus.Completed, "Done"));

        await context.SaveChangesAsync();

        var result = await new GetOutstandingTaskCountHandler(context).HandleAsync(
            new GetOutstandingTaskCountRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(2, result.Value!.Count);
    }

    private static TasksDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new TasksDbContext(options);
    }
}
