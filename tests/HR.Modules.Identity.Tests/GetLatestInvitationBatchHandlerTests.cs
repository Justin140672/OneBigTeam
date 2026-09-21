using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.GetLatestInvitationBatch;
using HR.Modules.Identity.Tests.Infrastructure;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class GetLatestInvitationBatchHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private GetLatestInvitationBatchHandler BuildHandler() => new(fixture.BuildContext());

    private async Task<InvitationBatch> SeedBatchAsync(Guid companyId, DateTimeOffset createdAt)
    {
        await using var db = fixture.BuildContext();
        var batch = InvitationBatch.Create(companyId, Guid.NewGuid(), createdAt, null);
        db.InvitationBatches.Add(batch);
        await db.SaveChangesAsync();
        return batch;
    }

    [Fact]
    public async Task HandleAsync_Returns_The_Most_Recently_Created_Batch_For_The_Company()
    {
        var companyId = Guid.NewGuid();
        await SeedBatchAsync(companyId, Now.AddMinutes(-10));
        var latest = await SeedBatchAsync(companyId, Now);

        var handler = BuildHandler();
        var result = await handler.HandleAsync(new GetLatestInvitationBatchRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(latest.Id, result.Value.BatchId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Batch_Exists_For_The_Company()
    {
        var handler = BuildHandler();
        var result = await handler.HandleAsync(new GetLatestInvitationBatchRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Never_Returns_A_Batch_Belonging_To_A_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await SeedBatchAsync(otherCompanyId, Now);

        var handler = BuildHandler();
        var result = await handler.HandleAsync(new GetLatestInvitationBatchRequest(companyId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
