using System.Net.Http.Json;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// API arrange/verify helpers for InternalAppointmentTests (internal recruitment Ticket 7 — the
/// vacancy Applications tab's "Appoint" action, POST .../applications/{id}/appoint).
///
/// Who appoints: the appoint endpoint requires recruitment:manage ONLY
/// (AppointInternalCandidate.Endpoint: Policies("recruitment:manage")), so the seeded Recruiter
/// persona (Marcus, <see cref="RecruiterEmail"/> — recruitment:manage, no employee:manage) is the
/// default appointer. Because Marcus cannot open the full employee record, the success banner shows
/// the employee's name rather than a "View employee profile" link for him.
///
/// Why a dedicated HR Administrator + Recruiter "appointer" user as well: tests that go on to FOLLOW
/// the banner's profile link need a user who can both appoint and open the full employee record. No
/// seeded dev persona holds both — recruitment:manage is Recruiter-only (Marcus) and employee:manage is
/// HR-Administrator-only (Laura/David), see RolePermissionConfiguration. Rather than mutate a shared
/// seeded persona's roles, <see cref="EnsureAppointerAsync"/> creates ONE brand-new Active Acme
/// employee with its own login (the same InternalVacancyApplyApi building block the internal-apply
/// tests use) and grants it Employee + HR Administrator + Recruiter through the real
/// PUT .../users/{userId}/roles endpoint, as Laura (users:manage; an HR Administrator may administer
/// both roles — RoleAdministrationPolicy). The user is created lazily once per test process and only
/// ever READ from afterwards (it logs in and performs appointments on other, per-test employees), so
/// sharing it across tests adds no cross-test coupling.
/// </summary>
internal static class InternalAppointmentApi
{
    private static readonly Guid AcmeId = InternalVacancyApplyApi.AcmeId;

    /// <summary>The seeded Recruiter persona (Marcus — RecruiterPersonaFixture): recruitment:manage, no employee:manage.</summary>
    public const string RecruiterEmail = "marcus.diallo@acme.example";

    // SystemRoles (HR.Modules.Identity.Domain.SystemRoles) — internal to the module, so mirrored here.
    private static readonly Guid EmployeeRoleId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid RecruiterRoleId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid HrAdministratorRoleId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    public sealed record Appointer(Guid Id, string FullName, string WorkEmail);

    private static readonly SemaphoreSlim AppointerLock = new(1, 1);
    private static Appointer? _appointer;

    /// <summary>
    /// Returns the process-wide HR Administrator + Recruiter appointer user (see class remarks),
    /// creating it on first use. Only needed when a test must also open the appointee's full profile.
    /// Makes real Supabase calls on first use (ensure-employee-login + /api/login).
    /// </summary>
    public static async Task<Appointer> EnsureAppointerAsync(HttpClient hrAdminApi, string apiBaseUrl)
    {
        if (_appointer is not null) return _appointer;

        await AppointerLock.WaitAsync();
        try
        {
            if (_appointer is not null) return _appointer;

            var employee = await InternalVacancyApplyApi.CreateActiveEmployeeWithLoginAsync(hrAdminApi, apiBaseUrl);

            var rolesResponse = await hrAdminApi.PutAsJsonAsync(
                $"/api/companies/{AcmeId}/users/{employee.Id}/roles",
                new
                {
                    companyId = AcmeId,
                    userId = employee.Id,
                    roleIds = new[] { EmployeeRoleId, HrAdministratorRoleId, RecruiterRoleId },
                });
            Assert.True(rolesResponse.IsSuccessStatusCode,
                $"Granting HR Administrator + Recruiter to the appointer failed with {rolesResponse.StatusCode}: {await rolesResponse.Content.ReadAsStringAsync()}");

            // Prove the appointer really holds both permissions (recruitment:manage to appoint,
            // employee:manage to open the appointee's full profile), so a role/permission drift
            // surfaces here with a clear message instead of as a UI 403 later.
            using (var appointerApi = await InternalVacancyApplyApi.CreateEmployeeApiClientAsync(apiBaseUrl, employee.WorkEmail))
            {
                var employeeManage = await appointerApi.GetAsync($"/api/companies/{AcmeId}/employees?pageSize=1");
                Assert.True(employeeManage.IsSuccessStatusCode,
                    $"Expected the appointer to hold employee:manage (GET employees), got {employeeManage.StatusCode}.");

                var recruitmentManage = await appointerApi.GetAsync($"/api/companies/{AcmeId}/vacancies/stale");
                Assert.True(recruitmentManage.IsSuccessStatusCode,
                    $"Expected the appointer to hold recruitment:manage (GET vacancies/stale), got {recruitmentManage.StatusCode}.");
            }

            _appointer = new Appointer(employee.Id, employee.FullName, employee.WorkEmail);
            return _appointer;
        }
        finally
        {
            AppointerLock.Release();
        }
    }

