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

    // ── UpdateCompanyDocumentCategory ─────────────────────────────────────────

    [Fact]
    public async Task Update_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/document-categories/{Guid.NewGuid()}", new { name = "Renamed" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        var hrUserId  = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrUserId, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, managerId, SystemRoles.Manager);

        using var hrClient = await ClientAs(companyId, hrUserId);
        var categoryId = await CreateCategoryAsync(hrClient, companyId, "Policy");

        using var managerClient = await ClientAs(companyId, managerId);
        var response = await managerClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-categories/{categoryId}", new { name = "Renamed" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_Succeeds_For_HrAdministrator_And_Renames_Category()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-categories/{categoryId}", new { name = "Company Policies" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        Assert.Equal("Company Policies", payload!.Name);
    }

    [Fact]
    public async Task Update_Returns_NotFound_For_Unknown_Category()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-categories/{Guid.NewGuid()}", new { name = "Renamed" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_Returns_NotFound_When_Category_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA    = Guid.NewGuid();
        var hrInB    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.PutAsJsonAsync(
            $"/api/companies/{companyB}/document-categories/{categoryInA}", new { name = "Renamed" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_Returns_Conflict_When_Renaming_To_An_Existing_Active_Name()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        await CreateCategoryAsync(client, companyId, "Handbook");
        var second = await CreateCategoryAsync(client, companyId, "Policy");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-categories/{second}", new { name = "Handbook" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_Returns_NotFound_For_A_Deactivated_Category()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");

        var deactivate = await client.DeleteAsync($"/api/companies/{companyId}/document-categories/{categoryId}");
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/document-categories/{categoryId}", new { name = "Renamed" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── DeactivateCompanyDocumentCategory ─────────────────────────────────────

    [Fact]
    public async Task Deactivate_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response = await client.DeleteAsync(
            $"/api/companies/{Guid.NewGuid()}/document-categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_Succeeds_For_HrAdministrator_And_Hides_Category_From_List()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");

        var response = await client.DeleteAsync($"/api/companies/{companyId}/document-categories/{categoryId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var list = await client.GetFromJsonAsync<ListPayload>($"/api/companies/{companyId}/document-categories");
        Assert.DoesNotContain(list!.Items, c => c.Id == categoryId);
    }

    [Fact]
    public async Task Deactivate_Returns_NotFound_For_Unknown_Category()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);

        var response = await client.DeleteAsync(
            $"/api/companies/{companyId}/document-categories/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_Is_Not_Idempotent_Second_Call_Returns_NotFound()
    {
        var companyId = Guid.NewGuid();
        var userId    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientAs(companyId, userId);
        var categoryId = await CreateCategoryAsync(client, companyId, "Policy");

        var first  = await client.DeleteAsync($"/api/companies/{companyId}/document-categories/{categoryId}");
        var second = await client.DeleteAsync($"/api/companies/{companyId}/document-categories/{categoryId}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Deactivate_Returns_NotFound_When_Category_Belongs_To_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var hrInA    = Guid.NewGuid();
        var hrInB    = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInA, SystemRoles.HrAdministrator);
        await TestRoleSeeder.AssignRoleAsync(_factory, hrInB, SystemRoles.HrAdministrator);

        using var clientA = await ClientAs(companyA, hrInA);
        var categoryInA = await CreateCategoryAsync(clientA, companyA, "Policy");

        using var clientB = await ClientAs(companyB, hrInB);
        var response = await clientB.DeleteAsync(
            $"/api/companies/{companyB}/document-categories/{categoryInA}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, Guid companyId, string name)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/document-categories", new { name });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CategoryPayload>();
        return payload!.Id;
    }

    private sealed record ListPayload(IReadOnlyList<CategoryPayload> Items);

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
