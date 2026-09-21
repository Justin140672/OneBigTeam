using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.RetryInvitationBatch;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class RetryInvitationBatchHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private RetryInvitationBatchHandler BuildHandler(RecordingBackgroundJobClient? jobClient = null) =>
        new(fixture.BuildContext(), jobClient ?? new RecordingBackgroundJobClient());

    [Fact]
    public async Task HandleAsync_Resets_Only_Failed_Recipients_Back_To_Waiting()
    {
        var companyId = Guid.NewGuid();
        Guid batchId, sentId, skippedId, failedId;

        await using (var db = fixture.BuildContext())
        {
            var batch = InvitationBatch.Create(companyId, Guid.NewGuid(), Now, null);
            batch.MarkProcessing(Now);
            batch.MarkCompleted(Now.AddMinutes(1));
            batchId = batch.Id;
            db.InvitationBatches.Add(batch);

            var sent = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), "sent@test.com", Now);
            sent.MarkProcessing();
            sent.MarkSent(Now.AddMinutes(1));
            sentId = sent.Id;

            var skipped = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), "skipped@test.com", Now);
            skipped.MarkProcessing();
            skipped.MarkSkipped("AlreadyHasAccount", Now.AddMinutes(1));
            skippedId = skipped.Id;

            var failed = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), "failed@test.com", Now);
            failed.MarkProcessing();
            failed.MarkFailed("Email delivery failed");
            failedId = failed.Id;

            db.InvitationBatchRecipients.AddRange(sent, skipped, failed);
            await db.SaveChangesAsync();
        }

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(jobClient);

        var result = await handler.HandleAsync(new RetryInvitationBatchRequest(companyId, batchId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.RetryCount);

        await using var verifyDb = fixture.BuildContext();
        var reloadedSent = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == sentId);
        var reloadedSkipped = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == skippedId);
        var reloadedFailed = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == failedId);

        Assert.Equal(InvitationBatchRecipient.StatusSent, reloadedSent.Status);
        Assert.Equal(InvitationBatchRecipient.StatusSkipped, reloadedSkipped.Status);
        Assert.Equal(InvitationBatchRecipient.StatusWaiting, reloadedFailed.Status);
        Assert.Null(reloadedFailed.FailureReason);

        var reloadedBatch = await verifyDb.InvitationBatches.SingleAsync(b => b.Id == batchId);
        Assert.Equal(InvitationBatch.StatusQueued, reloadedBatch.Status);

        Assert.Single(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_When_Nothing_To_Retry()
    {
        var companyId = Guid.NewGuid();
        Guid batchId;

        await using (var db = fixture.BuildContext())
        {
            var batch = InvitationBatch.Create(companyId, Guid.NewGuid(), Now, null);
            batchId = batch.Id;
            db.InvitationBatches.Add(batch);

            var sent = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), "sent2@test.com", Now);
            sent.MarkProcessing();
            sent.MarkSent(Now.AddMinutes(1));
            db.InvitationBatchRecipients.Add(sent);
            await db.SaveChangesAsync();
        }

        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(jobClient);

        var result = await handler.HandleAsync(new RetryInvitationBatchRequest(companyId, batchId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Batch_Belonging_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        await using var db = fixture.BuildContext();
        var batch = InvitationBatch.Create(companyId, Guid.NewGuid(), Now, null);
        db.InvitationBatches.Add(batch);
        await db.SaveChangesAsync();

        var handler = BuildHandler();
        var result = await handler.HandleAsync(new RetryInvitationBatchRequest(otherCompanyId, batch.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
