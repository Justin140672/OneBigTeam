using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Features.CompleteTask.Actions;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Tests;

public class InterviewFeedbackTaskCompletionActionTests
{
    private static readonly Guid CompanyId    = Guid.NewGuid();
    private static readonly Guid InterviewId  = Guid.NewGuid();
    private static readonly Guid CompletedBy  = Guid.NewGuid();

    private static TaskCompletionContext MakeContext(
        string? outcomeDecision = "Passed",
        string? outcomeReason   = null,
        Guid?   sourceEntityId  = null) =>
        new(
            CompanyId,
            TaskId:             Guid.NewGuid(),
            Title:              "Provide feedback: interview with Emma Clarke",
            Description:        null,
            Source:             TaskSource.Recruitment,
            ActionType:         TaskActionType.Complete,
            AssignedEmployeeId: null,
            CompletedBy:        CompletedBy,
            CompletedAt:        DateTimeOffset.UtcNow,
            SourceEntityId:     sourceEntityId ?? InterviewId,
            OutcomeDecision:    outcomeDecision,
            OutcomeReason:      outcomeReason);

    [Fact]
    public async Task ExecuteAsync_Calls_RecordFeedbackAsync_With_Correct_Arguments()
    {
        var feedbackService = new FakeInterviewFeedbackService();
        var action = new InterviewFeedbackTaskCompletionAction(feedbackService);

        await action.ExecuteAsync(MakeContext("Passed", "Strong technical skills."), CancellationToken.None);

        var call = Assert.Single(feedbackService.Calls);
        Assert.Equal(CompanyId, call.CompanyId);
        Assert.Equal(InterviewId, call.InterviewId);
        Assert.Equal(CompletedBy, call.RecordedByEmployeeId);
        Assert.Equal("Passed", call.Outcome);
        Assert.Equal("Strong technical skills.", call.Notes);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_No_Decision()
    {
        var feedbackService = new FakeInterviewFeedbackService();
        var action = new InterviewFeedbackTaskCompletionAction(feedbackService);

        await action.ExecuteAsync(MakeContext(outcomeDecision: null), CancellationToken.None);

        Assert.Empty(feedbackService.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Nothing_When_SourceEntityId_Is_Null()
    {
        var feedbackService = new FakeInterviewFeedbackService();
        var action = new InterviewFeedbackTaskCompletionAction(feedbackService);

        var context = MakeContext("Passed") with { SourceEntityId = null };

        await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Empty(feedbackService.Calls);
    }

    [Fact]
    public void Source_Is_Recruitment()
    {
        var action = new InterviewFeedbackTaskCompletionAction(new FakeInterviewFeedbackService());
        Assert.Equal(TaskSource.Recruitment, action.Source);
    }

    [Fact]
    public void ActionType_Is_Complete()
    {
        var action = new InterviewFeedbackTaskCompletionAction(new FakeInterviewFeedbackService());
        Assert.Equal(TaskActionType.Complete, action.ActionType);
    }
}
