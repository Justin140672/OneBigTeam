using HR.Modules.Tasks.Features.CompleteTask.Actions;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Tests;

public class AssetReturnTaskCompletionActionTests
{
    private static readonly Guid CompanyId    = Guid.NewGuid();
    private static readonly Guid AssignmentId = Guid.NewGuid();
    private static readonly Guid CompletedBy  = Guid.NewGuid();

    private static TaskCompletionContext MakeContext(Guid? sourceEntityId = null) =>
        new(
            CompanyId,
            TaskId:             Guid.NewGuid(),
            Title:              "Return asset",
            Description:        null,
            Source:             TaskSource.Asset,
            ActionType:         TaskActionType.Return,
            AssignedEmployeeId: Guid.NewGuid(),
            CompletedBy:        CompletedBy,
            CompletedAt:        DateTimeOffset.UtcNow,
            SourceEntityId:     sourceEntityId ?? AssignmentId);

    [Fact]
    public async Task ExecuteAsync_Calls_ReturnAsync_With_Correct_Arguments()
    {
        var returnService = new FakeAssetReturnService();
        var action = new AssetReturnTaskCompletionAction(returnService);

        await action.ExecuteAsync(MakeContext(), CancellationToken.None);

        var call = Assert.Single(returnService.Calls);
        Assert.Equal(CompanyId,    call.CompanyId);
        Assert.Equal(AssignmentId, call.AssignmentId);
        Assert.Equal(CompletedBy,  call.ReturnedBy);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_SourceEntityId_Is_Null()
    {
        var returnService = new FakeAssetReturnService();
        var action = new AssetReturnTaskCompletionAction(returnService);

        var context = MakeContext() with { SourceEntityId = null };
        await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Empty(returnService.Calls);
    }

    [Fact]
    public void Source_Is_Asset()
    {
        var action = new AssetReturnTaskCompletionAction(new FakeAssetReturnService());
        Assert.Equal(TaskSource.Asset, action.Source);
    }

    [Fact]
    public void ActionType_Is_Return()
    {
        var action = new AssetReturnTaskCompletionAction(new FakeAssetReturnService());
        Assert.Equal(TaskActionType.Return, action.ActionType);
    }
}
