using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Verifies the shared-document:* policies (see IdentityModule.AddRolePolicies) as applied to
/// the CompanyDocumentCategory endpoints — in particular the "Expected access" rules from the
/// permissions spec: HR can manage, Managers do not automatically get manage rights, and a
/// Company Administrator only gets access if they ALSO hold the HrAdministrator role.
/// </summary>
[Collection("Integration")]
public class CompanyDocumentCategoryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public CompanyDocumentCategoryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/document-categories", new { name = "Policy" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_Returns_Forbidden_For_Manager_Alone()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-categories", new { name = "Policy" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_Returns_Forbidden_For_CompanyAdministrator_Without_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-categories", new { name = "Policy" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_Succeeds_For_CompanyAdministrator_Who_Also_Holds_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-categories", new { name = "Policy" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_Succeeds_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-categories", new { name = "Handbook" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        Assert.Equal("Handbook", created!.Name);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task Create_Returns_Conflict_For_Duplicate_Active_Name_In_Same_Company()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        await client.PostAsJsonAsync($"/api/companies/{companyId}/document-categories", new { name = "Policy" });
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/document-categories", new { name = "Policy" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        var managerId  = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var createResp = await hrClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-categories", new { name = "Procedure" });
        var created = await createResp.Content.ReadFromJsonAsync<CategoryPayload>();

        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.DeleteAsync(
            $"/api/companies/{companyId}/document-categories/{created!.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_Is_Visible_To_Employee_Manager_Recruiter_And_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var hrUserId   = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        using var hrClient = await ClientAs(companyId, hrUserId);
        await hrClient.PostAsJsonAsync($"/api/companies/{companyId}/document-categories", new { name = "Guidance" });

        foreach (var roleId in new[] { SystemRoles.Employee, SystemRoles.Manager, SystemRoles.Recruiter, SystemRoles.HrAdministrator })
        {
            var userId = Guid.NewGuid();
            await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId);
            using var client = await ClientAs(companyId, userId);

            var response = await client.GetAsync($"/api/companies/{companyId}/document-categories");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task List_Returns_Forbidden_For_CompanyAdministrator_Without_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/document-categories");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> ClientAs(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        // Role-agnostic sync only — every caller of this helper already granted the specific
        // role(s) it wants to test beforehand via AssignRoleAsync. Hardcoding a role here (this
        // used to always grant SystemRoles.Manager) additionally granted it to every caller
        // regardless of intent, which used to be harmless only because tenant resolution didn't
        // actually key off UserProfile.CompanyId yet — now that it does, an unconditional extra
        // role grant here changes real authorization outcomes.
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private sealed record CategoryPayload(Guid Id, Guid CompanyId, string Name, bool IsActive);
}
