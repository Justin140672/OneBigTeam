using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// OBT-IAM-09: support:manage was removed from Company Administrator's seed grants — a
/// Company-Administrator-only account must now be denied every "support:manage"-gated endpoint,
/// not just the requests queue already covered by
/// <see cref="AdministrativeRoleSeparationTests"/>. This file covers the remaining
/// support:manage-gated endpoints (AddSupportResponse, UpdateSupportRequestStatus,
/// GetSupportDashboard) that were not previously exercised against the Company-Administrator-only
/// persona, and confirms a Company Administrator who also holds HR Administrator retains access
/// via HR Administrator's own grant.
/// </summary>
[Collection("Integration")]
public class SupportManagementRoleBoundaryTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid CompanyAdminOnly = new("0b719900-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdminPlusHrAdmin = new("0b719900-0000-0000-0000-000000000002");

    public SupportManagementRoleBoundaryTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId, bool alsoHrAdmin)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator, companyId);
        if (alsoHrAdmin)
            await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);

        return client;
    }

    private static void AssertForbidden(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Expected 401/403 but got {(int)response.StatusCode} {response.StatusCode}");

    private static void AssertReachedHandler(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized),
            $"Expected the request to reach the handler but got {(int)response.StatusCode} {response.StatusCode}");

    // ---------------------------------------------------------------------
    // GetSupportDashboard — GET /api/support/dashboard
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CompanyAdministratorOnly_CannotReach_SupportDashboard()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminOnly, companyId, alsoHrAdmin: false);

        var response = await client.GetAsync("/api/support/dashboard");

        AssertForbidden(response);
    }

    [Fact]
    public async Task CompanyAdministratorPlusHrAdministrator_CanReach_SupportDashboard()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminPlusHrAdmin, companyId, alsoHrAdmin: true);

        var response = await client.GetAsync("/api/support/dashboard");

        AssertReachedHandler(response);
    }

    // ---------------------------------------------------------------------
    // AddSupportResponse — POST /api/companies/{companyId}/support/requests/{id}/responses
    // ---------------------------------------------------------------------

    private static MultipartFormDataContent BuildResponseBody(Guid companyId, Guid id) => new()
    {
        { new StringContent(companyId.ToString()), "CompanyId" },
        { new StringContent(id.ToString()), "Id" },
        { new StringContent("Reply body"), "BodyHtml" },
    };

    [Fact]
    public async Task CompanyAdministratorOnly_CannotReach_AddSupportResponse()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminOnly, companyId, alsoHrAdmin: false);

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}/responses",
            BuildResponseBody(companyId, Guid.NewGuid()));

        AssertForbidden(response);
    }

    [Fact]
    public async Task CompanyAdministratorPlusHrAdministrator_CanReach_AddSupportResponse()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminPlusHrAdmin, companyId, alsoHrAdmin: true);

        // No request exists for this id — the authorization layer must still let the request
        // through to the handler, which then 404s. The load-bearing assertion is "not 401/403".
        var response = await client.PostAsync(
            $"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}/responses",
            BuildResponseBody(companyId, Guid.NewGuid()));

        AssertReachedHandler(response);
    }

    // ---------------------------------------------------------------------
    // UpdateSupportRequestStatus — PUT /api/companies/{companyId}/support/requests/{id}/status
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CompanyAdministratorOnly_CannotReach_UpdateSupportRequestStatus()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminOnly, companyId, alsoHrAdmin: false);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}/status",
            new { CompanyId = companyId, Id = Guid.NewGuid(), Status = "Resolved" });

        AssertForbidden(response);
    }

    [Fact]
    public async Task CompanyAdministratorPlusHrAdministrator_CanReach_UpdateSupportRequestStatus()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminPlusHrAdmin, companyId, alsoHrAdmin: true);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/support/requests/{Guid.NewGuid()}/status",
            new { CompanyId = companyId, Id = Guid.NewGuid(), Status = "Resolved" });

        AssertReachedHandler(response);
    }
}
