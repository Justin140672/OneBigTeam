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

        request.MarkFailed("Account disablement failed.", failedAt, maxAutomaticAttempts: 4);

        Assert.Equal(AccountDisablement.StatusFailed, request.Status);
        Assert.Equal("Account disablement failed.", request.FailureReason);
        Assert.Equal(failedAt, request.LastAttemptAt);
    }

    [Fact]
    public void ResetForRetry_From_Failed_Resets_To_Pending_And_Clears_FailureReason()
    {
        var request = CreatePending();
        request.MarkProcessing(RequestedAt.AddMinutes(1));
        request.MarkFailed("boom", RequestedAt.AddMinutes(2), maxAutomaticAttempts: 4);

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

    // ---- Ticket 19 (P2): claim/lease + terminal-failure + manual retry ---------------------------

    [Fact]
    public void Claim_Sets_ClaimedBy_LeaseExpiresAt_And_Transitions_To_Processing()
    {
        var request = CreatePending();
        var workerId = Guid.NewGuid();
        var now = RequestedAt.AddMinutes(1);

        request.Claim(workerId, now);

        Assert.Equal(AccountDisablement.StatusProcessing, request.Status);
        Assert.Equal(1, request.AttemptCount);
        Assert.True(request.IsClaimedBy(workerId, now));
        Assert.True(request.IsClaimedBy(workerId, now + AccountDisablement.LeaseDuration - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void IsClaimedBy_Returns_False_For_A_Different_Worker()
    {
        var request = CreatePending();
        var now = RequestedAt.AddMinutes(1);
        request.Claim(Guid.NewGuid(), now);

        Assert.False(request.IsClaimedBy(Guid.NewGuid(), now));
    }

    [Fact]
    public void IsClaimedBy_Returns_False_Once_The_Lease_Has_Expired()
    {
        var request = CreatePending();
        var workerId = Guid.NewGuid();
        var claimedAt = RequestedAt.AddMinutes(1);
        request.Claim(workerId, claimedAt);

        var afterLeaseExpiry = claimedAt + AccountDisablement.LeaseDuration + TimeSpan.FromSeconds(1);

        Assert.False(request.IsClaimedBy(workerId, afterLeaseExpiry));
    }

    [Fact]
    public void MarkProcessed_Clears_The_Claim()
    {
        var request = CreatePending();
        var workerId = Guid.NewGuid();
        var now = RequestedAt.AddMinutes(1);
        request.Claim(workerId, now);

        request.MarkProcessed(now.AddMinutes(1));

        Assert.Null(request.ClaimedBy);
        Assert.Null(request.LeaseExpiresAt);
    }

    [Fact]
    public void MarkFailed_Below_Automatic_Retry_Ceiling_Is_Not_Terminal()
    {
        var request = CreatePending();
        request.MarkProcessing(RequestedAt.AddMinutes(1));

        request.MarkFailed("boom", RequestedAt.AddMinutes(2), maxAutomaticAttempts: 4);

        Assert.False(request.IsTerminallyFailed);
        request.ResetForRetry(); // Must not throw — still automatically retryable.
        Assert.Equal(AccountDisablement.StatusPending, request.Status);
    }

    [Fact]
    public void MarkFailed_At_Automatic_Retry_Ceiling_Is_Terminal_And_Blocks_ResetForRetry()
    {
        var request = CreatePending();
        for (var i = 0; i < 4; i++)
            request.MarkProcessing(RequestedAt.AddMinutes(i + 1));

        // 4th attempt reaches the ceiling of 4.
        request.MarkFailed("boom", RequestedAt.AddMinutes(5), maxAutomaticAttempts: 4);

        Assert.True(request.IsTerminallyFailed);
        Assert.Throws<InvalidOperationException>(() => request.ResetForRetry());
    }

    [Fact]
    public void RecordManualRetry_Clears_Terminal_Flag_And_Records_Actor_And_Reason()
    {
        var request = CreatePending();
        for (var i = 0; i < 4; i++)
            request.MarkProcessing(RequestedAt.AddMinutes(i + 1));
        request.MarkFailed("boom", RequestedAt.AddMinutes(5), maxAutomaticAttempts: 4);
        Assert.True(request.IsTerminallyFailed);

        var actorId = Guid.NewGuid();
        var retriedAt = RequestedAt.AddHours(1);
        request.RecordManualRetry(actorId, "Confirmed the outage is resolved.", retriedAt);

        Assert.Equal(AccountDisablement.StatusPending, request.Status);
        Assert.False(request.IsTerminallyFailed);
        Assert.Null(request.FailureReason);
        Assert.Equal(actorId, request.LastRetriedByActorId);
        Assert.Equal("Confirmed the outage is resolved.", request.LastRetryReason);
        Assert.Equal(retriedAt, request.LastRetriedAt);
    }

    [Fact]
    public void RecordManualRetry_Requires_A_Reason()
    {
        var request = CreatePending();
        request.MarkProcessing(RequestedAt.AddMinutes(1));
        request.MarkFailed("boom", RequestedAt.AddMinutes(2), maxAutomaticAttempts: 4);

        Assert.Throws<ArgumentException>(() => request.RecordManualRetry(Guid.NewGuid(), "", RequestedAt.AddHours(1)));
    }

    [Fact]
    public void RecordManualRetry_Throws_When_Not_Failed()
    {
        var request = CreatePending();

        Assert.Throws<InvalidOperationException>(
            () => request.RecordManualRetry(Guid.NewGuid(), "reason", RequestedAt.AddHours(1)));
    }
}
