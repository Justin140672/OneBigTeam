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

    [Fact]
    public void ExtendTrial_Succeeds_From_Trial_Status()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        var newExpiry = Now.AddDays(30);
        var extendAt = Now.AddDays(1);

        var result = subscription.ExtendTrial(newExpiry, extendAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
        Assert.Equal(newExpiry, subscription.TrialExpiresAt);
        Assert.Equal(extendAt, subscription.UpdatedAt);
    }

    [Fact]
    public void ExtendTrial_Succeeds_From_TrialExpired_And_Reactivates_To_Trial()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.MarkExpiredIfNeeded(Now.AddDays(14));
        var newExpiry = Now.AddDays(45);
        var extendAt = Now.AddDays(15);

        var result = subscription.ExtendTrial(newExpiry, extendAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Trial, subscription.Status);
        Assert.Equal(newExpiry, subscription.TrialExpiresAt);
        Assert.Equal(extendAt, subscription.UpdatedAt);
    }

    [Fact]
    public void ExtendTrial_Fails_When_Status_Is_Active()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);

        var result = subscription.ExtendTrial(Now.AddDays(30), Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void ExtendTrial_Fails_When_Status_Is_PastDue()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        subscription.UpdateFromStripe(SubscriptionStatus.PastDue, Now.AddMonths(1), cancelAtPeriodEnd: false, Now.AddDays(1));

        var result = subscription.ExtendTrial(Now.AddDays(30), Now.AddDays(2));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void ExtendTrial_Fails_When_Status_Is_Canceled()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        subscription.UpdateFromStripe(SubscriptionStatus.Canceled, Now.AddMonths(1), cancelAtPeriodEnd: true, Now.AddDays(1));

        var result = subscription.ExtendTrial(Now.AddDays(30), Now.AddDays(2));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void ExtendTrial_Fails_When_NewTrialExpiresAt_Is_In_The_Past()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        var result = subscription.ExtendTrial(Now.AddDays(-1), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void ExtendTrial_Fails_When_NewTrialExpiresAt_Equals_Now()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        var result = subscription.ExtendTrial(Now, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void AdminCancelAtPeriodEnd_Succeeds_From_Active()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        var cancelAt = Now.AddDays(1);

        var result = subscription.AdminCancelAtPeriodEnd(cancelAt);

        Assert.True(result.IsSuccess);
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.Equal(cancelAt, subscription.UpdatedAt);
    }

    [Fact]
    public void AdminCancelAtPeriodEnd_Succeeds_From_PastDue()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        subscription.UpdateFromStripe(SubscriptionStatus.PastDue, Now.AddMonths(1), cancelAtPeriodEnd: false, Now.AddDays(1));

        var result = subscription.AdminCancelAtPeriodEnd(Now.AddDays(2));

        Assert.True(result.IsSuccess);
        Assert.True(subscription.CancelAtPeriodEnd);
    }

    [Fact]
    public void AdminCancelAtPeriodEnd_Fails_From_Trial()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        var result = subscription.AdminCancelAtPeriodEnd(Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void AdminCancelAtPeriodEnd_Fails_From_TrialExpired()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.MarkExpiredIfNeeded(Now.AddDays(14));

        var result = subscription.AdminCancelAtPeriodEnd(Now.AddDays(15));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void AdminCancelAtPeriodEnd_Fails_From_Canceled()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        subscription.UpdateFromStripe(SubscriptionStatus.Canceled, Now.AddMonths(1), cancelAtPeriodEnd: true, Now.AddDays(1));

        var result = subscription.AdminCancelAtPeriodEnd(Now.AddDays(2));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void ReinstateCancelledSubscription_Succeeds_From_Canceled_And_Moves_To_Active()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        subscription.UpdateFromStripe(SubscriptionStatus.Canceled, Now.AddMonths(1), cancelAtPeriodEnd: true, Now.AddDays(1));
        var reinstateAt = Now.AddDays(2);

        var result = subscription.ReinstateCancelledSubscription(reinstateAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.False(subscription.CancelAtPeriodEnd);
        Assert.Equal(reinstateAt, subscription.UpdatedAt);
    }

    [Fact]
    public void ReinstateCancelledSubscription_Succeeds_When_CancelAtPeriodEnd_Pending_Regardless_Of_Status()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);
        subscription.RequestCancellation(Now.AddDays(1));

        var result = subscription.ReinstateCancelledSubscription(Now.AddDays(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.False(subscription.CancelAtPeriodEnd);
    }

    [Fact]
    public void ReinstateCancelledSubscription_Fails_When_Neither_Cancelled_Nor_Scheduled_To_Cancel()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ActivateSubscription("cus_1", "sub_1", "price_1", Now.AddMonths(1), Now);

        var result = subscription.ReinstateCancelledSubscription(Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void ForceReadOnly_Succeeds_On_First_Call()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        var forceAt = Now.AddDays(1);

        var result = subscription.ForceReadOnly(forceAt);

        Assert.True(result.IsSuccess);
        Assert.True(subscription.AdminForcedReadOnly);
        Assert.Equal(forceAt, subscription.UpdatedAt);
    }

    [Fact]
    public void ForceReadOnly_Fails_On_Second_Call()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ForceReadOnly(Now.AddDays(1));

        var result = subscription.ForceReadOnly(Now.AddDays(2));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.True(subscription.AdminForcedReadOnly);
    }

    [Fact]
    public void ResumeService_Succeeds_When_Currently_Forced()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ForceReadOnly(Now.AddDays(1));
        var resumeAt = Now.AddDays(2);

        var result = subscription.ResumeService(resumeAt);

        Assert.True(result.IsSuccess);
        Assert.False(subscription.AdminForcedReadOnly);
        Assert.Equal(resumeAt, subscription.UpdatedAt);
    }

    [Fact]
    public void ResumeService_Fails_When_Not_Currently_Forced()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        var result = subscription.ResumeService(Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void HasPendingDeletion_Is_False_By_Default()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        Assert.False(subscription.HasPendingDeletion);
    }

    [Fact]
    public void ScheduleDeletion_Succeeds_And_Sets_Expected_Fields()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        var scheduledByUserId = Guid.NewGuid();
        var scheduledFor = Now.AddDays(30);
        var scheduleAt = Now.AddDays(1);

        var result = subscription.ScheduleDeletion(scheduledByUserId, scheduledFor, scheduleAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(scheduledFor, subscription.DeletionScheduledAt);
        Assert.Equal(scheduledByUserId, subscription.DeletionScheduledBy);
        Assert.Null(subscription.DeletionCancelledAt);
        Assert.Null(subscription.DeletionExecutedAt);
        Assert.Equal(scheduleAt, subscription.UpdatedAt);
        Assert.True(subscription.HasPendingDeletion);
    }

    [Fact]
    public void ScheduleDeletion_Is_ReSchedulable_And_Resets_CancelledAt()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);
        subscription.CancelScheduledDeletion(Now.AddDays(1));

        var newScheduledByUserId = Guid.NewGuid();
        var newScheduledFor = Now.AddDays(60);
        var result = subscription.ScheduleDeletion(newScheduledByUserId, newScheduledFor, Now.AddDays(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(newScheduledFor, subscription.DeletionScheduledAt);
        Assert.Equal(newScheduledByUserId, subscription.DeletionScheduledBy);
        Assert.Null(subscription.DeletionCancelledAt);
        Assert.True(subscription.HasPendingDeletion);
    }

    [Fact]
    public void ScheduleDeletion_Fails_When_Deletion_Already_Executed()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);
        subscription.ExecuteDeletion(Now.AddDays(31));

        var result = subscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(60), Now.AddDays(32));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void ScheduleDeletion_Fails_When_ScheduledFor_Equals_Now()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        var result = subscription.ScheduleDeletion(Guid.NewGuid(), Now, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Null(subscription.DeletionScheduledAt);
    }

    [Fact]
    public void ScheduleDeletion_Fails_When_ScheduledFor_Is_In_The_Past()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        var result = subscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(-1), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void ScheduleDeletion_Accepts_Null_ScheduledByUserId()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        var result = subscription.ScheduleDeletion(null, Now.AddDays(30), Now);

        Assert.True(result.IsSuccess);
        Assert.Null(subscription.DeletionScheduledBy);
    }

    [Fact]
    public void CancelScheduledDeletion_Succeeds_When_Pending()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);
        var cancelAt = Now.AddDays(1);

        var result = subscription.CancelScheduledDeletion(cancelAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(cancelAt, subscription.DeletionCancelledAt);
        Assert.Equal(cancelAt, subscription.UpdatedAt);
        Assert.False(subscription.HasPendingDeletion);
        Assert.NotNull(subscription.DeletionScheduledAt);
    }

    [Fact]
    public void CancelScheduledDeletion_Fails_When_No_Deletion_Scheduled()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        var result = subscription.CancelScheduledDeletion(Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void CancelScheduledDeletion_Fails_When_Already_Cancelled()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);
        subscription.CancelScheduledDeletion(Now.AddDays(1));

        var result = subscription.CancelScheduledDeletion(Now.AddDays(2));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void CancelScheduledDeletion_Fails_When_Already_Executed()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);
        subscription.ExecuteDeletion(Now.AddDays(1));

        var result = subscription.CancelScheduledDeletion(Now.AddDays(2));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void ExecuteDeletion_Succeeds_When_Pending_Sets_ExecutedAt_And_Forces_ReadOnly()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);
        var executeAt = Now.AddDays(1);

        var result = subscription.ExecuteDeletion(executeAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(executeAt, subscription.DeletionExecutedAt);
        Assert.True(subscription.AdminForcedReadOnly);
        Assert.Equal(executeAt, subscription.UpdatedAt);
        Assert.False(subscription.HasPendingDeletion);
    }

    [Fact]
    public void ExecuteDeletion_Fails_When_No_Deletion_Scheduled()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);

        var result = subscription.ExecuteDeletion(Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.False(subscription.AdminForcedReadOnly);
    }

    [Fact]
    public void ExecuteDeletion_Fails_When_Already_Cancelled()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);
        subscription.CancelScheduledDeletion(Now.AddDays(1));

        var result = subscription.ExecuteDeletion(Now.AddDays(2));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void ExecuteDeletion_Fails_When_Already_Executed()
    {
        var subscription = CustomerSubscription.StartTrial(Guid.NewGuid(), Now, trialLengthDays: 14);
        subscription.ScheduleDeletion(Guid.NewGuid(), Now.AddDays(30), Now);
        subscription.ExecuteDeletion(Now.AddDays(1));

        var result = subscription.ExecuteDeletion(Now.AddDays(2));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }
}
