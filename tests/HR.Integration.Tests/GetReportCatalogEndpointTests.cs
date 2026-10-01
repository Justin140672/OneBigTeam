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
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // REP-04: a Company Administrator has no Manager/Recruiter/HrAdministrator role, so — like the
    // plain Employee case above — they never even reach the per-category workload-actions gate;
    // they're rejected at the baseline "reporting:view" policy before the catalogue is built at all.
    [Fact]
    public async Task Get_Catalog_Returns_Forbidden_For_CompanyAdministrator_Without_Operational_HR_Role()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.CompanyAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Catalog_Returns_Only_LeaveSummary_For_Manager_With_No_Other_Category_Access()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        Assert.Equal(4, payload!.Items.Count);
        Assert.Contains(payload.Items, i => i.Id == "leave-summary");
        Assert.Contains(payload.Items, i => i.Id == "probation-report");
        Assert.Contains(payload.Items, i => i.Id == "onboarding-progress");
        Assert.Contains(payload.Items, i => i.Id == "workload-actions");
    }

    [Fact]
    public async Task Get_Catalog_Flags_Every_Report_As_ManagerReport_For_Manager()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Manager);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        Assert.All(payload!.Items, i => Assert.True(i.ManagerReport));
    }

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
    public async Task Get_Catalog_Excludes_WorkloadActions_For_Recruiter()
    {
        // Bug fix: Workload & HR Actions Report is an Hr-category report and must not appear in a
        // pure Recruiter's (no Manager/HrAdministrator role) reports list — see
        // reporting:view-workload-actions in IdentityModule.cs and GetReportCatalog/Endpoint.cs.
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        Assert.DoesNotContain(payload!.Items, i => i.Id == "workload-actions");
    }

    [Fact]
    public async Task Get_Catalog_Flags_Exactly_The_Four_Manager_Reports_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        var flagged = payload!.Items.Where(i => i.ManagerReport).Select(i => i.Id).OrderBy(x => x).ToList();
        Assert.Equal(["leave-summary", "onboarding-progress", "probation-report", "workload-actions"], flagged);
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
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        Assert.Equal(4, payload!.Items.Count);
        Assert.Contains(payload.Items, i => i.Id == "recruitment-pipeline-summary" && i.Category == "Recruitment");
        Assert.Contains(payload.Items, i => i.Id == "recruitment-pipeline-report" && i.Category == "Recruitment");
        Assert.Contains(payload.Items, i => i.Id == "vacancy-performance-report" && i.Category == "Recruitment");
        Assert.Contains(payload.Items, i => i.Id == "employee-starters" && i.Category == "Hr");
        // Bug fix: Workload & HR Actions Report must not leak into a pure Recruiter's catalog —
        // see Get_Catalog_Excludes_WorkloadActions_For_Recruiter above for the dedicated assertion.
        Assert.DoesNotContain(payload.Items, i => i.Id == "workload-actions");
    }

    [Fact]
    public async Task Get_Catalog_Returns_All_Hr_Related_Categories_For_HrAdministrator()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        using var client = await ClientFor(userId, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CatalogPayload>();
        Assert.NotNull(payload);
        Assert.Equal(15, payload!.Items.Count);
        Assert.Contains(payload.Items, i => i.Id == "hr-headcount-summary" && i.Category == "Hr");
        Assert.Contains(payload.Items, i => i.Id == "equality-diversity" && i.Category == "Hr");
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
        Assert.Equal(18, payload!.Items.Count);
        Assert.Contains(payload.Items, i => i.Id == "workload-actions");
        Assert.Contains(payload.Items, i => i.Id == "equality-diversity");
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
        Assert.Contains(payload.Items, i => i.Id == "probation-report");
    }

    private sealed record CatalogPayload(List<CatalogItemPayload> Items);

    private sealed record CatalogItemPayload(string Id, string DisplayName, string Category, string Description, bool ManagerReport = false);
}
