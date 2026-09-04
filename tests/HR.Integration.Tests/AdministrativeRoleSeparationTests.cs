using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// ADM-05: end-to-end proof of administrative role separation at the authoritative
/// enforcement boundary (the API). One authenticated persona per role is exercised against a
/// representative protected endpoint from every administrative area in
/// <c>specifications/product-specifications/30-administrative-role-separation-matrix.md</c>.
///
/// The load-bearing assertions are the negatives: a disallowed role must always get 401/403 and
/// must never get a 2xx. "Allowed" cases are tolerant of 2xx vs 404-with-valid-body (integration
/// seed data is deliberately thin) but strict that the response is NOT 401/403 — i.e. the
/// authorization layer let the request through to the handler.
/// </summary>
[Collection("Integration")]
public class AdministrativeRoleSeparationTests
{
    private readonly ApiWebApplicationFactory _factory;

    // Fixed per-role personas. Distinct literal ids (not Guid.NewGuid()) so the constructor's
    // one-time role seeding is stable, but namespaced under this file's own prefix so they can't
    // collide with personas seeded by sibling test classes under the shared database collection.
    private static readonly Guid EmployeeOnly       = new("ad050000-0000-0000-0000-000000000001");
    private static readonly Guid ManagerPersona     = new("ad050000-0000-0000-0000-000000000002");
    private static readonly Guid RecruiterPersona   = new("ad050000-0000-0000-0000-000000000003");
    private static readonly Guid HrAdminPersona     = new("ad050000-0000-0000-0000-000000000004");
    private static readonly Guid CompanyAdminPersona = new("ad050000-0000-0000-0000-000000000005");
    private static readonly Guid CompanyAdminPlusHrAdminPersona = new("ad050000-0000-0000-0000-000000000006");

    public AdministrativeRoleSeparationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            // Every persona also holds the Employee floor role, mirroring production (everyone is
            // an Employee) and the sibling authz test classes.
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeOnly, SystemRoles.Employee);

            await TestRoleSeeder.AssignRoleAsync(factory, ManagerPersona, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerPersona, SystemRoles.Manager);

            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterPersona, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterPersona, SystemRoles.Recruiter);

            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminPersona, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminPersona, SystemRoles.HrAdministrator);

            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminPersona, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminPersona, SystemRoles.CompanyAdministrator);

            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminPlusHrAdminPersona, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminPlusHrAdminPersona, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminPlusHrAdminPersona, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    // Role keys used by [Theory] InlineData -> persona id.
    private const string Employee = "employee";
    private const string Manager = "manager";
    private const string Recruiter = "recruiter";
    private const string HrAdmin = "hr-admin";
    private const string CompanyAdmin = "company-admin";
    private const string CompanyAdminPlusHrAdmin = "company-admin+hr-admin";

    private static Guid PersonaFor(string roleKey) => roleKey switch
    {
        Employee => EmployeeOnly,
        Manager => ManagerPersona,
        Recruiter => RecruiterPersona,
        HrAdmin => HrAdminPersona,
        CompanyAdmin => CompanyAdminPersona,
        CompanyAdminPlusHrAdmin => CompanyAdminPlusHrAdminPersona,
        _ => throw new ArgumentOutOfRangeException(nameof(roleKey), roleKey, "Unknown role key")
    };

    private async Task<HttpClient> ClientFor(string roleKey, Guid companyId)
    {
        var userId = PersonaFor(roleKey);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private HttpClient AnonymousClient() => _factory.CreateClient();

    private static void AssertForbidden(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Expected 401/403 but got {(int)response.StatusCode} {response.StatusCode}");

    private static void AssertReachedHandler(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized),
            $"Expected the request to pass authorization (not 401/403) but got {(int)response.StatusCode} {response.StatusCode}");

    // ---------------------------------------------------------------------
    // Employee administration - list  (employee:manage)
    // GET /api/companies/{companyId}/employees
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Employee)]
    [InlineData(Manager)]
    [InlineData(Recruiter)]
    [InlineData(CompanyAdmin)]
    public async Task EmployeeList_IsForbidden_ForRolesWithoutEmployeeManage(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees");

        AssertForbidden(response);
    }

    [Theory]
    [InlineData(HrAdmin)]
    [InlineData(CompanyAdminPlusHrAdmin)]
    public async Task EmployeeList_IsAllowed_ForRolesWithEmployeeManage(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees");

        AssertReachedHandler(response);
    }

    // ---------------------------------------------------------------------
    // Employee selection list  (employee:read)
    // GET /api/companies/{companyId}/employees/selectable
    // The lightweight name/id picker projection — Manager / Recruiter / HR Administrator may
    // reach it (dropdowns such as the vacancy hiring-manager picker), but a plain Employee or a
    // Company-Administrator-only user may not.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Manager)]
    [InlineData(Recruiter)]
    [InlineData(HrAdmin)]
    public async Task SelectableEmployeeList_IsAllowed_ForRolesWithEmployeeRead(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/selectable");

        AssertReachedHandler(response);
    }

    [Theory]
    [InlineData(Employee)]
    [InlineData(CompanyAdmin)]
    public async Task SelectableEmployeeList_IsForbidden_ForRolesWithoutEmployeeRead(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/selectable");

        AssertForbidden(response);
    }

    // ---------------------------------------------------------------------
    // Workforce analytics  (employee:read)
    // GET /api/companies/{companyId}/employees/headcount-summary
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Employee)]
    [InlineData(CompanyAdmin)]
    public async Task HeadcountSummary_IsForbidden_ForRolesWithoutEmployeeRead(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/headcount-summary");

        AssertForbidden(response);
    }

    [Theory]
    [InlineData(Manager)]
    [InlineData(Recruiter)]
    [InlineData(HrAdmin)]
    public async Task HeadcountSummary_IsAllowed_ForRolesWithEmployeeRead(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/headcount-summary");

        AssertReachedHandler(response);
    }

    // ---------------------------------------------------------------------
    // Offboarding read  (employee:read)
    // GET /api/companies/{companyId}/employees/{employeeId}/leaving-process
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Employee)]
    [InlineData(CompanyAdmin)]
    public async Task LeavingProcess_IsForbidden_ForRolesWithoutEmployeeRead(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/leaving-process");

        AssertForbidden(response);
    }

    [Fact]
    public async Task LeavingProcess_IsAllowed_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(HrAdmin, companyId);

        // Unseeded employee id: a 404 is acceptable here, a 401/403 is not.
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/leaving-process");

        AssertReachedHandler(response);
    }

    // ---------------------------------------------------------------------
    // Employee administration - mutate  (employee:manage)
    // POST /api/companies/{companyId}/employees
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Employee)]
    [InlineData(Manager)]
    [InlineData(Recruiter)]
    [InlineData(CompanyAdmin)]
    public async Task CreateEmployee_IsForbidden_ForEveryRoleExceptHrAdministrator(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Test",
            lastName = "Person",
            workEmail = $"test.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });

        AssertForbidden(response);
    }

    [Fact]
    public async Task CreateEmployee_PassesAuthorization_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(HrAdmin, companyId);

        // Missing reference data -> the handler may 400/422; what matters is it is not 401/403.
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Test",
            lastName = "Person",
            workEmail = $"test.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });

        AssertReachedHandler(response);
    }

    // ---------------------------------------------------------------------
    // User & role administration  (users:view)
    // GET /api/companies/{companyId}/users
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Employee)]
    [InlineData(Manager)]
    [InlineData(Recruiter)]
    [InlineData(CompanyAdmin)]
    public async Task ListUsers_IsForbidden_ForEveryRoleExceptHrAdministrator(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListUsers_IsAllowed_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(HrAdmin, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/users");

        AssertReachedHandler(response);
    }

    // ---------------------------------------------------------------------
    // HR settings  (hr-settings:manage)
    // PUT /api/companies/{companyId}/hr-settings
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Employee)]
    [InlineData(Manager)]
    [InlineData(Recruiter)]
    [InlineData(CompanyAdmin)]
    public async Task UpdateHrSettings_IsForbidden_ForEveryRoleExceptHrAdministrator(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", HrSettingsBody());

        AssertForbidden(response);
    }

    [Fact]
    public async Task UpdateHrSettings_IsAllowed_ForHrAdministrator()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, "ADM-05 HR Settings");
        using var client = await ClientFor(HrAdmin, companyId);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", HrSettingsBody());

        AssertReachedHandler(response);
    }

    private static object HrSettingsBody() => new
    {
        workingDays = 31,
        hoursPerDay = 7.5,
        leaveYearStartMonth = 1,
        defaultHolidayAllowance = 25,
        probationMonths = 6
    };

    // ---------------------------------------------------------------------
    // Company profile / settings  (company:manage)
    // PUT /api/companies/{companyId}/settings   +   GET .../settings/history
    // Company Administrator is the ONLY role permitted here; HR Administrator must be denied.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Employee)]
    [InlineData(Manager)]
    [InlineData(Recruiter)]
    [InlineData(HrAdmin)]
    public async Task UpdateCompanySettings_IsForbidden_ForEveryRoleExceptCompanyAdministrator(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/settings", CompanySettingsBody());

        AssertForbidden(response);
    }

    [Theory]
    [InlineData(CompanyAdmin)]
    [InlineData(CompanyAdminPlusHrAdmin)]
    public async Task UpdateCompanySettings_IsAllowed_ForCompanyAdministrator(string roleKey)
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, "ADM-05 Company Settings");
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/settings", CompanySettingsBody());

        AssertReachedHandler(response);
    }

    [Fact]
    public async Task CompanySettingsHistory_IsAllowed_ForCompanyAdministrator_ButForbidden_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();

        using var companyAdminClient = await ClientFor(CompanyAdmin, companyId);
        AssertReachedHandler(await companyAdminClient.GetAsync($"/api/companies/{companyId}/settings/history"));

        using var hrAdminClient = await ClientFor(HrAdmin, companyId);
        AssertForbidden(await hrAdminClient.GetAsync($"/api/companies/{companyId}/settings/history"));
    }

    private static object CompanySettingsBody() => new
    {
        timeZone = "UTC",
        locale = "en-GB",
    };

    // ---------------------------------------------------------------------
    // HR reports  (reporting:view-hr)
    // GET /api/companies/{companyId}/reporting/hr-headcount-summary
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Employee)]
    [InlineData(Manager)]
    [InlineData(Recruiter)]
    [InlineData(CompanyAdmin)]
    public async Task HrHeadcountReport_IsForbidden_ForRolesWithoutReportingViewHr(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/hr-headcount-summary");

        AssertForbidden(response);
    }

    [Fact]
    public async Task HrHeadcountReport_IsAllowed_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(HrAdmin, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/hr-headcount-summary");

        AssertReachedHandler(response);
    }

    // ---------------------------------------------------------------------
    // Recruitment  (candidate:view)
    // GET /api/companies/{companyId}/recruitment/applications
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Employee)]
    [InlineData(Manager)]
    [InlineData(HrAdmin)]
    [InlineData(CompanyAdmin)]
    public async Task RecruitmentApplications_IsForbidden_ForRolesWithoutCandidateView(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment/applications");

        AssertForbidden(response);
    }

    [Fact]
    public async Task RecruitmentApplications_IsAllowed_ForRecruiter()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(Recruiter, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment/applications");

        AssertReachedHandler(response);
    }

    // ---------------------------------------------------------------------
    // Leave administration  (leave:manage)
    // POST /api/companies/{companyId}/leave-policies
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Employee)]
    [InlineData(Recruiter)]
    [InlineData(CompanyAdmin)]
    public async Task CreateLeavePolicy_IsForbidden_ForRolesWithoutLeaveManage(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies", new
        {
            companyId,
            name = $"Policy-{Guid.NewGuid():N}",
            carryOverDays = 0,
            allowNegativeBalance = false
        });

        AssertForbidden(response);
    }

    [Fact]
    public async Task CreateLeavePolicy_PassesAuthorization_ForHrAdministrator()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, "ADM-05 Leave Admin");
        using var client = await ClientFor(HrAdmin, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/leave-policies", new
        {
            companyId,
            name = $"Policy-{Guid.NewGuid():N}",
            carryOverDays = 0,
            allowNegativeBalance = false
        });

        AssertReachedHandler(response);
    }

    // ---------------------------------------------------------------------
    // Sickness administration  (sickness:manage)
    // GET /api/companies/{companyId}/employees/{employeeId}/sickness-records
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Employee)]
    [InlineData(Recruiter)]
    [InlineData(CompanyAdmin)]
    public async Task SicknessRecords_IsForbidden_ForRolesWithoutSicknessManage(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records");

        AssertForbidden(response);
    }

    [Fact]
    public async Task SicknessRecords_IsAllowed_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(HrAdmin, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records");

        AssertReachedHandler(response);
    }

    // ---------------------------------------------------------------------
    // Anonymous (no auth header) -> 401 across a sample of protected endpoints
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("/api/companies/{c}/employees")]
    [InlineData("/api/companies/{c}/employees/headcount-summary")]
    [InlineData("/api/companies/{c}/users")]
    [InlineData("/api/companies/{c}/reporting/hr-headcount-summary")]
    public async Task ProtectedEndpoints_ReturnUnauthorized_ForAnonymousRequest(string routeTemplate)
    {
        using var client = AnonymousClient();
        var route = routeTemplate.Replace("{c}", Guid.NewGuid().ToString());

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------------
    // Positive: Company Administrator retains its own configuration surface
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CompanyAdministrator_CanReach_CompanyProfileAndSettingsReads()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdmin, companyId);

        AssertReachedHandler(await client.GetAsync($"/api/companies/{companyId}"));
        AssertReachedHandler(await client.GetAsync($"/api/companies/{companyId}/settings"));
        AssertReachedHandler(await client.GetAsync($"/api/companies/{companyId}/settings/history"));
    }

    [Fact]
    public async Task CompanyAdministrator_CanReach_CompanySettingsWrite()
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, "ADM-05 CA Write");
        using var client = await ClientFor(CompanyAdmin, companyId);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/settings", CompanySettingsBody());

        AssertReachedHandler(response);
    }

    [Fact]
    public async Task CompanyAdministrator_CanReach_SubscriptionStatus()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdmin, companyId);

        var response = await client.GetAsync("/api/companies/subscription-status");

        AssertReachedHandler(response);
    }

    [Fact]
    public async Task CompanyAdministrator_CanReach_OnboardingChecklist()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdmin, companyId);

        var response = await client.GetAsync("/api/company-onboarding/checklist");

        AssertReachedHandler(response);
    }

    // ---------------------------------------------------------------------
    // Subscription & billing  (subscription:manage)
    // POST /api/companies/subscription/cancel
    // ADM-05 / RestrictSubscriptionToCompanyAdministrator: subscription & billing is a company-
    // ownership function. Company Administrator is the ONLY role that holds subscription:manage;
    // HR Administrator's historical grant was removed by migration
    // RestrictSubscriptionToCompanyAdministrator.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Employee)]
    [InlineData(Manager)]
    [InlineData(Recruiter)]
    [InlineData(HrAdmin)]
    public async Task CancelSubscription_IsForbidden_ForEveryRoleExceptCompanyAdministrator(string roleKey)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(roleKey, companyId);

        var response = await client.PostAsync("/api/companies/subscription/cancel", content: null);

        AssertForbidden(response);
    }

    [Theory]
    [InlineData(CompanyAdmin)]
    [InlineData(CompanyAdminPlusHrAdmin)]
    public async Task CancelSubscription_PassesAuthorization_ForCompanyAdministrator(string roleKey)
    {
        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, "ADM-05 Subscription");
        using var client = await ClientFor(roleKey, companyId);

        // A 400/404/409 business outcome (no active paid subscription to cancel) is fine here —
        // what matters is the request is NOT rejected as 401/403 by the authorization layer.
        var response = await client.PostAsync("/api/companies/subscription/cancel", content: null);

        AssertReachedHandler(response);
    }

    [Fact]
    public async Task SubscriptionDetails_IsForbidden_ForHrAdministrator_ButAllowed_ForCompanyAdministrator()
    {
        var companyId = Guid.NewGuid();

        using var hrAdminClient = await ClientFor(HrAdmin, companyId);
        AssertForbidden(await hrAdminClient.GetAsync("/api/companies/subscription-details"));

        using var companyAdminClient = await ClientFor(CompanyAdmin, companyId);
        AssertReachedHandler(await companyAdminClient.GetAsync("/api/companies/subscription-details"));
    }

    [Fact]
    public async Task CompanyAdministrator_CanReach_SupportRequestsQueue()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdmin, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/support/requests");

        AssertReachedHandler(response);
    }
}
