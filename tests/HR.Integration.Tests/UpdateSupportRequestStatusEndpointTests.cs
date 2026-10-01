using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateSupportRequestStatusEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public UpdateSupportRequestStatusEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> TenantClient(Guid companyId, params Guid[] roles)
    {
        var userId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        foreach (var role in roles)
            await TestRoleSeeder.AssignRoleAsync(_factory, userId, role, companyId);
        return client;
    }

    private async Task<HttpClient> PlatformAdminClient()
    {
        var (_, email) = await PlatformAdministratorTestHelpers.SeedAdministratorAsync(
            _factory, PlatformAdministratorRole.SupportStaff);
        return PlatformAdministratorTestHelpers.ClientFor(_factory, Guid.NewGuid(), email);
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

    private async Task<Guid> SubmitAsHrAsync(Guid companyId, string title)
    {
        using var hr = await TenantClient(companyId, SystemRoles.Employee, SystemRoles.HrAdministrator);
        var created = await hr.PostAsync($"/api/companies/{companyId}/support/requests", BuildSubmission(companyId, title));
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<SubmitPayload>())!.Id;
    }

    [Fact]
    public async Task Put_AdminSupportRequestStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/admin/companies/{Guid.NewGuid()}/support/requests/{Guid.NewGuid()}/status",
            new { status = "UnderReview" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_Status_Route_Is_Removed_For_Every_Tenant_Role_Including_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var id = await SubmitAsHrAsync(companyId, "Tenant status route removed");

        var roleSets = new[]
        {
            new[] { SystemRoles.Employee },
            new[] { SystemRoles.Employee, SystemRoles.Manager },
            new[] { SystemRoles.Employee, SystemRoles.Recruiter },
            new[] { SystemRoles.Employee, SystemRoles.CompanyAdministrator },
            new[] { SystemRoles.Employee, SystemRoles.HrAdministrator },
        };

        foreach (var roles in roleSets)
        {
            using var client = await TenantClient(companyId, roles);
            var response = await client.PutAsJsonAsync(
                $"/api/companies/{companyId}/support/requests/{id}/status",
                new { companyId, id, status = "UnderReview", expectedVersion = 1 });

            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"Expected 404/405 for roles [{string.Join(",", roles)}] but got {(int)response.StatusCode}.");
        }
    }

    [Fact]
    public async Task Put_AdminSupportRequestStatus_Returns_Forbidden_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var id = await SubmitAsHrAsync(companyId, "HR cannot change status");
        using var hr = await TenantClient(companyId, SystemRoles.Employee, SystemRoles.HrAdministrator);

        var response = await hr.PutAsJsonAsync(
            $"/api/admin/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "UnderReview", expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_AdminSupportRequestStatus_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var admin = await PlatformAdminClient();

        var response = await admin.PutAsJsonAsync(
            $"/api/admin/companies/{companyId}/support/requests/{Guid.NewGuid()}/status",
            new { companyId, id = Guid.NewGuid(), status = "UnderReview", expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_AdminSupportRequestStatus_Updates_Status_For_Platform_Administrator()
    {
        var companyId = Guid.NewGuid();
        var id = await SubmitAsHrAsync(companyId, "Status update issue");
        using var admin = await PlatformAdminClient();

        var response = await admin.PutAsJsonAsync(
            $"/api/admin/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "UnderReview", expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<StatusPayload>();
        Assert.NotNull(updated);
        Assert.Equal("UnderReview", updated!.Status);
    }

    [Fact]
    public async Task Put_AdminSupportRequestStatus_Returns_Conflict_When_Reopening_Closed_Request_Directly_To_Submitted()
    {
        var companyId = Guid.NewGuid();
        var id = await SubmitAsHrAsync(companyId, "Closed issue");
        using var admin = await PlatformAdminClient();

        var version = 1;
        foreach (var status in new[] { "UnderReview", "Resolved", "Closed" })
        {
            var step = await admin.PutAsJsonAsync(
                $"/api/admin/companies/{companyId}/support/requests/{id}/status",
                new { companyId, id, status, expectedVersion = version });
            step.EnsureSuccessStatusCode();
            version = (await step.Content.ReadFromJsonAsync<StatusPayload>())!.Version;
        }

        var response = await admin.PutAsJsonAsync(
            $"/api/admin/companies/{companyId}/support/requests/{id}/status",
            new { companyId, id, status = "Submitted", expectedVersion = version });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed record SubmitPayload(Guid Id, string ReferenceNumber);
    private sealed record StatusPayload(Guid Id, string Status, DateTimeOffset UpdatedAt, int Version);
}
