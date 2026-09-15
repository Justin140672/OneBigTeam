using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Tests.Domain;

// Reliability follow-up: domain unit tests for LeavePolicyDeactivationOnDeparture's
// Pending -> Processing -> Processed|Failed state machine (mirrors
// HR.Modules.Identity.Domain.AccountDisablement's shape/tests).
public class LeavePolicyDeactivationOnDepartureTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 11, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);

    private static LeavePolicyDeactivationOnDeparture CreatePending() =>
        LeavePolicyDeactivationOnDeparture.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), OccurredAt, RequestedAt);

    [Fact]
    public void CreatePending_Sets_Status_Pending_With_Zero_Attempts_And_Captures_OccurredAt()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var request = LeavePolicyDeactivationOnDeparture.CreatePending(id, companyId, employeeId, OccurredAt, RequestedAt);

        Assert.Equal(id, request.Id);
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(employeeId, request.EmployeeId);
        Assert.Equal(OccurredAt, request.OccurredAt);
        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusPending, request.Status);
        Assert.Equal(0, request.AttemptCount);
        Assert.Equal(RequestedAt, request.RequestedAt);
        Assert.Null(request.LastAttemptAt);
        Assert.Null(request.FailureReason);
        Assert.Null(request.ProcessedAt);
    }

    [Fact]
    public void MarkProcessing_Increments_AttemptCount_Sets_Status_And_LastAttemptAt_And_Clears_FailureReason()
    {
        var request = CreatePending();
        var firstAttemptAt = RequestedAt.AddMinutes(1);

        request.MarkProcessing(firstAttemptAt);

        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusProcessing, request.Status);
        Assert.Equal(1, request.AttemptCount);
        Assert.Equal(firstAttemptAt, request.LastAttemptAt);
        Assert.Null(request.FailureReason);
    }

    [Fact]
    public void MarkProcessing_Called_Twice_Increments_AttemptCount_Each_Time()
    {
        var request = CreatePending();

        request.MarkProcessing(RequestedAt.AddMinutes(1));
        request.MarkProcessing(RequestedAt.AddMinutes(2));

        Assert.Equal(2, request.AttemptCount);
        Assert.Equal(RequestedAt.AddMinutes(2), request.LastAttemptAt);
    }

    [Fact]
    public void MarkProcessed_Sets_Status_Processed_And_ProcessedAt_And_Clears_FailureReason()
    {
        var request = CreatePending();
        request.MarkProcessing(RequestedAt.AddMinutes(1));
        var processedAt = RequestedAt.AddMinutes(2);

        request.MarkProcessed(processedAt);

        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusProcessed, request.Status);
        Assert.Equal(processedAt, request.ProcessedAt);
        Assert.Null(request.FailureReason);
    }

    [Fact]
    public void MarkFailed_Sets_Status_Failed_With_Reason_And_LastAttemptAt()
    {
        var request = CreatePending();
        request.MarkProcessing(RequestedAt.AddMinutes(1));
        var failedAt = RequestedAt.AddMinutes(5);

        request.MarkFailed("Leave policy deactivation failed.", failedAt);

        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusFailed, request.Status);
        Assert.Equal("Leave policy deactivation failed.", request.FailureReason);
        Assert.Equal(failedAt, request.LastAttemptAt);
    }

    [Fact]
    public void ResetForRetry_From_Failed_Resets_To_Pending_And_Clears_FailureReason()
    {
        var request = CreatePending();
        request.MarkProcessing(RequestedAt.AddMinutes(1));
        request.MarkFailed("boom", RequestedAt.AddMinutes(2));

        request.ResetForRetry();

        Assert.Equal(LeavePolicyDeactivationOnDeparture.StatusPending, request.Status);
        Assert.Null(request.FailureReason);
    }

    [Theory]
    [InlineData(LeavePolicyDeactivationOnDeparture.StatusPending)]
    [InlineData(LeavePolicyDeactivationOnDeparture.StatusProcessing)]
    [InlineData(LeavePolicyDeactivationOnDeparture.StatusProcessed)]
    public void ResetForRetry_Throws_When_Not_Failed(string nonFailedStatus)
    {
        var request = CreatePending();

        if (nonFailedStatus == LeavePolicyDeactivationOnDeparture.StatusProcessing)
        {
            request.MarkProcessing(RequestedAt.AddMinutes(1));
        }
        else if (nonFailedStatus == LeavePolicyDeactivationOnDeparture.StatusProcessed)
        {
            request.MarkProcessing(RequestedAt.AddMinutes(1));
            request.MarkProcessed(RequestedAt.AddMinutes(2));
        }
        // StatusPending: request is already Pending by construction.

        Assert.Throws<InvalidOperationException>(() => request.ResetForRetry());
    }
}
