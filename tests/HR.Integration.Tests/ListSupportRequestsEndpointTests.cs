using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListSupportRequestsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid EmployeeUserId = Guid.Parse("60000000-0000-0000-0000-000000000002");

    public ListSupportRequestsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> EmployeeClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        // Submit/List are both gated behind "support:manage", not just role:employee.
        await TestRoleSeeder.AssignRoleAsync(_factory, EmployeeUserId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, EmployeeUserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static MultipartFormDataContent BuildSubmission(Guid companyId, string title) => new()
    {
        { new StringContent(companyId.ToString()), "CompanyId" },
        { new StringContent("AskQuestion"), "Type" },
        { new StringContent(title), "Title" },
        { new StringContent("Some description of the issue."), "Description" },
        { new StringContent("Low"), "Priority" },
        { new StringContent("false"), "IncludeDiagnostics" },
    };

    [Fact]
    public async Task Get_SupportRequests_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/support/requests");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SupportRequests_Returns_Only_Requests_For_The_Callers_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId);

        var created = await client.PostAsync($"/api/companies/{companyId}/support/requests", BuildSubmission(companyId, "My company issue"));
        created.EnsureSuccessStatusCode();

        using var otherClient = await EmployeeClient(otherCompanyId);
        var createdOther = await otherClient.PostAsync($"/api/companies/{otherCompanyId}/support/requests", BuildSubmission(otherCompanyId, "Other company issue"));
        createdOther.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/support/requests");
        response.EnsureSuccessStatusCode();
        var list = await response.Content.ReadFromJsonAsync<List<ListItemPayload>>();

        Assert.NotNull(list);
        Assert.Contains(list!, r => r.Title == "My company issue");
        Assert.DoesNotContain(list!, r => r.Title == "Other company issue");
    }

    [Fact]
    public async Task Get_SupportRequests_Filters_By_Status()
    {
        var companyId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId);

        var created = await client.PostAsync($"/api/companies/{companyId}/support/requests", BuildSubmission(companyId, "Filterable issue"));
        created.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/api/companies/{companyId}/support/requests?Status=Resolved");
        response.EnsureSuccessStatusCode();
        var list = await response.Content.ReadFromJsonAsync<List<ListItemPayload>>();

        Assert.NotNull(list);
        Assert.DoesNotContain(list!, r => r.Title == "Filterable issue");
    }

    private sealed record ListItemPayload(
        Guid Id, string ReferenceNumber, string Type, string Title, string Priority, string Status,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? LatestResponseSnippet);
}
