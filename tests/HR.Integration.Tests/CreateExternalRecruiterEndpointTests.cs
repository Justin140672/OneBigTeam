using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateExternalRecruiterEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cd00000a-0000-0000-0000-000000000001");

    public CreateExternalRecruiterEndpointTests(ApiWebApplicationFactory factory)
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

    [Fact]
    public async Task Post_ExternalRecruiters_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/external-recruiters", new
        {
            agencyName = "Acme Recruiting"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExternalRecruiters_Creates_Recruiter()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/external-recruiters", new
        {
            companyId,
            agencyName = "Acme Recruiting",
            contactName = "Jane Smith",
            contactEmail = "jane@acme.com"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<RecruiterPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal("Acme Recruiting", payload.AgencyName);
        Assert.True(payload.IsActive);
    }

    [Fact]
    public async Task Post_ExternalRecruiters_Returns_UnprocessableEntity_When_AgencyName_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/external-recruiters", new
        {
            companyId,
            agencyName = string.Empty
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExternalRecruiters_Returns_UnprocessableEntity_When_ContactEmail_Is_Invalid()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/external-recruiters", new
        {
            companyId,
            agencyName = "Acme Recruiting",
            contactEmail = "not-an-email"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExternalRecruiters_Returns_Forbidden_When_Tenant_Claim_Does_Not_Match_Route()
    {
        var companyId = Guid.NewGuid();
        var differentCompany = Guid.NewGuid();
        using var mismatchedClient = await AuthenticatedClient(differentCompany);

        var response = await mismatchedClient.PostAsJsonAsync($"/api/companies/{companyId}/external-recruiters", new
        {
            companyId,
            agencyName = "Acme Recruiting"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record RecruiterPayload(Guid Id, Guid CompanyId, string AgencyName, bool IsActive);
}
