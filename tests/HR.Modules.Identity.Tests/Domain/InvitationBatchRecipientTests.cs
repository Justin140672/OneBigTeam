using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Tests.Domain;

public class InvitationBatchRecipientTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_Status_Waiting_And_Fields()
    {
        var batchId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var recipient = InvitationBatchRecipient.Create(batchId, employeeId, "a@test.com", Now);

        Assert.Equal(InvitationBatchRecipient.StatusWaiting, recipient.Status);
        Assert.Equal(batchId, recipient.BatchId);
        Assert.Equal(employeeId, recipient.EmployeeId);
        Assert.Equal("a@test.com", recipient.Email);
        Assert.Equal(Now, recipient.CreatedAt);
        Assert.Null(recipient.InviteId);
        Assert.Null(recipient.ProcessedAt);
        Assert.Null(recipient.FailureReason);
    }

    [Fact]
    public void MarkProcessing_Sets_Status_And_Clears_FailureReason()
    {
        var recipient = InvitationBatchRecipient.Create(Guid.NewGuid(), Guid.NewGuid(), "a@test.com", Now);
        recipient.MarkFailed("boom");

        recipient.MarkProcessing();

        Assert.Equal(InvitationBatchRecipient.StatusProcessing, recipient.Status);
        Assert.Null(recipient.FailureReason);
    }

    [Fact]
    public void RecordInviteCreated_Sets_InviteId_Without_Changing_Status()
    {
        var recipient = InvitationBatchRecipient.Create(Guid.NewGuid(), Guid.NewGuid(), "a@test.com", Now);
        recipient.MarkProcessing();
        var inviteId = Guid.NewGuid();

        recipient.RecordInviteCreated(inviteId);

        Assert.Equal(inviteId, recipient.InviteId);
        Assert.Equal(InvitationBatchRecipient.StatusProcessing, recipient.Status);
    }

    [Fact]
    public void MarkSent_Sets_Status_ProcessedAt_And_Clears_FailureReason()
    {
        var recipient = InvitationBatchRecipient.Create(Guid.NewGuid(), Guid.NewGuid(), "a@test.com", Now);
        recipient.MarkProcessing();

        recipient.MarkSent(Now.AddMinutes(1));

        Assert.Equal(InvitationBatchRecipient.StatusSent, recipient.Status);
        Assert.Equal(Now.AddMinutes(1), recipient.ProcessedAt);
        Assert.Null(recipient.FailureReason);
    }

    [Fact]
    public void MarkSkipped_Sets_Status_Reason_And_ProcessedAt()
    {
        var recipient = InvitationBatchRecipient.Create(Guid.NewGuid(), Guid.NewGuid(), "a@test.com", Now);
        recipient.MarkProcessing();

        recipient.MarkSkipped("AlreadyHasAccount", Now.AddMinutes(1));

        Assert.Equal(InvitationBatchRecipient.StatusSkipped, recipient.Status);
        Assert.Equal("AlreadyHasAccount", recipient.FailureReason);
        Assert.Equal(Now.AddMinutes(1), recipient.ProcessedAt);
    }

    [Fact]
    public void MarkFailed_Sets_Status_And_Reason_Without_ProcessedAt()
    {
        var recipient = InvitationBatchRecipient.Create(Guid.NewGuid(), Guid.NewGuid(), "a@test.com", Now);
        recipient.MarkProcessing();

        recipient.MarkFailed("Email delivery failed");

        Assert.Equal(InvitationBatchRecipient.StatusFailed, recipient.Status);
        Assert.Equal("Email delivery failed", recipient.FailureReason);
        Assert.Null(recipient.ProcessedAt);
    }

    [Fact]
    public void ResetForRetry_Restores_Waiting_Clears_Reason_And_ProcessedAt()
    {
        var recipient = InvitationBatchRecipient.Create(Guid.NewGuid(), Guid.NewGuid(), "a@test.com", Now);
        recipient.MarkProcessing();
        recipient.MarkFailed("Email delivery failed");

        recipient.ResetForRetry();

        Assert.Equal(InvitationBatchRecipient.StatusWaiting, recipient.Status);
        Assert.Null(recipient.FailureReason);
        Assert.Null(recipient.ProcessedAt);
    }

    [Fact]
    public void ResetForRetry_Does_Not_Clear_InviteId_So_A_Prior_Invite_Is_Reused()
    {
        var recipient = InvitationBatchRecipient.Create(Guid.NewGuid(), Guid.NewGuid(), "a@test.com", Now);
        var inviteId = Guid.NewGuid();
        recipient.RecordInviteCreated(inviteId);
        recipient.MarkFailed("Email delivery failed");

        recipient.ResetForRetry();

        Assert.Equal(inviteId, recipient.InviteId);
    }

    [Fact]
    public void ResetForRetry_From_Sent_Also_Resets_State_Domain_Level_But_Callers_Never_Call_It_On_Sent()
    {
        var recipient = InvitationBatchRecipient.Create(Guid.NewGuid(), Guid.NewGuid(), "a@test.com", Now);
        recipient.MarkProcessing();
        recipient.MarkSent(Now.AddMinutes(1));

        recipient.ResetForRetry();

        Assert.Equal(InvitationBatchRecipient.StatusWaiting, recipient.Status);
    }
}
