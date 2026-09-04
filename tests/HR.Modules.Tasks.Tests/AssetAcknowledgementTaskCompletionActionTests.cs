using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Features.CompleteTask.Actions;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Tests;

public class AssetAcknowledgementTaskCompletionActionTests
{
    private static readonly Guid CompanyId    = Guid.NewGuid();
    private static readonly Guid AssignmentId = Guid.NewGuid();
    private static readonly Guid EmployeeId   = Guid.NewGuid();
    private static readonly Guid CompletedBy  = Guid.NewGuid();

    private static TaskCompletionContext MakeContext(Guid? sourceEntityId = null) =>
        new(
            CompanyId,
            TaskId:             Guid.NewGuid(),
            Title:              "Acknowledge receipt of asset",
            Description:        null,
            Source:             TaskSource.Asset,
            ActionType:         TaskActionType.Acknowledge,
            AssignedEmployeeId: EmployeeId,
            CompletedBy:        CompletedBy,
            CompletedAt:        DateTimeOffset.UtcNow,
            SourceEntityId:     sourceEntityId ?? AssignmentId);

    private static AssetTaskCompletionAction MakeAction(
        FakeAssetAcknowledgementService? ackService = null,
        FakeTaskCreator? taskCreator = null) =>
        new(ackService ?? new FakeAssetAcknowledgementService(),
            taskCreator ?? new FakeTaskCreator());

    // ── Source / ActionType ────────────────────────────────────────────────────

    [Fact]
    public void Source_Is_Asset()
    {
        Assert.Equal(TaskSource.Asset, MakeAction().Source);
    }

    [Fact]
    public void ActionType_Is_Acknowledge()
    {
        Assert.Equal(TaskActionType.Acknowledge, MakeAction().ActionType);
    }

    // ── Acknowledgement call ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Calls_AcknowledgeAsync_With_Correct_Arguments()
    {
        var ackService = new FakeAssetAcknowledgementService();
        var action     = MakeAction(ackService: ackService);

        await action.ExecuteAsync(MakeContext(), CancellationToken.None);

        var call = Assert.Single(ackService.Calls);
        Assert.Equal(CompanyId,    call.CompanyId);
        Assert.Equal(AssignmentId, call.AssignmentId);
        Assert.Equal(CompletedBy,  call.AcknowledgedBy);
    }

    // ── Return task creation ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Creates_A_Return_Task_After_Acknowledgement()
    {
        var taskCreator = new FakeTaskCreator();
        var action      = MakeAction(taskCreator: taskCreator);

        await action.ExecuteAsync(MakeContext(), CancellationToken.None);

        Assert.Single(taskCreator.Created);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Return_Task_With_Asset_Source_And_Return_ActionType()
    {
        var taskCreator = new FakeTaskCreator();
        var action      = MakeAction(taskCreator: taskCreator);

        await action.ExecuteAsync(MakeContext(), CancellationToken.None);

        var created = taskCreator.Created[0];
        Assert.Equal(TaskSource.Asset,      created.Source);
        Assert.Equal(TaskActionType.Return, created.ActionType);
    }

    [Fact]
    public async Task ExecuteAsync_Creates_Return_Task_Linked_To_Same_Assignment_And_Employee()
    {
        var taskCreator = new FakeTaskCreator();
        var action      = MakeAction(taskCreator: taskCreator);

        await action.ExecuteAsync(MakeContext(), CancellationToken.None);

        var created = taskCreator.Created[0];
        Assert.Equal(CompanyId,    created.CompanyId);
        Assert.Equal(AssignmentId, created.SourceEntityId);
        Assert.Equal(EmployeeId,   created.AssignedEmployeeId);
    }

    // ── Guard clause ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_SourceEntityId_Is_Null()
    {
        var ackService  = new FakeAssetAcknowledgementService();
        var taskCreator = new FakeTaskCreator();
        var action      = MakeAction(ackService: ackService, taskCreator: taskCreator);

        var context = MakeContext() with { SourceEntityId = null };
        await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Empty(ackService.Calls);
        Assert.Empty(taskCreator.Created);
    }
}
