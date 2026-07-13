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
public class CompanyDocumentCategoryEndpointTests : IClassFixture<ApiWebApplicationFactory>
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
        using var client = ClientAs(companyId, userId);

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
        using var client = ClientAs(companyId, userId);

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
        using var client = ClientAs(companyId, userId);

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
        using var client = ClientAs(companyId, userId);

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
        using var client = ClientAs(companyId, userId);

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

        using var hrClient = ClientAs(companyId, hrUserId);
        var createResp = await hrClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-categories", new { name = "Procedure" });
        var created = await createResp.Content.ReadFromJsonAsync<CategoryPayload>();

        using var managerClient = ClientAs(companyId, managerId);
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
        using var hrClient = ClientAs(companyId, hrUserId);
        await hrClient.PostAsJsonAsync($"/api/companies/{companyId}/document-categories", new { name = "Guidance" });

        foreach (var roleId in new[] { SystemRoles.Employee, SystemRoles.Manager, SystemRoles.Recruiter, SystemRoles.HrAdministrator })
        {
            var userId = Guid.NewGuid();
            await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId);
            using var client = ClientAs(companyId, userId);

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
        using var client = ClientAs(companyId, userId);

        var response = await client.GetAsync($"/api/companies/{companyId}/document-categories");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient ClientAs(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private sealed record CategoryPayload(Guid Id, Guid CompanyId, string Name, bool IsActive);
}
