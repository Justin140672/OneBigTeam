using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Integration coverage for the GetMyTeam self-service slice
/// (GET /api/companies/{companyId}/employees/me/team). The team is always scoped to the
/// caller's OWN resolved employee id — a caller can never see another manager's team.
/// Only Active reports are included.
/// </summary>
[Collection("Integration")]
public class GetMyTeamEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser = new("7ea70001-0000-0000-0000-000000000001");
    private static readonly Guid NoRoleUser = new("7ea70001-0000-0000-0000-000000000002");

    public GetMyTeamEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<(HttpClient AdminClient, Guid CompanyId, EmployeeReferenceDataSeeder.ReferenceData RefData)> ContextAsync()
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.Employee, companyId);
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);
        return (client, companyId, refData);
    }

    private async Task<Guid> CreateEmployeeAsync(
        HttpClient admin, Guid companyId, EmployeeReferenceDataSeeder.ReferenceData refData,
        string firstName, string lastName)
    {
        var response = await admin.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, firstName, lastName, $"{firstName}.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
        await TestRoleSeeder.AssignRoleAsync(_factory, id, SystemRoles.Employee, companyId);
        return id;
    }

    private async Task ActivateAsync(HttpClient admin, Guid companyId, Guid employeeId)
    {
        var response = await admin.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/employment",
            new
            {
                companyId,
                id = employeeId,
                employeeNumber = $"EMP-{Guid.NewGuid():N}",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-01"
            });
        response.EnsureSuccessStatusCode();
    }

    private async Task AssignManagerAsync(HttpClient admin, Guid companyId, Guid employeeId, Guid managerId)
    {
        var response = await admin.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/manager",
            new { companyId, employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    private HttpClient AsEmployee(Guid employeeId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_MyTeam_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/me/team");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_MyTeam_Returns_Forbidden_For_User_Without_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, NoRoleUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, NoRoleUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/me/team");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_MyTeam_Returns_Empty_When_Caller_Has_No_Reports()
    {
        var (admin, companyId, refData) = await ContextAsync();
        var loneEmployeeId = await CreateEmployeeAsync(admin, companyId, refData, "Lone", "Wolf");

        using var client = AsEmployee(loneEmployeeId, companyId);
        var payload = await client.GetFromJsonAsync<TeamPayload>(
            $"/api/companies/{companyId}/employees/me/team");

        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_MyTeam_Returns_Only_Active_Direct_Reports()
    {
        var (admin, companyId, refData) = await ContextAsync();
        var managerId = await CreateEmployeeAsync(admin, companyId, refData, "Mandy", "Manager");
        var activeReportId = await CreateEmployeeAsync(admin, companyId, refData, "Aaron", "Active");
        var draftReportId = await CreateEmployeeAsync(admin, companyId, refData, "Drew", "Draft");

        await ActivateAsync(admin, companyId, activeReportId);
        await AssignManagerAsync(admin, companyId, activeReportId, managerId);
        await AssignManagerAsync(admin, companyId, draftReportId, managerId); // left as Draft

        using var client = AsEmployee(managerId, companyId);
        var payload = await client.GetFromJsonAsync<TeamPayload>(
            $"/api/companies/{companyId}/employees/me/team");

        var member = Assert.Single(payload!.Items);
        Assert.Equal(activeReportId, member.EmployeeId);
        Assert.Equal("Aaron Active", member.FullName);
        Assert.Equal("AtWork", member.Status);
        Assert.False(string.IsNullOrWhiteSpace(member.WorkEmail));
    }

    [Fact]
    public async Task Get_MyTeam_Excludes_Indirect_Reports_Unless_IncludeIndirect_Set()
    {
        var (admin, companyId, refData) = await ContextAsync();
        var managerId = await CreateEmployeeAsync(admin, companyId, refData, "Tina", "Top");
        var leadId = await CreateEmployeeAsync(admin, companyId, refData, "Leo", "Lead");
        var reportId = await CreateEmployeeAsync(admin, companyId, refData, "Ravi", "Report");

        await ActivateAsync(admin, companyId, leadId);
        await ActivateAsync(admin, companyId, reportId);
        await AssignManagerAsync(admin, companyId, leadId, managerId);
        await AssignManagerAsync(admin, companyId, reportId, leadId);

        using var client = AsEmployee(managerId, companyId);

        var direct = await client.GetFromJsonAsync<TeamPayload>(
            $"/api/companies/{companyId}/employees/me/team");
        Assert.Single(direct!.Items);
        Assert.Equal(leadId, direct.Items[0].EmployeeId);

        var all = await client.GetFromJsonAsync<TeamPayload>(
            $"/api/companies/{companyId}/employees/me/team?includeIndirect=true");
        Assert.Equal(2, all!.Items.Count);
        Assert.Contains(all.Items, i => i.EmployeeId == leadId);
        Assert.Contains(all.Items, i => i.EmployeeId == reportId);
    }

    [Fact]
    public async Task Get_MyTeam_Does_Not_Leak_Another_Managers_Team()
    {
        var (admin, companyId, refData) = await ContextAsync();
        var manager1 = await CreateEmployeeAsync(admin, companyId, refData, "One", "Manager");
        var manager2 = await CreateEmployeeAsync(admin, companyId, refData, "Two", "Manager");
        var report1 = await CreateEmployeeAsync(admin, companyId, refData, "Rep", "OfOne");
        var report2 = await CreateEmployeeAsync(admin, companyId, refData, "Rep", "OfTwo");

        await ActivateAsync(admin, companyId, report1);
        await ActivateAsync(admin, companyId, report2);
        await AssignManagerAsync(admin, companyId, report1, manager1);
        await AssignManagerAsync(admin, companyId, report2, manager2);

        using var client1 = AsEmployee(manager1, companyId);
        var team1 = await client1.GetFromJsonAsync<TeamPayload>(
            $"/api/companies/{companyId}/employees/me/team");

        var member = Assert.Single(team1!.Items);
        Assert.Equal(report1, member.EmployeeId);
        Assert.DoesNotContain(team1.Items, i => i.EmployeeId == report2);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record TeamMemberPayload(
        Guid EmployeeId,
        string FullName,
        string? JobTitle,
        string? PhoneNumber,
        string WorkEmail,
        string? ProfilePhotoUrl,
        string Status);

    private sealed record TeamPayload(List<TeamMemberPayload> Items);
}
