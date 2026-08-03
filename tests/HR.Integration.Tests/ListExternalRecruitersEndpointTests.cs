using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListExternalRecruitersEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cd00000b-0000-0000-0000-000000000001");

    public ListExternalRecruitersEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_ExternalRecruiters_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/external-recruiters");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ExternalRecruiters_Returns_Created_Recruiters()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        await client.PostAsJsonAsync($"/api/companies/{companyId}/external-recruiters", new { companyId, agencyName = "Acme Recruiting" });
        await client.PostAsJsonAsync($"/api/companies/{companyId}/external-recruiters", new { companyId, agencyName = "Beta Talent" });

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.TotalCount);
    }

    [Fact]
    public async Task Get_ExternalRecruiters_Filters_By_Search()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        await client.PostAsJsonAsync($"/api/companies/{companyId}/external-recruiters", new { companyId, agencyName = "Acme Recruiting" });
        await client.PostAsJsonAsync($"/api/companies/{companyId}/external-recruiters", new { companyId, agencyName = "Beta Talent" });

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters?search=acme");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        Assert.Equal("Acme Recruiting", payload.Items[0].AgencyName);
    }

    [Fact]
    public async Task Get_ExternalRecruiters_Returns_UnprocessableEntity_For_Invalid_PageSize()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters?pageSize=0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record ListItemPayload(Guid Id, string AgencyName);
    private sealed record ListPayload(IReadOnlyList<ListItemPayload> Items, int TotalCount);
}
