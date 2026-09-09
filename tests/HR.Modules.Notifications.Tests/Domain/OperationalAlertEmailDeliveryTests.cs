using HR.Modules.Notifications.Domain;

namespace HR.Modules.Notifications.Tests.Domain;

/// <summary>
/// Follow-up E: exclusive, recoverable operational-alert email delivery. <see cref="OperationalAlertEmailDelivery.RecordAttempt"/>
/// was removed and replaced by the lease-taking <see cref="OperationalAlertEmailDelivery.Claim"/> /
/// <see cref="OperationalAlertEmailDelivery.ReleaseForRetry"/> state machine — these tests pin its
/// boundaries (lease expiry, attempt budget, terminal guards).
/// </summary>
public class OperationalAlertEmailDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

    private static OperationalAlertEmailDelivery Create() =>
        OperationalAlertEmailDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

    [Fact]
    public void Create_Starts_Pending_With_Zero_Attempts_And_No_Lease()
    {
        var alertId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var delivery = OperationalAlertEmailDelivery.Create(Guid.NewGuid(), alertId, companyId, Now);

        Assert.Equal(alertId, delivery.AlertId);
        Assert.Equal(companyId, delivery.CompanyId);
        Assert.Equal(EmailDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);
        Assert.Null(delivery.LastAttemptAt);
        Assert.Null(delivery.SentAt);
        Assert.Null(delivery.FailureReason);
        Assert.Null(delivery.LeaseOwnerToken);
        Assert.Null(delivery.LeaseAcquiredAt);
        Assert.Null(delivery.LeaseExpiresAt);
        Assert.Equal(Now, delivery.CreatedAt);
        Assert.False(delivery.IsTerminal);
        Assert.True(delivery.HasAttemptsRemaining);
    }

    [Fact]
    public void Claim_From_Pending_Moves_To_Sending_Takes_Lease_And_Succeeds()
    {
        var delivery = Create();
        var token = Guid.NewGuid();

        var result = delivery.Claim(token, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailDeliveryStatus.Sending, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal(Now, delivery.LastAttemptAt);
        Assert.Equal(token, delivery.LeaseOwnerToken);
        Assert.Equal(Now, delivery.LeaseAcquiredAt);
        Assert.Equal(Now.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes), delivery.LeaseExpiresAt);
        Assert.False(delivery.IsLeaseExpired(Now));
    }

    [Fact]
    public void Claim_Twice_Without_Release_Second_Fails_While_Lease_Still_Live()
    {
        var delivery = Create();
        Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);

        // 1 minute later — well inside the 10 minute lease.
        var result = delivery.Claim(Guid.NewGuid(), Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(1, delivery.AttemptCount);
    }

    [Fact]
    public void Claim_On_Sending_Row_Succeeds_Once_Lease_Has_Expired()
    {
        var delivery = Create();
        Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);

        // Exactly at expiry: LeaseExpiresAt <= now => expired.
        var atExpiry = Now.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes);
        Assert.True(delivery.IsLeaseExpired(atExpiry));

        var result = delivery.Claim(Guid.NewGuid(), atExpiry);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, delivery.AttemptCount);
        Assert.Equal(EmailDeliveryStatus.Sending, delivery.Status);
    }

    [Fact]
    public void Claim_On_Sending_Row_Just_Before_Lease_Expiry_Still_Fails()
    {
        var delivery = Create();
        Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);

        var justBefore = Now.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes).AddTicks(-1);
        Assert.False(delivery.IsLeaseExpired(justBefore));

        Assert.True(delivery.Claim(Guid.NewGuid(), justBefore).IsFailure);
        Assert.Equal(1, delivery.AttemptCount);
    }

    [Fact]
    public void Claim_With_Empty_Owner_Token_Fails()
    {
        var delivery = Create();

        var result = delivery.Claim(Guid.Empty, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(EmailDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);
    }

    [Theory]
    [InlineData((int)EmailDeliveryStatus.Sent)]
    [InlineData((int)EmailDeliveryStatus.Failed)]
    [InlineData((int)EmailDeliveryStatus.Skipped)]
    public void Claim_When_Terminal_Fails(int statusValue)
    {
        var status = (EmailDeliveryStatus)statusValue;
        var delivery = Create();
        switch (status)
        {
            case EmailDeliveryStatus.Sent:
                Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);
                delivery.MarkSent(Now);
                break;
            case EmailDeliveryStatus.Failed:
                delivery.MarkFailed("Email delivery failed.");
                break;
            case EmailDeliveryStatus.Skipped:
                delivery.MarkSkipped("No internal operations recipient configured.");
                break;
        }

        Assert.True(delivery.IsTerminal);
        var attemptsBefore = delivery.AttemptCount;

        var result = delivery.Claim(Guid.NewGuid(), Now.AddHours(1));

        Assert.True(result.IsFailure);
        Assert.Equal(status, delivery.Status);
        Assert.Equal(attemptsBefore, delivery.AttemptCount);
    }

    [Fact]
    public void Claim_When_AttemptCount_Already_At_MaxAttempts_Fails()
    {
        var delivery = Create();
        var now = Now;

        // Exhaust the budget across expired leases.
        for (var i = 0; i < OperationalAlertEmailDelivery.MaxAttempts; i++)
        {
            Assert.True(delivery.Claim(Guid.NewGuid(), now).IsSuccess);
            now = now.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes);
        }

        Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, delivery.AttemptCount);
        Assert.False(delivery.HasAttemptsRemaining);

        var result = delivery.Claim(Guid.NewGuid(), now);

        Assert.True(result.IsFailure);
        Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, delivery.AttemptCount);
    }

    [Fact]
    public void Claim_Exactly_MaxAttempts_Minus_One_Times_Still_Allows_One_More()
    {
        var delivery = Create();
        var now = Now;
        for (var i = 0; i < OperationalAlertEmailDelivery.MaxAttempts - 1; i++)
        {
            Assert.True(delivery.Claim(Guid.NewGuid(), now).IsSuccess);
            now = now.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes);
        }

        Assert.True(delivery.HasAttemptsRemaining);
        Assert.True(delivery.Claim(Guid.NewGuid(), now).IsSuccess);
        Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, delivery.AttemptCount);
    }

    [Fact]
    public void ReleaseForRetry_From_Sending_Returns_To_Pending_And_Clears_Lease()
    {
        var delivery = Create();
        Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);

        delivery.ReleaseForRetry(Now.AddMinutes(1));

        Assert.Equal(EmailDeliveryStatus.Pending, delivery.Status);
        Assert.Null(delivery.LeaseOwnerToken);
        Assert.Null(delivery.LeaseAcquiredAt);
        Assert.Null(delivery.LeaseExpiresAt);
        Assert.Equal(1, delivery.AttemptCount); // attempt already consumed by the Claim
    }

    [Fact]
    public void ReleaseForRetry_Is_A_NoOp_When_Not_Sending()
    {
        var delivery = Create(); // Pending
        delivery.ReleaseForRetry(Now);
        Assert.Equal(EmailDeliveryStatus.Pending, delivery.Status);

        Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);
        delivery.MarkSent(Now);
        delivery.ReleaseForRetry(Now.AddMinutes(1));
        Assert.Equal(EmailDeliveryStatus.Sent, delivery.Status);
    }

    [Fact]
    public void ReleaseForRetry_Allows_A_Fresh_Claim()
    {
        var delivery = Create();
        Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);
        delivery.ReleaseForRetry(Now.AddMinutes(1));

        var result = delivery.Claim(Guid.NewGuid(), Now.AddMinutes(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, delivery.AttemptCount);
    }

    [Fact]
    public void MarkSent_Clears_Lease_And_FailureReason()
    {
        var delivery = Create();
        Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);

        delivery.MarkSent(Now.AddMinutes(1));

        Assert.Equal(EmailDeliveryStatus.Sent, delivery.Status);
        Assert.Equal(Now.AddMinutes(1), delivery.SentAt);
        Assert.Null(delivery.FailureReason);
        Assert.Null(delivery.LeaseOwnerToken);
        Assert.Null(delivery.LeaseAcquiredAt);
        Assert.Null(delivery.LeaseExpiresAt);
    }

    [Fact]
    public void MarkFailed_Clears_Lease_And_Sets_Reason()
    {
        var delivery = Create();
        Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);

        delivery.MarkFailed("Email provider error.");

        Assert.Equal(EmailDeliveryStatus.Failed, delivery.Status);
        Assert.Equal("Email provider error.", delivery.FailureReason);
        Assert.Null(delivery.SentAt);
        Assert.Null(delivery.LeaseOwnerToken);
        Assert.Null(delivery.LeaseExpiresAt);
    }

    [Fact]
    public void MarkSkipped_Clears_Lease_And_Sets_Reason()
    {
        var delivery = Create();
        Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);

        delivery.MarkSkipped("No internal operations recipient configured.");

        Assert.Equal(EmailDeliveryStatus.Skipped, delivery.Status);
        Assert.Equal("No internal operations recipient configured.", delivery.FailureReason);
        Assert.Null(delivery.LeaseOwnerToken);
        Assert.Null(delivery.LeaseExpiresAt);
    }

    [Fact]
    public void IsLeaseExpired_Is_True_When_LeaseExpiresAt_Is_Null()
    {
        var delivery = Create();
        Assert.True(delivery.IsLeaseExpired(Now));
        Assert.True(delivery.IsLeaseExpired(DateTimeOffset.MinValue));
    }

    // Ticket 3L: HasLiveOwner -------------------------------------------------------------------

    [Fact]
    public void HasLiveOwner_Is_True_When_Sending_With_A_Live_Lease()
    {
        var delivery = Create();
        Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);

        Assert.True(delivery.HasLiveOwner(Now));
        Assert.True(delivery.HasLiveOwner(Now.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes).AddTicks(-1)));
    }

    [Fact]
    public void HasLiveOwner_Is_False_When_Sending_But_Lease_Has_Expired()
    {
        var delivery = Create();
        Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);

        // Exactly at expiry the lease counts as expired (LeaseExpiresAt <= now).
        var atExpiry = Now.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes);
        Assert.Equal(EmailDeliveryStatus.Sending, delivery.Status);
        Assert.False(delivery.HasLiveOwner(atExpiry));
        Assert.False(delivery.HasLiveOwner(atExpiry.AddHours(1)));
    }

    [Fact]
    public void HasLiveOwner_Is_False_When_Pending()
    {
        var delivery = Create();
        Assert.Equal(EmailDeliveryStatus.Pending, delivery.Status);
        Assert.False(delivery.HasLiveOwner(Now));
    }

    [Theory]
    [InlineData((int)EmailDeliveryStatus.Sent)]
    [InlineData((int)EmailDeliveryStatus.Failed)]
    [InlineData((int)EmailDeliveryStatus.Skipped)]
    public void HasLiveOwner_Is_False_When_Terminal(int statusValue)
    {
        var status = (EmailDeliveryStatus)statusValue;
        var delivery = Create();
        switch (status)
        {
            case EmailDeliveryStatus.Sent:
                Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);
                delivery.MarkSent(Now);
                break;
            case EmailDeliveryStatus.Failed:
                delivery.MarkFailed("Email delivery failed.");
                break;
            case EmailDeliveryStatus.Skipped:
                delivery.MarkSkipped("No internal operations recipient configured.");
                break;
        }

        Assert.False(delivery.HasLiveOwner(Now));
    }

    // Ticket 3L: MarkFailedIfNoLiveOwner -----------------------------------------------------------

    [Fact]
    public void MarkFailedIfNoLiveOwner_Returns_Conflict_And_Leaves_State_Untouched_When_A_Live_Owner_Holds_The_Lease()
    {
        var delivery = Create();
        var token = Guid.NewGuid();
        Assert.True(delivery.Claim(token, Now).IsSuccess);
        var leaseExpiresAt = delivery.LeaseExpiresAt;

        var result = delivery.MarkFailedIfNoLiveOwner("Delivery abandoned after repeated interruptions.", Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(EmailDeliveryStatus.Sending, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Null(delivery.FailureReason);
        Assert.Equal(token, delivery.LeaseOwnerToken);
        Assert.Equal(leaseExpiresAt, delivery.LeaseExpiresAt);
    }

    [Theory]
    [InlineData((int)EmailDeliveryStatus.Sent)]
    [InlineData((int)EmailDeliveryStatus.Failed)]
    [InlineData((int)EmailDeliveryStatus.Skipped)]
    public void MarkFailedIfNoLiveOwner_Returns_Conflict_When_Already_Terminal(int statusValue)
    {
        var status = (EmailDeliveryStatus)statusValue;
        var delivery = Create();
        switch (status)
        {
            case EmailDeliveryStatus.Sent:
                Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);
                delivery.MarkSent(Now);
                break;
            case EmailDeliveryStatus.Failed:
                delivery.MarkFailed("Email provider error.");
                break;
            case EmailDeliveryStatus.Skipped:
                delivery.MarkSkipped("No internal operations recipient configured.");
                break;
        }

        var reasonBefore = delivery.FailureReason;

        var result = delivery.MarkFailedIfNoLiveOwner("Delivery abandoned after repeated interruptions.", Now.AddHours(1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(status, delivery.Status);
        Assert.Equal(reasonBefore, delivery.FailureReason);
    }

    [Fact]
    public void MarkFailedIfNoLiveOwner_Fails_The_Row_When_Pending()
    {
        var delivery = Create();

        var result = delivery.MarkFailedIfNoLiveOwner("Delivery abandoned after repeated interruptions.", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailDeliveryStatus.Failed, delivery.Status);
        Assert.Equal("Delivery abandoned after repeated interruptions.", delivery.FailureReason);
        Assert.Null(delivery.LeaseOwnerToken);
        Assert.Null(delivery.LeaseAcquiredAt);
        Assert.Null(delivery.LeaseExpiresAt);
    }

    [Fact]
    public void MarkFailedIfNoLiveOwner_Fails_The_Row_When_Sending_But_The_Lease_Has_Expired()
    {
        var delivery = Create();
        Assert.True(delivery.Claim(Guid.NewGuid(), Now).IsSuccess);
        var afterExpiry = Now.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes + 1);

        var result = delivery.MarkFailedIfNoLiveOwner("Delivery abandoned after repeated interruptions.", afterExpiry);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailDeliveryStatus.Failed, delivery.Status);
        Assert.Equal("Delivery abandoned after repeated interruptions.", delivery.FailureReason);
        Assert.Null(delivery.LeaseOwnerToken);
        Assert.Null(delivery.LeaseExpiresAt);
    }
}
