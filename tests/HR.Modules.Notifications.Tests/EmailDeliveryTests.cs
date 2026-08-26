using HR.Modules.Notifications.Domain;

namespace HR.Modules.Notifications.Tests;

public class EmailDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_Pending_Status_And_IdempotencyKey_Equal_To_NotificationId()
    {
        var id             = Guid.NewGuid();
        var companyId      = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        var delivery = EmailDelivery.Create(id, companyId, notificationId, Now);

        Assert.Equal(id,               delivery.Id);
        Assert.Equal(companyId,        delivery.CompanyId);
        Assert.Equal(notificationId,   delivery.NotificationId);
        Assert.Equal(notificationId,   delivery.IdempotencyKey);
        Assert.Equal(EmailDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0,                delivery.AttemptCount);
        Assert.Null(delivery.LastAttemptAt);
        Assert.Null(delivery.SentAt);
        Assert.Null(delivery.FailureReason);
        Assert.Equal(Now,              delivery.CreatedAt);
    }

    [Fact]
    public void RecordAttempt_Increments_AttemptCount_And_Sets_LastAttemptAt()
    {
        var delivery = EmailDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        delivery.RecordAttempt(Now.AddMinutes(1));

        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal(Now.AddMinutes(1), delivery.LastAttemptAt);
    }

    [Fact]
    public void RecordAttempt_Called_Multiple_Times_Keeps_Incrementing_And_Updates_LastAttemptAt()
    {
        var delivery = EmailDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        delivery.RecordAttempt(Now.AddMinutes(1));
        delivery.RecordAttempt(Now.AddMinutes(2));

        Assert.Equal(2, delivery.AttemptCount);
        Assert.Equal(Now.AddMinutes(2), delivery.LastAttemptAt);
    }

    [Fact]
    public void MarkSent_Sets_Status_SentAt_And_Clears_FailureReason()
    {
        var delivery = EmailDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        delivery.RecordAttempt(Now.AddMinutes(1));
        delivery.MarkFailed("Email provider error."); // simulate a prior failed attempt

        delivery.MarkSent(Now.AddMinutes(2));

        Assert.Equal(EmailDeliveryStatus.Sent, delivery.Status);
        Assert.Equal(Now.AddMinutes(2),        delivery.SentAt);
        Assert.Null(delivery.FailureReason);
    }

    [Fact]
    public void MarkFailed_Sets_Status_And_FailureReason()
    {
        var delivery = EmailDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        delivery.MarkFailed("Invalid recipient address.");

        Assert.Equal(EmailDeliveryStatus.Failed, delivery.Status);
        Assert.Equal("Invalid recipient address.", delivery.FailureReason);
    }

    // SET-06 -------------------------------------------------------------------------------------

    [Fact]
    public void MarkSkipped_Sets_Status_Skipped_And_FailureReason()
    {
        var delivery = EmailDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        delivery.MarkSkipped("Email notifications disabled for this company.");

        Assert.Equal(EmailDeliveryStatus.Skipped, delivery.Status);
        Assert.Equal("Email notifications disabled for this company.", delivery.FailureReason);
    }

    [Fact]
    public void MarkSkipped_Is_Distinct_From_MarkFailed()
    {
        var delivery = EmailDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);

        delivery.MarkSkipped("Email notifications disabled for this company.");

        Assert.NotEqual(EmailDeliveryStatus.Failed, delivery.Status);
    }
}
