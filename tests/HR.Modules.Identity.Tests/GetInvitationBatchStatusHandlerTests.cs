using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.GetInvitationBatchStatus;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class GetInvitationBatchStatusHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private GetInvitationBatchStatusHandler BuildHandler() => new(fixture.BuildContext());

    private async Task<InvitationBatch> SeedBatchAsync(
        Guid companyId,
        Action<InvitationBatch>? configure = null,
        params (string Status, string? FailureReason)[] recipients)
    {
        await using var db = fixture.BuildContext();
        var batch = InvitationBatch.Create(companyId, Guid.NewGuid(), Now, null);
        configure?.Invoke(batch);
        db.InvitationBatches.Add(batch);

        foreach (var (status, reason) in recipients)
        {
            var recipient = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), $"{Guid.NewGuid():N}@test.com", Now);
            switch (status)
            {
                case InvitationBatchRecipient.StatusProcessing:
                    recipient.MarkProcessing();
                    break;
                case InvitationBatchRecipient.StatusSent:
                    recipient.MarkSent(Now.AddMinutes(1));
                    break;
                case InvitationBatchRecipient.StatusSkipped:
                    recipient.MarkSkipped(reason ?? "NotEligible", Now.AddMinutes(1));
                    break;
                case InvitationBatchRecipient.StatusFailed:
                    recipient.MarkFailed(reason ?? "Email delivery failed");
                    break;
            }
            db.InvitationBatchRecipients.Add(recipient);
        }

        await db.SaveChangesAsync();
        return batch;
    }

    [Fact]
    public async Task HandleAsync_Returns_Correct_Counts_And_Recipient_Results()
    {
        var companyId = Guid.NewGuid();
        var batch = await SeedBatchAsync(
            companyId,
            null,
            (InvitationBatchRecipient.StatusWaiting, null),
            (InvitationBatchRecipient.StatusSent, null),
            (InvitationBatchRecipient.StatusSkipped, "AlreadyHasAccount"),
            (InvitationBatchRecipient.StatusFailed, "Email delivery failed"));

        var handler = BuildHandler();
        var result = await handler.HandleAsync(
            new GetInvitationBatchStatusRequest(companyId, batch.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Counts.Waiting);
        Assert.Equal(1, result.Value.Counts.Sent);
        Assert.Equal(1, result.Value.Counts.Skipped);
        Assert.Equal(1, result.Value.Counts.Failed);
        Assert.Equal(0, result.Value.Counts.Processing);
        Assert.Equal(4, result.Value.Recipients.Count);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Batch_Belonging_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var batch = await SeedBatchAsync(companyId);

        var handler = BuildHandler();
        var result = await handler.HandleAsync(
            new GetInvitationBatchStatusRequest(otherCompanyId, batch.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_BatchId()
    {
        var handler = BuildHandler();
        var result = await handler.HandleAsync(
            new GetInvitationBatchStatusRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Response_Never_Exposes_An_InviteId_Or_Token()
    {
        var companyId = Guid.NewGuid();
        var batch = await SeedBatchAsync(companyId, null, (InvitationBatchRecipient.StatusSent, null));

        var handler = BuildHandler();
        var result = await handler.HandleAsync(
            new GetInvitationBatchStatusRequest(companyId, batch.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var responseType = typeof(InvitationBatchStatusResponse);
        var recipientResultType = typeof(InvitationBatchRecipientResult);

        Assert.DoesNotContain(responseType.GetProperties(), p => p.Name.Contains("Invite") || p.Name.Contains("Token"));
        Assert.DoesNotContain(recipientResultType.GetProperties(), p => p.Name.Contains("Invite") || p.Name.Contains("Token"));
    }
}
