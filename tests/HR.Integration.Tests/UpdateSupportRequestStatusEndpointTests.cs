using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateSupportRequestStatusEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid EmployeeUserId = Guid.Parse("60000000-0000-0000-0000-000000000004");
    private static readonly Guid AdminUserId = Guid.Parse("60000000-0000-0000-0000-000000000005");

    public UpdateSupportRequestStatusEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> EmployeeClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, EmployeeUserId, SystemRoles.Employee, companyId);
        return client;
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        // Also needs role:employee — SubmitSupportRequest (used to seed a request in these tests) is
        // gated behind that policy, independent of the support:manage policy on the status endpoint.
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

    [Fact]
    public async Task Put_SupportRequestStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/support/requests/{Guid.NewGuid()}/status",
            new { status = "UnderReview" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_SupportRequestStatus_Returns_Forbidden_For_Non_Staff_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await EmployeeClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}/status",
            new { companyId, id = Guid.NewGuid(), status = "UnderReview" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_SupportRequestStatus_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}/status",
            new { companyId, id = Guid.NewGuid(), status = "UnderReview" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_SupportRequestStatus_Updates_Status_For_Staff_Admin()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var created = await client.PostAsync($"/api/companies/{companyId}/support/requests", BuildSubmission(companyId, "Status update issue"));
        created.EnsureSuccessStatusCode();
        var payload = await created.Content.ReadFromJsonAsync<SubmitPayload>();
        Assert.NotNull(payload);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{payload!.Id}/status",
            new { companyId, id = payload.Id, status = "UnderReview" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(updated);
        Assert.Equal("UnderReview", updated!.Status);
    }

    [Fact]
    public async Task Put_SupportRequestStatus_Returns_Conflict_When_Reopening_Closed_Request_Directly_To_Submitted()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);

        var created = await client.PostAsync($"/api/companies/{companyId}/support/requests", BuildSubmission(companyId, "Closed issue"));
        created.EnsureSuccessStatusCode();
        var payload = await created.Content.ReadFromJsonAsync<SubmitPayload>();
        Assert.NotNull(payload);

        // Walk the request to Closed via valid intermediate states.
        foreach (var status in new[] { "UnderReview", "Resolved", "Closed" })
        {
            var step = await client.PutAsJsonAsync(
                $"/api/companies/{companyId}/support/requests/{payload!.Id}/status",
                new { companyId, id = payload.Id, status });
            step.EnsureSuccessStatusCode();
        }

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{payload!.Id}/status",
            new { companyId, id = payload.Id, status = "Submitted" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record SubmitPayload(Guid Id, string ReferenceNumber);
    private sealed record StatusPayload(Guid Id, string Status, DateTimeOffset UpdatedAt);
}
