using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Proves the sickness:manage / sickness:view-team FastEndpoints policy declarations
/// actually enforce access end-to-end over real HTTP. Unit tests on handlers cannot
/// exercise policy middleware, so this coverage lives exclusively at this layer.
/// </summary>
[Collection("Integration")]
public class SicknessAuthorizationTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid PlainEmployeeUser = new("ee000001-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUser = new("ee000001-0000-0000-0000-000000000002");
    private static readonly Guid OtherManagerUser = new("ee000001-0000-0000-0000-000000000003");
    private static readonly Guid HrAdminUser = new("ee000001-0000-0000-0000-000000000004");
    private static readonly Guid CompanyAdministratorUser = new("ee000001-0000-0000-0000-000000000005");

    public SicknessAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, OtherManagerUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, OtherManagerUser, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdministratorUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdministratorUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    // --- sickness:manage — plain Employee is denied on HR-only endpoints ---

    // ListSicknessCategories is read-only reference data (category names) shared with employee
    // self-service "notify sickness" — same pattern as ListDocumentTypes — so it's "role:employee"
    // rather than "sickness:manage" and a plain employee is allowed to list it.
    [Fact]
    public async Task PlainEmployee_Gets_Ok_Listing_Sickness_Categories()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/sickness-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Creating_Sickness_Category()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = "Cold",
            displayOrder = 1
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Listing_Employee_Sickness_Records()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Recording_Sickness_On_Behalf_Of_Another_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records",
            new
            {
                companyId,
                employeeId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                startDate = "2026-07-01",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Ok_Listing_Sickness_Categories()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, HrAdminUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/sickness-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Company Administrator is scoped to company profile/settings management only and no
    // longer holds sickness:manage / sickness:view-team — see the narrowing in
    // HR.Modules.Identity.IdentityModule.AddRolePolicies.
    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Creating_Sickness_Category()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, CompanyAdministratorUser);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = "Cold",
            displayOrder = 1
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Listing_Employee_Sickness_Records()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, CompanyAdministratorUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- sickness:view-team — GetTeamSicknessToday manager self-scoping ---

    [Fact]
    public async Task Manager_Gets_Ok_Viewing_Own_Team_Sickness_Today()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, ManagerUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{ManagerUser}/team-sickness-today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Manager_Gets_Forbidden_Viewing_Different_Managers_Team_Sickness_Today()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, ManagerUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{OtherManagerUser}/team-sickness-today");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Ok_Viewing_Any_Managers_Team_Sickness_Today()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, HrAdminUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{ManagerUser}/team-sickness-today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Viewing_Team_Sickness_Today()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{PlainEmployeeUser}/team-sickness-today");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Viewing_Team_Sickness_Today()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, CompanyAdministratorUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{CompanyAdministratorUser}/team-sickness-today");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- RecordMySickness / GetMySicknessRecords self-service scoping ---

    [Fact]
    public async Task Employee_Can_Record_And_List_Their_Own_Sickness()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        // Plain employees cannot create categories directly — use HR admin for setup.
        using var hrClient = await ClientFor(companyId, HrAdminUser);
        var setupResponse = await hrClient.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = $"Category-{Guid.NewGuid():N}",
            displayOrder = 1
        });
        setupResponse.EnsureSuccessStatusCode();
        var category = await setupResponse.Content.ReadFromJsonAsync<CategoryPayload>();

        var recordResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{PlainEmployeeUser}/sickness-records/my",
            new
            {
                companyId,
                employeeId = PlainEmployeeUser,
                categoryId = category!.Id,
                startDate = "2026-07-01",
                startDayPart = 0
            });
        Assert.Equal(HttpStatusCode.Created, recordResponse.StatusCode);

        var listResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{PlainEmployeeUser}/sickness-records/my");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var payload = await listResponse.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Records);
    }

    [Fact]
    public async Task Employee_Gets_Forbidden_Listing_Another_Employees_Own_Sickness_Records()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records/my");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- sickness:review — GetOverdueReturnToWorkReviews / GetMissingFitNotes now include
    // Manager (dashboard widening; was previously "sickness:manage", HrAdministrator only) ---

    [Fact]
    public async Task Manager_Gets_Ok_Getting_Overdue_ReturnToWork_Reviews()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, ManagerUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/overdue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Getting_Overdue_ReturnToWork_Reviews()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/return-to-work-reviews/overdue");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_Gets_Ok_Getting_Missing_Fit_Notes()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, ManagerUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/sickness-evidence-requests/missing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Getting_Missing_Fit_Notes()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/sickness-evidence-requests/missing");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record CategoryPayload(Guid Id);

    private sealed record SicknessRecordSummaryPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        Guid CategoryId,
        string Status,
        string StartDate,
        string StartDayPart,
        string? EndDate,
        decimal? TotalDays,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record ListPayload(List<SicknessRecordSummaryPayload> Records);
}
