using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class CustomerSubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartTrial_Sets_Expected_Fields()
    {
        var companyId = Guid.NewGuid();

        var subscription = CustomerSubscription.StartTrial(companyId, Now, trialLengthDays: 14);

        Assert.Equal(companyId, subscription.CompanyId);
        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
        Assert.Equal(Now, subscription.TrialStartedAt);
        Assert.Equal(Now.AddDays(14), subscription.TrialExpiresAt);
        Assert.False(subscription.CancelAtPeriodEnd);
        Assert.Equal(Now, subscription.CreatedAt);
        Assert.Equal(Now, subscription.UpdatedAt);
    }

    [Fact]
    public void MarkExpiredIfNeeded_Returns_False_Before_Expiry()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        var transitioned = subscription.MarkExpiredIfNeeded(Now.AddDays(13));

        Assert.False(transitioned);
        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
    }

    [Fact]
    public void MarkExpiredIfNeeded_Transitions_To_TrialExpired_At_Expiry()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        var expiryMoment = Now.AddDays(14);

        var transitioned = subscription.MarkExpiredIfNeeded(expiryMoment);

        Assert.True(transitioned);
        Assert.Equal(SubscriptionStatus.TrialExpired, subscription.Status);
        Assert.Equal(expiryMoment, subscription.UpdatedAt);
    }

    [Fact]
    public void MarkExpiredIfNeeded_Transitions_To_TrialExpired_After_Expiry()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        var transitioned = subscription.MarkExpiredIfNeeded(Now.AddDays(20));

        Assert.True(transitioned);
        Assert.Equal(SubscriptionStatus.TrialExpired, subscription.Status);
    }

    [Fact]
    public void MarkExpiredIfNeeded_Is_Idempotent_Once_Already_Expired()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.MarkExpiredIfNeeded(Now.AddDays(14));

        var transitionedAgain = subscription.MarkExpiredIfNeeded(Now.AddDays(15));

        Assert.False(transitionedAgain);
        Assert.Equal(SubscriptionStatus.TrialExpired, subscription.Status);
    }

    [Fact]
    public void MarkExpiredIfNeeded_Returns_False_When_Status_Is_Not_Trial()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddYears(1), Now);

        var transitioned = subscription.MarkExpiredIfNeeded(Now.AddDays(365));

        Assert.False(transitioned);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void ActivateSubscription_Sets_Expected_Fields()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        var currentPeriodEnd = Now.AddMonths(1);

        subscription.ActivateSubscription("cus_123", "sub_456", "price_789", currentPeriodEnd, Now);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal("cus_123", subscription.StripeCustomerId);
        Assert.Equal("sub_456", subscription.StripeSubscriptionId);
        Assert.Equal("price_789", subscription.PriceId);
        Assert.Equal(currentPeriodEnd, subscription.CurrentPeriodEnd);
        Assert.False(subscription.CancelAtPeriodEnd);
        Assert.Equal(Now, subscription.UpdatedAt);
    }

    [Fact]
    public void UpdateFromStripe_Sets_Expected_Fields()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        var currentPeriodEnd = Now.AddMonths(1);
        var updateAt = Now.AddDays(1);

        subscription.UpdateFromStripe(SubscriptionStatus.PastDue, currentPeriodEnd, cancelAtPeriodEnd: true, updateAt);

        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.Equal(currentPeriodEnd, subscription.CurrentPeriodEnd);
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.Equal(updateAt, subscription.UpdatedAt);
    }

    [Fact]
    public void ActivateSubscription_With_Null_CurrentPeriodEnd_Sets_Null()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        subscription.ActivateSubscription("cus_123", "sub_456", "price_789", currentPeriodEnd: null, Now);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Null(subscription.CurrentPeriodEnd);
        Assert.False(subscription.CancelAtPeriodEnd);
        Assert.Equal(Now, subscription.UpdatedAt);
    }

    [Fact]
    public void ActivateSubscription_With_Set_CurrentPeriodEnd_Persists_It()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        var currentPeriodEnd = Now.AddMonths(1);

        subscription.ActivateSubscription("cus_123", "sub_456", "price_789", currentPeriodEnd, Now);

        Assert.Equal(currentPeriodEnd, subscription.CurrentPeriodEnd);
    }

    [Fact]
    public void ActivateSubscription_Clears_Any_Prior_CancelAtPeriodEnd_Flag()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddYears(1), Now);
        subscription.RequestCancellation(Now.AddDays(1));

        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddYears(1), Now.AddDays(2));

        Assert.False(subscription.CancelAtPeriodEnd);
    }

    [Fact]
    public void UpdateFromStripe_Transitions_Active_To_PastDue_To_Canceled()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);

        subscription.UpdateFromStripe(SubscriptionStatus.PastDue, Now.AddMonths(1), cancelAtPeriodEnd: false, Now.AddDays(1));
        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.Equal(Now.AddDays(1), subscription.UpdatedAt);

        subscription.UpdateFromStripe(SubscriptionStatus.Canceled, Now.AddMonths(1), cancelAtPeriodEnd: true, Now.AddDays(2));
        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.Equal(Now.AddDays(2), subscription.UpdatedAt);
    }

    [Fact]
    public void UpdateFromStripe_Is_Idempotent_When_Reapplied_With_Same_Payload()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        var currentPeriodEnd = Now.AddMonths(2);
        var updateAt = Now.AddDays(1);

        subscription.UpdateFromStripe(SubscriptionStatus.Active, currentPeriodEnd, cancelAtPeriodEnd: true, updateAt);
        subscription.UpdateFromStripe(SubscriptionStatus.Active, currentPeriodEnd, cancelAtPeriodEnd: true, updateAt);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(currentPeriodEnd, subscription.CurrentPeriodEnd);
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.Equal(updateAt, subscription.UpdatedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpdateFromStripe_Toggles_CancelAtPeriodEnd(bool cancelAtPeriodEnd)
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);

        subscription.UpdateFromStripe(SubscriptionStatus.Active, Now.AddMonths(1), cancelAtPeriodEnd, Now.AddDays(1));

        Assert.Equal(cancelAtPeriodEnd, subscription.CancelAtPeriodEnd);
    }

    [Fact]
    public void RequestCancellation_Sets_CancelAtPeriodEnd()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddYears(1), Now);
        var requestAt = Now.AddDays(5);

        subscription.RequestCancellation(requestAt);

        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.Equal(requestAt, subscription.UpdatedAt);
    }

    [Fact]
    public void Resume_Clears_CancelAtPeriodEnd()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddYears(1), Now);
        subscription.RequestCancellation(Now.AddDays(5));
        var resumeAt = Now.AddDays(6);

        subscription.Resume(resumeAt);

        Assert.False(subscription.CancelAtPeriodEnd);
        Assert.Equal(resumeAt, subscription.UpdatedAt);
    }
}
