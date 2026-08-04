using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetSupportRequestEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid EmployeeUserId = Guid.Parse("60000000-0000-0000-0000-000000000003");

    public GetSupportRequestEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> EmployeeClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        // Submit/Get are both gated behind "support:manage", not just role:employee.
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
    public async Task Get_SupportRequest_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/support/requests/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_SupportRequest_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_SupportRequest_Returns_Details_For_Existing_Request()
    {
        var companyId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId);

        var created = await client.PostAsync($"/api/companies/{companyId}/support/requests", BuildSubmission(companyId, "Detail lookup issue"));
        created.EnsureSuccessStatusCode();
        var createdPayload = await created.Content.ReadFromJsonAsync<SubmitPayload>();
        Assert.NotNull(createdPayload);

        var response = await client.GetAsync($"/api/companies/{companyId}/support/requests/{createdPayload!.Id}");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GetSupportRequestPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Detail lookup issue", payload!.Title);
        Assert.Equal(createdPayload.ReferenceNumber, payload.ReferenceNumber);
        Assert.Empty(payload.Responses);
    }

    [Fact]
    public async Task Get_SupportRequest_Returns_NotFound_When_Request_Belongs_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId);

        var created = await client.PostAsync($"/api/companies/{companyId}/support/requests", BuildSubmission(companyId, "Cross-tenant issue"));
        created.EnsureSuccessStatusCode();
        var createdPayload = await created.Content.ReadFromJsonAsync<SubmitPayload>();
        Assert.NotNull(createdPayload);

        using var otherClient = await EmployeeClient(otherCompanyId);
        var response = await otherClient.GetAsync($"/api/companies/{otherCompanyId}/support/requests/{createdPayload!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record SubmitPayload(Guid Id, string ReferenceNumber);

    private sealed record GetSupportRequestPayload(
        Guid Id, string ReferenceNumber, string Type, string Title, string Description,
        string Priority, string Status, List<object> Attachments, List<object> Responses);
}
