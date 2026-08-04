using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetLeavingProcessEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ffffffff-2000-0000-0000-000000000001");
    private static readonly Guid User2 = new("ffffffff-2000-0000-0000-000000000002");
    private static readonly Guid User3 = new("ffffffff-2000-0000-0000-000000000003");

    public GetLeavingProcessEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Leaving", "Employee", $"leaving.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    // Relative to "today" rather than hardcoded literals — see StartLeavingProcessEndpointTests'
    // identical fields for why a fixed near-term literal eventually becomes "backdated".
    private static readonly DateOnly LeavingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    private static readonly DateOnly LastWorkingDay = LeavingDate.AddDays(-1);

    private static async Task StartLeavingProcessAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = LeavingDate.AddDays(-30).ToString("yyyy-MM-dd"),
                leavingDate = LeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = LastWorkingDay.ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Get_LeavingProcess_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/leaving-process");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeavingProcess_Returns_NotFound_When_None_Exists()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leaving-process");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_LeavingProcess_Returns_LeavingProcess_After_Started()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User2, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leaving-process");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LeavingProcessPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(LeavingDate.AddDays(-30), payload.ResignationReceivedDate);
        Assert.Equal(LeavingDate, payload.LeavingDate);
        Assert.Equal(LastWorkingDay, payload.LastWorkingDay);
        Assert.Equal("Resignation", payload.LeavingReason);
        Assert.Equal("InProgress", payload.Status);
    }

    [Fact]
    public async Task Get_LeavingProcess_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User3, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        // Authenticated as companyId but the route targets otherCompanyId —
        // TenantRouteAuthorizationMiddleware blocks it before the handler ever runs.
        var response = await client.GetAsync($"/api/companies/{otherCompanyId}/employees/{employeeId}/leaving-process");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Unlike StartLeavingProcess/AmendLeavingProcess/CancelLeavingProcess (all gated by the
    // employee:manage policy — HrAdministrator only), GetLeavingProcess is gated by the broader
    // "role:employee" policy (see IdentityModule.AddRolePolicies) — every real user always carries
    // the Employee role (AcceptInvite always assigns it), so this is still broad read access, just
    // no longer satisfied by an authenticated-but-completely-role-less caller. This test locks in
    // that a caller with zero assigned roles at all must now get 403, not 200.
    [Fact]
    public async Task Get_LeavingProcess_Returns_Forbidden_For_User_With_No_Roles()
    {
        using var hrAdminClient = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        hrAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        hrAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);
        await StartLeavingProcessAsync(hrAdminClient, companyId, employeeId);

        // A brand-new user id with no TestRoleSeeder role assignment at all — still authenticated
        // (TestAuthHandler only requires the user header), just role-less.
        using var noRoleClient = _factory.CreateClient();
        noRoleClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        noRoleClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await noRoleClient.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record LeavingProcessPayload(
        Guid Id,
        DateOnly ResignationReceivedDate,
        DateOnly LeavingDate,
        DateOnly LastWorkingDay,
        string NoticePeriodUnit,
        int NoticePeriodLength,
        string NoticeSource,
        string LeavingReason,
        string Status);
}
