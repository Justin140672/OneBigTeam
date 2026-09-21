using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetLatestInvitationBatchEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaae1-0000-0000-0000-000000000001");

    public GetLatestInvitationBatchEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedBatchAsync(Guid companyId, DateTimeOffset createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var batch = InvitationBatch.Create(companyId, Guid.NewGuid(), createdAt, null);
        db.InvitationBatches.Add(batch);
        await db.SaveChangesAsync();
        return batch.Id;
    }

    [Fact]
    public async Task Get_LatestInvitationBatch_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/companies/{companyId}/invitation-batches/latest");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_LatestInvitationBatch_Returns_The_Most_Recent_Batch_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var now = DateTimeOffset.UtcNow;
        await SeedBatchAsync(companyId, now.AddMinutes(-10));
        var latestId = await SeedBatchAsync(companyId, now);

        var response = await client.GetAsync($"/api/companies/{companyId}/invitation-batches/latest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.Equal(latestId, payload!.BatchId);
    }

    [Fact]
    public async Task Get_LatestInvitationBatch_Returns_NotFound_When_No_Batch_Exists_For_The_Company()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/invitation-batches/latest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record Payload(Guid BatchId);
}
