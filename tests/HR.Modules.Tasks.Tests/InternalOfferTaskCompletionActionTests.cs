using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Features.CompleteTask.Actions;

namespace HR.Modules.Tasks.Tests;

public class InternalOfferTaskCompletionActionTests
{
    private static readonly InternalOfferTaskCompletionAction Action = new();

    [Fact]
    public void Handles_Recruitment_Approve_Tasks_Only()
    {
        Assert.Equal(TaskSource.Recruitment, Action.Source);
        Assert.Equal(TaskActionType.Approve, Action.ActionType);
    }

    [Fact]
    public async Task Manual_Completion_Is_Rejected_So_The_Offer_Cannot_Be_Bypassed()
    {
        var context = new TaskCompletionContext(
            Guid.NewGuid(), Guid.NewGuid(), "Review your internal job offer", null,
            TaskSource.Recruitment, TaskActionType.Approve, Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow, Guid.NewGuid());

        var result = await Action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("accept or decline", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