    public sealed record EmployeeSnapshot(
        Guid Id,
        Guid? PositionProfileId,
        string? PositionTitle,
        Guid? DepartmentId,
        Guid? LocationId,
        Guid? ManagerId,
        string? ManagerFullName,
        string FirstName,
        string LastName);

    /// <summary>GET /api/companies/{companyId}/employees/{id} (HR Administrator client).</summary>
    public static async Task<EmployeeSnapshot> GetEmployeeAsync(HttpClient hrAdminApi, Guid employeeId)
    {
        var response = await hrAdminApi.GetAsync($"/api/companies/{AcmeId}/employees/{employeeId}");
        Assert.True(response.IsSuccessStatusCode,
            $"GET employee {employeeId} failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var employee = await response.Content.ReadFromJsonAsync<EmployeeSnapshot>();
        Assert.NotNull(employee);
        return employee!;
    }

    /// <summary>
    /// Total number of Acme employees (any status) whose name/email/number contains
    /// <paramref name="search"/> — GET /api/companies/{companyId}/employees?search=... (employee:manage).
    /// </summary>
    public static async Task<int> CountEmployeesMatchingAsync(HttpClient hrAdminApi, string search)
    {
        var response = await hrAdminApi.GetAsync(
            $"/api/companies/{AcmeId}/employees?search={Uri.EscapeDataString(search)}&pageSize=50");
        Assert.True(response.IsSuccessStatusCode,
            $"List employees failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var list = await response.Content.ReadFromJsonAsync<EmployeeListSnapshot>();
        Assert.NotNull(list);
        return list!.TotalCount;
    }

    public sealed record VacancySnapshot(
        Guid Id,
        Guid PositionProfileId,
        string? PositionProfileTitle,
        Guid? PositionProfileDepartmentId,
        string? EffectiveLocation);

    /// <summary>GET /api/companies/{companyId}/vacancies/{vacancyId} (recruitment:view).</summary>
    public static async Task<VacancySnapshot> GetVacancyAsync(HttpClient recruiterApi, Guid vacancyId)
    {
        var response = await recruiterApi.GetAsync($"/api/companies/{AcmeId}/vacancies/{vacancyId}");
        Assert.True(response.IsSuccessStatusCode,
            $"GET vacancy {vacancyId} failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var vacancy = await response.Content.ReadFromJsonAsync<VacancySnapshot>();
        Assert.NotNull(vacancy);
        return vacancy!;
    }

    /// <summary>GET /api/companies/{companyId}/departments/{id} — returns the department's name.</summary>
    public static async Task<string> GetDepartmentNameAsync(HttpClient hrAdminApi, Guid departmentId)
    {
        var response = await hrAdminApi.GetAsync($"/api/companies/{AcmeId}/departments/{departmentId}");
        Assert.True(response.IsSuccessStatusCode,
            $"GET department {departmentId} failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var department = await response.Content.ReadFromJsonAsync<DepartmentSnapshot>();
        Assert.NotNull(department);
        return department!.Name;
    }

    public sealed record CompensationSnapshot(
        DateOnly EffectiveFrom,
        string SalaryType,
        decimal Salary,
        string Currency,
        string? Notes,
        string Reason);

    /// <summary>GET /api/companies/{companyId}/employees/{employeeId}/compensation/current (employee:manage).</summary>
    public static async Task<CompensationSnapshot> GetCurrentCompensationAsync(HttpClient hrAdminApi, Guid employeeId)
    {
        var response = await hrAdminApi.GetAsync($"/api/companies/{AcmeId}/employees/{employeeId}/compensation/current");
        Assert.True(response.IsSuccessStatusCode,
            $"GET current compensation for {employeeId} failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var compensation = await response.Content.ReadFromJsonAsync<CompensationSnapshot>();
        Assert.NotNull(compensation);
        return compensation!;
    }

    private sealed record EmployeeListSnapshot(int TotalCount);

    private sealed record DepartmentSnapshot(string Name);
}
