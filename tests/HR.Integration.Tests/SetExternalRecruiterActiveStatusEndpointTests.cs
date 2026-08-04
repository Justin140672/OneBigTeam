using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SetExternalRecruiterActiveStatusEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cd00000d-0000-0000-0000-000000000001");

    public SetExternalRecruiterActiveStatusEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, RecruiterUser, SystemRoles.Recruiter, companyId);
        return client;
    }

    private async Task<Guid> SeedRecruiterAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/external-recruiters", new { companyId, agencyName = "Acme Recruiting" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecruiterPayload>())!.Id;
    }

    [Fact]
    public async Task Post_ActiveStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/external-recruiters/{Guid.NewGuid()}/active-status",
            new { isActive = false });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ActiveStatus_Deactivates_Recruiter()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var recruiterId = await SeedRecruiterAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters/{recruiterId}/active-status",
            new { companyId, externalRecruiterId = recruiterId, isActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RecruiterStatusPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.IsActive);
    }

    [Fact]
    public async Task Post_ActiveStatus_Returns_NotFound_When_Recruiter_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters/{Guid.NewGuid()}/active-status",
            new { companyId, externalRecruiterId = Guid.NewGuid(), isActive = false });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ActiveStatus_Returns_Forbidden_When_Tenant_Claim_Does_Not_Match_Route()
    {
        var companyId = Guid.NewGuid();
        var differentCompany = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var recruiterId = await SeedRecruiterAsync(client, companyId);

        using var mismatchedClient = await AuthenticatedClient(differentCompany);
        var response = await mismatchedClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters/{recruiterId}/active-status",
            new { companyId, externalRecruiterId = recruiterId, isActive = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record RecruiterPayload(Guid Id);
    private sealed record RecruiterStatusPayload(Guid Id, bool IsActive);
}
