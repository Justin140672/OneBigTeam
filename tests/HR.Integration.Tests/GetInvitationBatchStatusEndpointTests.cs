using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetInvitationBatchStatusEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaad1-0000-0000-0000-000000000001");

    public GetInvitationBatchStatusEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId, Guid? userId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, (userId ?? AdminUser).ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid> SeedBatchAsync(Guid companyId, params string[] recipientStatuses)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;

        var batch = InvitationBatch.Create(companyId, Guid.NewGuid(), now, null);
        db.InvitationBatches.Add(batch);

        foreach (var status in recipientStatuses)
        {
            var recipient = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), $"{Guid.NewGuid():N}@test.com", now);
            switch (status)
            {
                case InvitationBatchRecipient.StatusSent:
                    recipient.MarkProcessing();
                    recipient.MarkSent(now);
                    break;
                case InvitationBatchRecipient.StatusFailed:
                    recipient.MarkProcessing();
                    recipient.MarkFailed("Email delivery failed");
                    break;
            }
            db.InvitationBatchRecipients.Add(recipient);
        }

        await db.SaveChangesAsync();
        return batch.Id;
    }

    [Fact]
    public async Task Get_InvitationBatchStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/companies/{companyId}/invitation-batches/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_InvitationBatchStatus_Returns_Correct_Counts_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var batchId = await SeedBatchAsync(
            companyId, InvitationBatchRecipient.StatusSent, InvitationBatchRecipient.StatusSent, InvitationBatchRecipient.StatusFailed);

        var response = await client.GetAsync($"/api/companies/{companyId}/invitation-batches/{batchId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.Equal(batchId, payload!.BatchId);
        Assert.Equal(2, payload.Counts.Sent);
        Assert.Equal(1, payload.Counts.Failed);
        Assert.Equal(3, payload.Recipients.Count);
    }

    [Fact]
    public async Task Get_InvitationBatchStatus_Returns_NotFound_For_Batch_Belonging_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var batchId = await SeedBatchAsync(otherCompanyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/invitation-batches/{batchId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record Payload(
        Guid BatchId, string Status, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
        CountsPayload Counts, IReadOnlyList<object> Recipients);

    private sealed record CountsPayload(int Waiting, int Processing, int Sent, int Skipped, int Failed);
}
