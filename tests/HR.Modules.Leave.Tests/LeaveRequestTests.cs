using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Tests;

public class LeaveRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);

    private static LeaveRequest CreateDraft() =>
        LeaveRequest.CreateDraft(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Holiday", Now);

    private static LeaveRequest CreatePending() =>
        LeaveRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Holiday", Now);

    [Fact]
    public void CreateDraft_Produces_Status_Draft()
    {
        var draft = CreateDraft();
        Assert.Equal(LeaveRequestStatus.Draft, draft.Status);
    }

    [Fact]
    public void Create_Defaults_To_Pending_Status_When_Not_Specified()
    {
        var request = CreatePending();
        Assert.Equal(LeaveRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void MarkSubmittedPending_Transitions_Draft_To_Pending()
    {
        var draft = CreateDraft();
        var submitTime = Now.AddHours(1);

        draft.MarkSubmittedPending(submitTime);

        Assert.Equal(LeaveRequestStatus.Pending, draft.Status);
        Assert.Equal(submitTime, draft.UpdatedAt);
    }

    [Fact]
    public void MarkSubmittedPending_Throws_When_Not_Draft()
    {
        var pending = CreatePending();
        Assert.Throws<InvalidOperationException>(() => pending.MarkSubmittedPending(Now));
    }

    [Fact]
    public void MarkSubmittedPending_Throws_When_Already_Approved()
    {
        var request = CreatePending();
        request.Approve(Guid.NewGuid(), Now);
        Assert.Throws<InvalidOperationException>(() => request.MarkSubmittedPending(Now));
    }

    [Fact]
    public void UpdateDraftDetails_Throws_When_Not_Draft()
    {
        var pending = CreatePending();
        Assert.Throws<InvalidOperationException>(() =>
            pending.UpdateDraftDetails(
                Guid.NewGuid(), new DateOnly(2026, 8, 10), LeaveDayPart.FullDay,
                new DateOnly(2026, 8, 11), LeaveDayPart.FullDay, 2m, "Updated", Now));
    }

    [Fact]
    public void UpdateDraftDetails_Succeeds_When_Draft()
    {
        var draft = CreateDraft();
        var updateTime = Now.AddHours(1);

        draft.UpdateDraftDetails(
            Guid.NewGuid(), new DateOnly(2026, 8, 10), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 11), LeaveDayPart.FullDay, 2m, "Updated", updateTime);

        Assert.Equal(new DateOnly(2026, 8, 10), draft.StartDate);
        Assert.Equal("Updated", draft.Reason);
        Assert.Equal(updateTime, draft.UpdatedAt);
    }

    [Fact]
    public void Approve_Works_From_Pending_Status()
    {
        var request = CreatePending();
        var reviewerId = Guid.NewGuid();
        request.Approve(reviewerId, Now);

        Assert.Equal(LeaveRequestStatus.Approved, request.Status);
        Assert.Equal(reviewerId, request.ReviewedByEmployeeId);
    }

    [Fact]
    public void Approve_Works_From_Draft_Status()
    {
        var draft = CreateDraft();
        var reviewerId = Guid.NewGuid();
        draft.Approve(reviewerId, Now);

        Assert.Equal(LeaveRequestStatus.Approved, draft.Status);
        Assert.Equal(reviewerId, draft.ReviewedByEmployeeId);
    }

    [Fact]
    public void AssignLeavePolicy_Sets_LeavePolicyId()
    {
        var draft = CreateDraft();
        var policyId = Guid.NewGuid();

        draft.AssignLeavePolicy(policyId);

        Assert.Equal(policyId, draft.LeavePolicyId);
    }
}
