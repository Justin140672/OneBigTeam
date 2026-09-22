using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Resource-level (self / manager-hierarchy / HR-admin) authorization for GetEmployee, guarded by
/// HR.Modules.Employees.Services.EmployeesResourceAuthorizer. Endpoint-level Policies("role:employee")
/// only proves tenant/role membership; it never proves the caller has a relationship to the specific
/// employeeId in the route, so these tests exercise that resource-ownership check end-to-end over
/// real HTTP — mirroring LeaveResourceAuthorizationTests's pattern for the same class of bug.
/// </summary>
[Collection("Integration")]
public class GetEmployeeResourceAuthorizationTests(ApiWebApplicationFactory factory)
{
    private static readonly Guid EmploymentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid DepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Allows_Employee_Viewing_Own_Record()
    {
        var employee = await CreateEmployeeAsync();

        using var client = await AuthenticatedClient(employee);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/employees/{employee}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var employee = await CreateEmployeeAsync();
        var peer = await CreateEmployeeAsync();

        using var client = await AuthenticatedClient(peer);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/employees/{employee}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Allows_Direct_Manager_Viewing_Report()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
            await AssignManagerAsync(setupClient, report, manager);

        using var managerClient = await AuthenticatedClient(manager);
        var response = await managerClient.GetAsync($"/api/companies/{SeededCompanyId}/employees/{report}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Allows_Skip_Level_Manager_In_Three_Level_Hierarchy()
    {
        var seniorManager = await CreateEmployeeAsync(); // C
        var manager = await CreateEmployeeAsync();       // B
        var employee = await CreateEmployeeAsync();      // A

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, employee, manager);
            await AssignManagerAsync(setupClient, manager, seniorManager);
        }

        using var seniorManagerClient = await AuthenticatedClient(seniorManager);
        var response = await seniorManagerClient.GetAsync($"/api/companies/{SeededCompanyId}/employees/{employee}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Unrelated_Branch_Manager()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();
        var unrelatedEmployee = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
            await AssignManagerAsync(setupClient, report, manager);

        using var managerClient = await AuthenticatedClient(manager);
        var response = await managerClient.GetAsync($"/api/companies/{SeededCompanyId}/employees/{unrelatedEmployee}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Own_Manager_Viewed_Bottom_Up()
    {
        // Denial case: being someone's report does not grant you view rights over your manager's
        // record — the hierarchy check is one-directional (manager -> report only).
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
            await AssignManagerAsync(setupClient, report, manager);

        using var reportClient = await AuthenticatedClient(report);
        var response = await reportClient.GetAsync($"/api/companies/{SeededCompanyId}/employees/{manager}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_Response_Contains_Only_Operational_Fields_Over_The_Wire()
    {
        // GetEmployee field-level access matrix (see 30-administrative-role-separation-matrix.md): assert on the actual JSON payload a manager receives,
        // not just the HTTP status code — permitted operational fields must be present, and every
        // sensitive field the ticket calls out (personal email, DOB, nationality/gender, home
        // phone, home address, leaving-process detail, notice period, HR notes, system-access
        // state) must be structurally absent from the response, not merely null.
        var employee = await CreateEmployeeAsync();
        var manager = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
            await AssignManagerAsync(setupClient, employee, manager);

        using var managerClient = await AuthenticatedClient(manager);
        var response = await managerClient.GetAsync($"/api/companies/{SeededCompanyId}/employees/{employee}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        string[] permittedFields =
        [
            "id", "companyId", "firstName", "lastName", "workEmail", "startDate", "status",
            "employeeNumber", "employmentTypeId", "showOnboardingTab", "showProbationTab",
            "showOffboardingTab", "showLeavingTab", "canStartLeavingProcess",
        ];
        foreach (var field in permittedFields)
            Assert.True(json.TryGetProperty(field, out _), $"Expected permitted field '{field}' to be present.");

        string[] restrictedFields =
        [
            "personalEmail", "dateOfBirth", "nationality", "gender", "genderOther",
            "phoneNumber", "homePhone", "addressLine1", "addressLine2", "city", "county",
            "postCode", "country", "hasSystemAccess", "workingDaysOverride", "hoursPerDayOverride",
            "continuousServiceDate", "probationEndDate", "leavingDate", "noticePeriodUnitOverride",
            "noticePeriodLengthOverride", "notes", "effectiveNoticePeriodUnit",
            "effectiveNoticePeriodLength", "effectiveNoticePeriodSource", "version",
        ];
        foreach (var field in restrictedFields)
            Assert.False(json.TryGetProperty(field, out _), $"Expected restricted field '{field}' to be absent.");
    }

    [Fact]
    public async Task Hr_Response_Contains_Sensitive_Fields()
    {
        // The counterpart to the manager test above: HR Administrator access must still return
        // the full administrative record, proving the restriction is scoped to manager access
        // only, not applied globally.
        var employee = await CreateEmployeeAsync();

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);
        var response = await hrClient.GetAsync($"/api/companies/{SeededCompanyId}/employees/{employee}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.TryGetProperty("dateOfBirth", out _));
        Assert.True(json.TryGetProperty("nationality", out _));
        Assert.True(json.TryGetProperty("hasSystemAccess", out _));
        Assert.True(json.TryGetProperty("notes", out _));
    }

    [Fact]
    public async Task Allows_HrAdministrator()
    {
        var employee = await CreateEmployeeAsync();

        using var hrClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);
        var response = await hrClient.GetAsync($"/api/companies/{SeededCompanyId}/employees/{employee}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Company_Administrator_Without_Hr_Role()
    {
        var employee = await CreateEmployeeAsync();

        using var client = await AuthenticatedClient(Guid.NewGuid(), companyAdministrator: true);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/employees/{employee}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Recruiter_Without_Relationship_To_Target()
    {
        var employee = await CreateEmployeeAsync();

        using var client = await AuthenticatedClient(Guid.NewGuid(), recruiter: true);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/employees/{employee}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(
        Guid userId,
        bool hrAdministrator = false,
        bool manager = false,
        bool recruiter = false,
        bool companyAdministrator = false)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee, SeededCompanyId);

        if (manager)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Manager, SeededCompanyId);

        if (recruiter)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Recruiter, SeededCompanyId);

        if (companyAdministrator)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.CompanyAdministrator, SeededCompanyId);

        if (hrAdministrator)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.HrAdministrator, SeededCompanyId);

        return client;
    }

    /// <summary>
    /// Creates a real employee via the employees API and returns its id. An employee's id doubles
    /// as the identity user id for the linked account (see GetMyEmployeeHandler's `e.Id == userId`
    /// lookup), so this id is used both as the target employeeId and as the
    /// TestAuthHandler.UserHeader value when acting "as" that employee.
    /// </summary>
    private async Task<Guid> CreateEmployeeAsync()
    {
        using var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var unique = Guid.NewGuid().ToString("N")[..12];

        var response = await setupClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees",
            new
            {
                companyId = SeededCompanyId,
                firstName = "Test",
                lastName = $"Employee-{unique}",
                workEmail = $"getemp.auth.{unique}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"GEN-{unique}",
                employmentTypeId = EmploymentTypeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private async Task AssignManagerAsync(HttpClient client, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/manager",
            new { companyId = SeededCompanyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    private sealed record IdPayload(Guid Id);
}
