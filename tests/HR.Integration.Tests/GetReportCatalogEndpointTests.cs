using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetReportCatalogEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetReportCatalogEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        return client;
    }

    [Fact]
    public async Task Get_Catalog_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/catalog");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Catalog_Returns_Forbidden_For_Persona_With_No_Reporting_Policy()
    {
        // Plain Employee — not Manager/Recruiter/HrAdministrator — fails the baseline
        // "reporting:view" policy gate outright.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Catalog_Returns_Only_LeaveSummary_For_Manager_With_No_Other_Category_Access()
    {
        // Manager has baseline "reporting:view" access and "reporting:view-leave-summary" +
        // "reporting:view-probation" (both Manager OR HrAdministrator per the Reporting
        // Dashboard epic phase 2/3), but neither "reporting:view-recruitment",
        // "reporting:view-hr" nor "reporting:view-employee-starter" — proves per-category
        // filtering, not just a baseline 403/200 split.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        // reporting:view-leave-summary, reporting:view-probation and reporting:view-onboarding are
        // all Manager-OR-HrAdministrator policies, plus workload-actions which is always visible to
        // any baseline reporting:view caller (see OBT-721 tests below).
        Assert.Equal(4, payload!.Items.Count);
        Assert.Contains(payload.Items, i => i.Id == "leave-summary");
        Assert.Contains(payload.Items, i => i.Id == "probation-report");
        Assert.Contains(payload.Items, i => i.Id == "onboarding-progress");
        Assert.Contains(payload.Items, i => i.Id == "workload-actions");
    }

    // OBT-721: workload-actions is relevant to all three baseline reporting:view roles at once
    // (Manager, Recruiter, HrAdministrator) — see the RequiresWorkloadActionsAccess xmldoc in
    // GetReportCatalog/Handler.cs. Every caller who reaches this endpoint at all (i.e. passes the
    // baseline "reporting:view" policy) should see this entry.
    [Fact]
    public async Task Get_Catalog_Includes_WorkloadActions_For_Manager()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.Id == "workload-actions" && i.Category == "Hr");
    }

    [Fact]
    public async Task Get_Catalog_Includes_WorkloadActions_For_Recruiter()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.Id == "workload-actions");
    }

    [Fact]
    public async Task Get_Catalog_Includes_WorkloadActions_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, i => i.Id == "workload-actions");
    }

    [Fact]
    public async Task Get_Catalog_Returns_Recruitment_And_EmployeeStarter_For_Recruiter()
    {
        // Recruiter has "reporting:view" + "reporting:view-recruitment" +
        // "reporting:view-employee-starter" (HrAdministrator OR Recruiter) but not
        // "reporting:view-hr" or "reporting:view-leave-summary".
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        Assert.Equal(5, payload!.Items.Count);
        Assert.Contains(payload.Items, i => i.Id == "recruitment-pipeline-summary" && i.Category == "Recruitment");
        Assert.Contains(payload.Items, i => i.Id == "recruitment-pipeline-report" && i.Category == "Recruitment");
        Assert.Contains(payload.Items, i => i.Id == "vacancy-performance-report" && i.Category == "Recruitment");
        Assert.Contains(payload.Items, i => i.Id == "employee-starters" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "workload-actions");
    }

    [Fact]
    public async Task Get_Catalog_Returns_All_Hr_Related_Categories_For_HrAdministrator()
    {
        // HrAdministrator (without Recruiter) has "reporting:view" + "reporting:view-hr" +
        // "reporting:view-employee-starter" + "reporting:view-leave-summary" +
        // "reporting:view-probation" (all include HrAdministrator) but not
        // "reporting:view-recruitment" — so every entry except the Recruitment-only ones is
        // visible.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        // Full Hr-category catalog (phases 1-4) plus workload-actions: 14 entries total once the
        // 3 Recruitment-only entries are excluded from the full 17-entry catalog.
        Assert.Equal(14, payload!.Items.Count);
        Assert.Contains(payload.Items, i => i.Id == "hr-headcount-summary" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "employee-directory" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "employee-starters" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "employee-leavers" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "leave-summary" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "leave-calendar" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "sickness-report" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "probation-report" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "onboarding-progress" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "offboarding-progress" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "document-compliance" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "document-acknowledgement" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "asset-assignment" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "workload-actions" && i.Category == "Hr");
        Assert.DoesNotContain(payload.Items, i => i.Id == "recruitment-pipeline-summary");
        Assert.DoesNotContain(payload.Items, i => i.Id == "recruitment-pipeline-report");
        Assert.DoesNotContain(payload.Items, i => i.Id == "vacancy-performance-report");
    }

    [Fact]
    public async Task Get_Catalog_Returns_All_Categories_For_User_With_Recruiter_And_HrAdministrator_Roles()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        // Recruiter + HrAdministrator together satisfy every category flag: the full 17-entry
        // catalog (all Recruitment + all Hr, including phase-4 and OBT-721 workload-actions).
        Assert.Equal(17, payload!.Items.Count);
        Assert.Contains(payload.Items, i => i.Id == "workload-actions");
        Assert.Contains(payload.Items, i => i.Id == "recruitment-pipeline-summary");
        Assert.Contains(payload.Items, i => i.Id == "hr-headcount-summary");
        Assert.Contains(payload.Items, i => i.Id == "employee-directory");
        Assert.Contains(payload.Items, i => i.Id == "employee-starters");
        Assert.Contains(payload.Items, i => i.Id == "employee-leavers");
        Assert.Contains(payload.Items, i => i.Id == "leave-summary");
        Assert.Contains(payload.Items, i => i.Id == "leave-calendar");
        Assert.Contains(payload.Items, i => i.Id == "sickness-report");
        Assert.Contains(payload.Items, i => i.Id == "recruitment-pipeline-report");
        Assert.Contains(payload.Items, i => i.Id == "vacancy-performance-report");
        Assert.Contains(payload.Items, i => i.Id == "onboarding-progress");
        Assert.Contains(payload.Items, i => i.Id == "offboarding-progress");
        Assert.Contains(payload.Items, i => i.Id == "document-compliance");
        Assert.Contains(payload.Items, i => i.Id == "document-acknowledgement");
        Assert.Contains(payload.Items, i => i.Id == "asset-assignment");
        // reporting:view-probation is Manager OR HrAdministrator — HrAdministrator is present
        // on this persona, so probation-report is visible too (full catalog, 17 items).
        Assert.Contains(payload.Items, i => i.Id == "probation-report");
    }

    private sealed record CatalogPayload(List<CatalogItemPayload> Items);

    private sealed record CatalogItemPayload(string Id, string DisplayName, string Category, string Description);
}
