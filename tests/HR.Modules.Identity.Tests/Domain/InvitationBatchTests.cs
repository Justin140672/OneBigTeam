using HR.Modules.Identity.Domain;

namespace HR.Modules.Identity.Tests.Domain;

public class InvitationBatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Sets_Status_Queued_And_Fields()
    {
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var batch = InvitationBatch.Create(companyId, actorId, Now, idempotencyKey: null);

        Assert.Equal(InvitationBatch.StatusQueued, batch.Status);
        Assert.Equal(companyId, batch.CompanyId);
        Assert.Equal(actorId, batch.RequestedByUserId);
        Assert.Equal(Now, batch.CreatedAt);
        Assert.Null(batch.StartedAt);
        Assert.Null(batch.CompletedAt);
        Assert.Null(batch.IdempotencyKey);
    }

    [Fact]
    public void Create_Sets_IdempotencyKey_When_Supplied()
    {
        var batch = InvitationBatch.Create(Guid.NewGuid(), Guid.NewGuid(), Now, idempotencyKey: "key-1");

        Assert.Equal("key-1", batch.IdempotencyKey);
    }

    [Fact]
    public void MarkProcessing_Sets_Status_And_StartedAt()
    {
        var batch = InvitationBatch.Create(Guid.NewGuid(), Guid.NewGuid(), Now, idempotencyKey: null);

        batch.MarkProcessing(Now.AddMinutes(1));

        Assert.Equal(InvitationBatch.StatusProcessing, batch.Status);
        Assert.Equal(Now.AddMinutes(1), batch.StartedAt);
    }

    [Fact]
    public void MarkProcessing_Does_Not_Overwrite_StartedAt_On_Second_Call()
    {
        var batch = InvitationBatch.Create(Guid.NewGuid(), Guid.NewGuid(), Now, idempotencyKey: null);

        batch.MarkProcessing(Now.AddMinutes(1));
        batch.MarkProcessing(Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(1), batch.StartedAt);
        Assert.Equal(InvitationBatch.StatusProcessing, batch.Status);
    }

    [Fact]
    public void MarkCompleted_Sets_Status_And_CompletedAt()
    {
        var batch = InvitationBatch.Create(Guid.NewGuid(), Guid.NewGuid(), Now, idempotencyKey: null);
        batch.MarkProcessing(Now.AddMinutes(1));

        batch.MarkCompleted(Now.AddMinutes(2));

        Assert.Equal(InvitationBatch.StatusCompleted, batch.Status);
        Assert.Equal(Now.AddMinutes(2), batch.CompletedAt);
    }

    [Fact]
    public void ReopenForRetry_Resets_Status_To_Queued_And_Clears_CompletedAt()
    {
        var batch = InvitationBatch.Create(Guid.NewGuid(), Guid.NewGuid(), Now, idempotencyKey: null);
        batch.MarkProcessing(Now.AddMinutes(1));
        batch.MarkCompleted(Now.AddMinutes(2));

        batch.ReopenForRetry();

        Assert.Equal(InvitationBatch.StatusQueued, batch.Status);
        Assert.Null(batch.CompletedAt);
        // StartedAt from the original run is deliberately left as-is — only CompletedAt is cleared.
        Assert.Equal(Now.AddMinutes(1), batch.StartedAt);
    }
}
