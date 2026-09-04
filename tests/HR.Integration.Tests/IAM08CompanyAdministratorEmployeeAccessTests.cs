using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// IAM-08 — a "Company Administrator only" account must never inherit employee visibility, at the
/// authoritative enforcement boundary (the API). Also proves api/me exposes the effective role-id
/// set (RoleIds) with no stale state after a role is removed, and that the initial signup persona
/// (Employee + CompanyAdministrator + HrAdministrator) is unaffected.
/// </summary>
[Collection("Integration")]
public class IAM08CompanyAdministratorEmployeeAccessTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid CompanyAdminOnly = new("1a080000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdminPlusHrAdmin = new("1a080000-0000-0000-0000-000000000002");
    private static readonly Guid CompanyAdminPlusManager = new("1a080000-0000-0000-0000-000000000003");
    private static readonly Guid RoleRemovalPersona = new("1a080000-0000-0000-0000-000000000004");
    private static readonly Guid InitialCreatorPersona = new("1a080000-0000-0000-0000-000000000005");

    // Permissions a Company-Administrator-only account must NOT hold.
    private static readonly Guid EmployeeReadPerm = new("00000000-0000-0000-0001-000000000003");
    private static readonly Guid EmployeeEditPerm = new("00000000-0000-0000-0001-000000000004");
    private static readonly Guid LeaveApprovePerm = new("00000000-0000-0000-0001-000000000008");
    private static readonly Guid SicknessReadPerm = new("00000000-0000-0000-0001-000000000014");
    private static readonly Guid SicknessManagePerm = new("00000000-0000-0000-0001-000000000015");
    private static readonly Guid HrSettingsManagePerm = new("00000000-0000-0000-0001-000000000018");
    private static readonly Guid ReportingViewPerm = new("00000000-0000-0000-0001-000000000034");

    public IAM08CompanyAdministratorEmployeeAccessTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminOnly, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminOnly, SystemRoles.CompanyAdministrator);

            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminPlusHrAdmin, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminPlusHrAdmin, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminPlusHrAdmin, SystemRoles.HrAdministrator);

            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminPlusManager, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminPlusManager, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminPlusManager, SystemRoles.Manager);

            await TestRoleSeeder.AssignRoleAsync(factory, RoleRemovalPersona, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, RoleRemovalPersona, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, RoleRemovalPersona, SystemRoles.HrAdministrator);

            await TestRoleSeeder.AssignRoleAsync(factory, InitialCreatorPersona, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, InitialCreatorPersona, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, InitialCreatorPersona, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private async Task<MePayload> GetMe(Guid userId, Guid companyId)
    {
        using var client = await ClientFor(userId, companyId);
        var response = await client.GetAsync("/api/me");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<MePayload>();
        Assert.NotNull(payload);
        return payload!;
    }

    private static void AssertForbidden(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Expected 401/403 but got {(int)response.StatusCode} {response.StatusCode}");

    private static void AssertReachedHandler(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized),
            $"Expected the request to pass authorization (not 401/403) but got {(int)response.StatusCode} {response.StatusCode}");

    // ---------------------------------------------------------------------
    // api/me — Company-Administrator-only account
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CompanyAdministratorOnly_Me_HasOnlyEmployeeAndCompanyAdministratorRoles_AndNoEmployeePermissions()
    {
        var companyId = Guid.NewGuid();
        var me = await GetMe(CompanyAdminOnly, companyId);

        Assert.Contains(SystemRoles.Employee, me.RoleIds);
        Assert.Contains(SystemRoles.CompanyAdministrator, me.RoleIds);
        Assert.DoesNotContain(SystemRoles.HrAdministrator, me.RoleIds);
        Assert.DoesNotContain(SystemRoles.Manager, me.RoleIds);
        Assert.DoesNotContain(SystemRoles.Recruiter, me.RoleIds);

        foreach (var forbidden in new[]
        {
            EmployeeReadPerm, EmployeeEditPerm, LeaveApprovePerm, SicknessReadPerm,
            SicknessManagePerm, HrSettingsManagePerm, ReportingViewPerm,
        })
            Assert.DoesNotContain(forbidden, me.PermissionIds);
    }

    [Fact]
    public async Task CompanyAdministratorOnly_CannotReach_EmployeeList_Or_HeadcountSummary()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminOnly, companyId);

        AssertForbidden(await client.GetAsync($"/api/companies/{companyId}/employees"));
        AssertForbidden(await client.GetAsync($"/api/companies/{companyId}/employees/headcount-summary"));
        AssertForbidden(await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/leaving-process"));
    }

    // ---------------------------------------------------------------------
    // Combined personas
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CompanyAdministratorPlusHrAdministrator_CanReach_EmployeeList_AndMeHasEmployeeRead()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminPlusHrAdmin, companyId);

        AssertReachedHandler(await client.GetAsync($"/api/companies/{companyId}/employees"));

        var me = await GetMe(CompanyAdminPlusHrAdmin, companyId);
        Assert.Contains(EmployeeReadPerm, me.PermissionIds);
    }

    [Fact]
    public async Task CompanyAdministratorPlusManager_CanReach_ManagerScopedEmployeeRead_ButNotEmployeeAdministration()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(CompanyAdminPlusManager, companyId);

        // Manager holds employee:read -> workforce analytics reaches the handler.
        AssertReachedHandler(await client.GetAsync($"/api/companies/{companyId}/employees/headcount-summary"));

        // But not employee:manage -> the employee administration list stays forbidden.
        AssertForbidden(await client.GetAsync($"/api/companies/{companyId}/employees"));
    }

    // ---------------------------------------------------------------------
    // Role removal — no stale permissions on the next api/me
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RemovingHrAdministrator_DropsEmployeeRead_AndHrAdministratorRoleId_FromMe()
    {
        var companyId = Guid.NewGuid();

        var before = await GetMe(RoleRemovalPersona, companyId);
        Assert.Contains(EmployeeReadPerm, before.PermissionIds);
        Assert.Contains(SystemRoles.HrAdministrator, before.RoleIds);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var hrRole = await db.UserRoles.SingleAsync(ur =>
                ur.UserId == RoleRemovalPersona && ur.RoleId == SystemRoles.HrAdministrator);
            db.UserRoles.Remove(hrRole);
            await db.SaveChangesAsync();
        }

        // Fresh client — there is no server-side permission/role cache.
        var after = await GetMe(RoleRemovalPersona, companyId);
        Assert.DoesNotContain(SystemRoles.HrAdministrator, after.RoleIds);
        Assert.DoesNotContain(EmployeeReadPerm, after.PermissionIds);
        Assert.DoesNotContain(EmployeeEditPerm, after.PermissionIds);
        Assert.Contains(SystemRoles.CompanyAdministrator, after.RoleIds);
    }

    // ---------------------------------------------------------------------
    // Initial creator regression — signup assigns all three roles
    // ---------------------------------------------------------------------

    [Fact]
    public async Task InitialCreatorPersona_KeepsAllThreeRoleIds_AndCanReachEmployeeList()
    {
        var companyId = Guid.NewGuid();

        var me = await GetMe(InitialCreatorPersona, companyId);
        Assert.Contains(SystemRoles.Employee, me.RoleIds);
        Assert.Contains(SystemRoles.CompanyAdministrator, me.RoleIds);
        Assert.Contains(SystemRoles.HrAdministrator, me.RoleIds);

        using var client = await ClientFor(InitialCreatorPersona, companyId);
        AssertReachedHandler(await client.GetAsync($"/api/companies/{companyId}/employees"));
    }

    private sealed record MePayload(
        Guid UserId,
        Guid CompanyId,
        string? Email,
        List<Guid> PermissionIds,
        List<Guid> RoleIds,
        bool CanManageCompany,
        bool IsHrAdministrator,
        bool IsManager,
        bool IsRecruiter,
        bool IsEmailConfirmed);
}
