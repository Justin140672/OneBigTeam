using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Tests.Domain;

// P1 fix (departure access disablement): domain unit tests for AccountDisablement's
// Pending -> Processing -> Processed|Failed state machine (mirrors
// HR.Modules.Companies.Domain.OutboxMessage's shape).
public class AccountDisablementTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);

    private static AccountDisablement CreatePending() =>
        AccountDisablement.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RequestedAt);

    [Fact]
    public void CreatePending_Sets_Status_Pending_With_Zero_Attempts()
    {
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var applicationUserId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var request = AccountDisablement.CreatePending(id, companyId, applicationUserId, employeeId, RequestedAt);

        Assert.Equal(id, request.Id);
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(applicationUserId, request.ApplicationUserId);
        Assert.Equal(employeeId, request.EmployeeId);
        Assert.Equal(AccountDisablement.StatusPending, request.Status);
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

        Assert.Equal(AccountDisablement.StatusProcessing, request.Status);
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

        Assert.Equal(AccountDisablement.StatusProcessed, request.Status);
        Assert.Equal(processedAt, request.ProcessedAt);
        Assert.Null(request.FailureReason);
    }

    [Fact]
    public void MarkFailed_Sets_Status_Failed_With_Reason_And_LastAttemptAt()
    {
        var request = CreatePending();
        request.MarkProcessing(RequestedAt.AddMinutes(1));
        var failedAt = RequestedAt.AddMinutes(5);

        request.MarkFailed("Account disablement failed.", failedAt);

        Assert.Equal(AccountDisablement.StatusFailed, request.Status);
        Assert.Equal("Account disablement failed.", request.FailureReason);
        Assert.Equal(failedAt, request.LastAttemptAt);
    }

    [Fact]
    public void ResetForRetry_From_Failed_Resets_To_Pending_And_Clears_FailureReason()
    {
        var request = CreatePending();
        request.MarkProcessing(RequestedAt.AddMinutes(1));
        request.MarkFailed("boom", RequestedAt.AddMinutes(2));

        request.ResetForRetry();

        Assert.Equal(AccountDisablement.StatusPending, request.Status);
        Assert.Null(request.FailureReason);
    }

    [Theory]
    [InlineData(AccountDisablement.StatusPending)]
    [InlineData(AccountDisablement.StatusProcessing)]
    [InlineData(AccountDisablement.StatusProcessed)]
    public void ResetForRetry_Throws_When_Not_Failed(string nonFailedStatus)
    {
        var request = CreatePending();

        if (nonFailedStatus == AccountDisablement.StatusProcessing)
        {
            request.MarkProcessing(RequestedAt.AddMinutes(1));
        }
        else if (nonFailedStatus == AccountDisablement.StatusProcessed)
        {
            request.MarkProcessing(RequestedAt.AddMinutes(1));
            request.MarkProcessed(RequestedAt.AddMinutes(2));
        }
        // StatusPending: request is already Pending by construction.

        Assert.Throws<InvalidOperationException>(() => request.ResetForRetry());
    }
}
