using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests;

/// <summary>
/// Ticket 3 (P1): TaskCompletionDispatcher.DispatchAsync now returns Task&lt;Result&gt; and runs
/// BEFORE the underlying TaskItem is marked Completed (see CompleteTaskHandler). These tests cover
/// the dispatcher in isolation — a failing action must stop dispatch and surface its failure, a
/// successful dispatch (or no matching action at all) must return Result.Success().
/// </summary>
public class TaskCompletionDispatcherTests
{
    private sealed class StubAction(TaskSource source, TaskActionType actionType, Result result) : ITaskCompletionAction
    {
        public TaskSource Source => source;
        public TaskActionType ActionType => actionType;
        public int CallCount { get; private set; }

        public Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private static TaskCompletionContext MakeContext(TaskSource source, TaskActionType actionType) =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), "Title", null,
            source, actionType, null, Guid.NewGuid(), DateTimeOffset.UtcNow,
            null, null, null);

    [Fact]
    public async Task DispatchAsync_Returns_Success_When_No_Matching_Action_Registered()
    {
        // Ordinary tasks without decision requirements (e.g. plain Workflow/Complete tasks) have no
        // registered ITaskCompletionAction and must remain completable.
        var dispatcher = new TaskCompletionDispatcher([]);

        var result = await dispatcher.DispatchAsync(
            MakeContext(TaskSource.Workflow, TaskActionType.Complete), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DispatchAsync_Returns_Success_When_Matching_Action_Succeeds()
    {
        var action = new StubAction(TaskSource.Leave, TaskActionType.Approve, Result.Success());
        var dispatcher = new TaskCompletionDispatcher([action]);

        var result = await dispatcher.DispatchAsync(
            MakeContext(TaskSource.Leave, TaskActionType.Approve), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, action.CallCount);
    }

    [Fact]
    public async Task DispatchAsync_Returns_Failure_From_Failing_Action()
    {
        var failure = Result.Failure(Error.Validation("A decision is required."));
        var action = new StubAction(TaskSource.Leave, TaskActionType.Approve, failure);
        var dispatcher = new TaskCompletionDispatcher([action]);

        var result = await dispatcher.DispatchAsync(
            MakeContext(TaskSource.Leave, TaskActionType.Approve), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal("A decision is required.", result.Error.Message);
    }

    [Fact]
    public async Task DispatchAsync_Stops_At_First_Failing_Action_And_Does_Not_Call_Subsequent_Actions()
    {
        var failing = new StubAction(TaskSource.Leave, TaskActionType.Approve, Result.Failure(Error.Validation("bad")));
        var second  = new StubAction(TaskSource.Leave, TaskActionType.Approve, Result.Success());
        var dispatcher = new TaskCompletionDispatcher([failing, second]);

        var result = await dispatcher.DispatchAsync(
            MakeContext(TaskSource.Leave, TaskActionType.Approve), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, failing.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task DispatchAsync_Only_Invokes_Actions_Matching_Both_Source_And_ActionType()
    {
        var wrongSource     = new StubAction(TaskSource.Probation, TaskActionType.Review, Result.Success());
        var wrongActionType = new StubAction(TaskSource.Leave, TaskActionType.Review, Result.Success());
        var matching        = new StubAction(TaskSource.Leave, TaskActionType.Approve, Result.Success());
        var dispatcher = new TaskCompletionDispatcher([wrongSource, wrongActionType, matching]);

        var result = await dispatcher.DispatchAsync(
            MakeContext(TaskSource.Leave, TaskActionType.Approve), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, wrongSource.CallCount);
        Assert.Equal(0, wrongActionType.CallCount);
        Assert.Equal(1, matching.CallCount);
    }
}
