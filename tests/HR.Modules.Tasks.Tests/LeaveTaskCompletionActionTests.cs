using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Features.CompleteTask.Actions;
using HR.Modules.Tasks.Tests.Infrastructure;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Tests;

public class LeaveTaskCompletionActionTests
{
    private static readonly Guid CompanyId      = Guid.NewGuid();
    private static readonly Guid LeaveRequestId = Guid.NewGuid();
    private static readonly Guid CompletedBy    = Guid.NewGuid();

    private static TaskCompletionContext MakeContext(
        string? outcomeDecision  = "Approve",
        string? outcomeReason    = null,
        Guid?   sourceEntityId   = null) =>
        new(
            CompanyId,
            TaskId:            Guid.NewGuid(),
            Title:             "Review leave request — Alice",
            Description:       null,
            Source:            TaskSource.Leave,
            ActionType:        TaskActionType.Approve,
            AssignedEmployeeId: null,
            CompletedBy:       CompletedBy,
            CompletedAt:       DateTimeOffset.UtcNow,
            SourceEntityId:    sourceEntityId ?? LeaveRequestId,
            OutcomeDecision:   outcomeDecision,
            OutcomeReason:     outcomeReason);

    // ── Approve ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Approve_Calls_ApproveAsync()
    {
        var leaveService = new FakeLeaveApprovalService();
        var action = new LeaveTaskCompletionAction(leaveService);

        var result = await action.ExecuteAsync(MakeContext("Approve"), CancellationToken.None);

        var call = Assert.Single(leaveService.Calls);
        Assert.Equal("Approve", call.Action);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAsync_Approve_Propagates_Failure_From_ApproveAsync()
    {
        // Ticket 3 (P1): if the underlying leave approval service fails (e.g. the request is no
        // longer Pending), the action must surface that failure rather than silently succeeding.
        var failure = Result.Failure(Error.Conflict("The leave request is no longer pending."));
        var leaveService = new FakeLeaveApprovalService(approveResult: failure);
        var action = new LeaveTaskCompletionAction(leaveService);

        var result = await action.ExecuteAsync(MakeContext("Approve"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Approve_Passes_Correct_Arguments()
    {
        var leaveService = new FakeLeaveApprovalService();
        var action = new LeaveTaskCompletionAction(leaveService);

        await action.ExecuteAsync(MakeContext("Approve"), CancellationToken.None);

        var call = leaveService.Calls[0];
        Assert.Equal(CompanyId,      call.CompanyId);
        Assert.Equal(LeaveRequestId, call.LeaveRequestId);
        Assert.Equal(CompletedBy,    call.ReviewedByEmployeeId);
    }

    // ── Reject ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Reject_Calls_RejectAsync()
    {
        var leaveService = new FakeLeaveApprovalService();
        var action = new LeaveTaskCompletionAction(leaveService);

        var result = await action.ExecuteAsync(MakeContext("Reject", "Sprint release week"), CancellationToken.None);

        var call = Assert.Single(leaveService.Calls);
        Assert.Equal("Reject", call.Action);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteAsync_Reject_Propagates_Failure_From_RejectAsync()
    {
        var failure = Result.Failure(Error.Conflict("The leave request is no longer pending."));
        var leaveService = new FakeLeaveApprovalService(rejectResult: failure);
        var action = new LeaveTaskCompletionAction(leaveService);

        var result = await action.ExecuteAsync(MakeContext("Reject", "Too short-staffed"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Reject_Passes_Reason()
    {
        var leaveService = new FakeLeaveApprovalService();
        var action = new LeaveTaskCompletionAction(leaveService);

        await action.ExecuteAsync(MakeContext("Reject", "Too short-staffed"), CancellationToken.None);

        Assert.Equal("Too short-staffed", leaveService.Calls[0].Reason);
    }

    [Fact]
    public async Task ExecuteAsync_Reject_Passes_Null_Reason_When_Not_Provided()
    {
        var leaveService = new FakeLeaveApprovalService();
        var action = new LeaveTaskCompletionAction(leaveService);

        await action.ExecuteAsync(MakeContext("Reject", outcomeReason: null), CancellationToken.None);

        Assert.Null(leaveService.Calls[0].Reason);
    }

    // ── Guard clauses ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Validation_Failure_When_No_Decision()
    {
        var leaveService = new FakeLeaveApprovalService();
        var action = new LeaveTaskCompletionAction(leaveService);

        var result = await action.ExecuteAsync(MakeContext(outcomeDecision: null), CancellationToken.None);

        Assert.Empty(leaveService.Calls);
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Validation_Failure_When_SourceEntityId_Is_Null()
    {
        var leaveService = new FakeLeaveApprovalService();
        var action = new LeaveTaskCompletionAction(leaveService);

        var context = MakeContext("Approve", sourceEntityId: null) with { SourceEntityId = null };

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Empty(leaveService.Calls);
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Validation_Failure_For_Unknown_Decision()
    {
        var leaveService = new FakeLeaveApprovalService();
        var action = new LeaveTaskCompletionAction(leaveService);

        var result = await action.ExecuteAsync(MakeContext("Maybe"), CancellationToken.None);

        Assert.Empty(leaveService.Calls);
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Validation_Failure_For_Whitespace_Only_Decision()
    {
        // NotEmpty-style guards must reject whitespace-only input, not just null/empty — the
        // OutcomeDecision check here is a plain string comparison so whitespace falls through to
        // the "unknown decision" branch rather than the "no decision" branch; either way it must
        // fail, not silently no-op.
        var leaveService = new FakeLeaveApprovalService();
        var action = new LeaveTaskCompletionAction(leaveService);

        var result = await action.ExecuteAsync(MakeContext("   "), CancellationToken.None);

        Assert.Empty(leaveService.Calls);
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task Source_Is_Leave()
    {
        var action = new LeaveTaskCompletionAction(new FakeLeaveApprovalService());
        Assert.Equal(TaskSource.Leave, action.Source);
    }
}
