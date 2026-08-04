using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateExternalRecruiterEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cd00000c-0000-0000-0000-000000000001");

    public UpdateExternalRecruiterEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<Guid> SeedRecruiterAsync(HttpClient client, Guid companyId, string agencyName = "Acme Recruiting")
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/external-recruiters", new { companyId, agencyName });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecruiterPayload>())!.Id;
    }

    [Fact]
    public async Task Put_ExternalRecruiter_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/external-recruiters/{Guid.NewGuid()}",
            new { agencyName = "Updated Name" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_ExternalRecruiter_Updates_Recruiter()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var recruiterId = await SeedRecruiterAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters/{recruiterId}",
            new { companyId, externalRecruiterId = recruiterId, agencyName = "Updated Name" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RecruiterPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Updated Name", payload!.AgencyName);
    }

    [Fact]
    public async Task Put_ExternalRecruiter_Returns_NotFound_When_Recruiter_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters/{Guid.NewGuid()}",
            new { companyId, externalRecruiterId = Guid.NewGuid(), agencyName = "Updated Name" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ExternalRecruiter_Returns_UnprocessableEntity_When_AgencyName_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var recruiterId = await SeedRecruiterAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters/{recruiterId}",
            new { companyId, externalRecruiterId = recruiterId, agencyName = string.Empty });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_ExternalRecruiter_Returns_Forbidden_When_Tenant_Claim_Does_Not_Match_Route()
    {
        var companyId = Guid.NewGuid();
        var differentCompany = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var recruiterId = await SeedRecruiterAsync(client, companyId);

        using var mismatchedClient = await AuthenticatedClient(differentCompany);
        var response = await mismatchedClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters/{recruiterId}",
            new { companyId, externalRecruiterId = recruiterId, agencyName = "Updated Name" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record RecruiterPayload(Guid Id, string AgencyName);
}
