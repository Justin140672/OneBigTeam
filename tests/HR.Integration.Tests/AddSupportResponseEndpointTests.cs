using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class AddSupportResponseEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = Guid.Parse("60000000-0000-0000-0000-000000000007");

    public AddSupportResponseEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        // Submit/AddResponse are both gated behind "support:manage" — the whole module is
        // staff-only, so IsStaffResponse (derived from the same policy) is always true here.
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
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

    private static MultipartFormDataContent BuildResponse(Guid companyId, Guid id, string bodyHtml) => new()
    {
        { new StringContent(companyId.ToString()), "CompanyId" },
        { new StringContent(id.ToString()), "Id" },
        { new StringContent(bodyHtml), "BodyHtml" },
    };

    [Fact]
    public async Task Post_SupportResponse_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync(
            $"/api/companies/{Guid.NewGuid()}/support/requests/{Guid.NewGuid()}/responses",
            BuildResponse(Guid.NewGuid(), Guid.NewGuid(), "Reply"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_SupportResponse_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}/responses",
            BuildResponse(companyId, Guid.NewGuid(), "Reply"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_SupportResponse_From_Staff_Is_Flagged_As_Staff()
    {
        var companyId = Guid.NewGuid();
        using var adminClient = await AdminClient(companyId);

        var created = await adminClient.PostAsync($"/api/companies/{companyId}/support/requests", BuildSubmission(companyId, "Staff response issue"));
        created.EnsureSuccessStatusCode();
        var payload = await created.Content.ReadFromJsonAsync<SubmitPayload>();
        Assert.NotNull(payload);

        var response = await adminClient.PostAsync(
            $"/api/companies/{companyId}/support/requests/{payload!.Id}/responses",
            BuildResponse(companyId, payload.Id, "We're looking into it."));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var responsePayload = await response.Content.ReadFromJsonAsync<ResponsePayload>();
        Assert.NotNull(responsePayload);
        Assert.True(responsePayload!.IsStaffResponse);
    }

    private sealed record SubmitPayload(Guid Id, string ReferenceNumber);
    private sealed record ResponsePayload(Guid Id, bool IsStaffResponse, DateTimeOffset CreatedAt);
}
