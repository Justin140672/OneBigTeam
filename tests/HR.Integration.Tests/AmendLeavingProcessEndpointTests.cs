using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class AmendLeavingProcessEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ffffffff-3000-0000-0000-000000000001");
    private static readonly Guid User2 = new("ffffffff-3000-0000-0000-000000000002");
    private static readonly Guid User3 = new("ffffffff-3000-0000-0000-000000000003");
    private static readonly Guid User4 = new("ffffffff-3000-0000-0000-000000000004");
    private static readonly Guid User5 = new("ffffffff-3000-0000-0000-000000000007");
    private static readonly Guid User6 = new("ffffffff-3000-0000-0000-000000000008");
    private static readonly Guid EmployeeRoleUser = new("ffffffff-3000-0000-0000-000000000005");
    private static readonly Guid ManagerRoleUser = new("ffffffff-3000-0000-0000-000000000006");

    public AmendLeavingProcessEndpointTests(ApiWebApplicationFactory factory)
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
            await TestRoleSeeder.AssignRoleAsync(factory, User4, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User4, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, User5, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User5, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, User6, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User6, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeRoleUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerRoleUser, SystemRoles.Manager);
        }).GetAwaiter().GetResult();
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Amend", "Employee", $"amend.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static async Task StartLeavingProcessAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = "2026-07-01",
                leavingDate = "2026-08-01",
                lastWorkingDay = "2026-07-31",
                leavingReason = "Resignation"
            });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Put_LeavingProcess_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                leavingDate = "2026-09-01",
                lastWorkingDay = "2026-08-31",
                leavingReason = "MutualAgreement"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_LeavingProcess_Amends_LeavingDate_LastWorkingDay_And_Reason()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                leavingDate = "2026-09-01",
                lastWorkingDay = "2026-08-31",
                leavingReason = "MutualAgreement"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AmendLeavingProcessPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal(new DateOnly(2026, 9, 1), payload.LeavingDate);
        Assert.Equal(new DateOnly(2026, 8, 31), payload.LastWorkingDay);
        Assert.Equal("MutualAgreement", payload.LeavingReason);
        Assert.Equal("InProgress", payload.Status);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leaving-process");
        getResponse.EnsureSuccessStatusCode();
        var getPayload = await getResponse.Content.ReadFromJsonAsync<GetLeavingProcessPayload>();
        Assert.NotNull(getPayload);
        Assert.Equal(new DateOnly(2026, 9, 1), getPayload!.LeavingDate);
        Assert.Equal(new DateOnly(2026, 8, 31), getPayload.LastWorkingDay);
        Assert.Equal("MutualAgreement", getPayload.LeavingReason);
    }

    [Fact]
    public async Task Put_LeavingProcess_Returns_NotFound_When_No_InProgress_LeavingProcess_Exists()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                leavingDate = "2026-09-01",
                lastWorkingDay = "2026-08-31",
                leavingReason = "MutualAgreement"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_LeavingProcess_Returns_UnprocessableEntity_When_LastWorkingDay_After_LeavingDate()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                leavingDate = "2026-08-31",
                lastWorkingDay = "2026-09-01",
                leavingReason = "MutualAgreement"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_LeavingProcess_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{otherCompanyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId = otherCompanyId,
                employeeId,
                leavingDate = "2026-09-01",
                lastWorkingDay = "2026-08-31",
                leavingReason = "MutualAgreement"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // AmendLeavingProcess is gated by the employee:manage policy (HrAdministrator only) — Employee
    // and Manager roles must be rejected with 403.
    [Fact]
    public async Task Put_LeavingProcess_Returns_Forbidden_For_Employee_Role()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeRoleUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                leavingDate = "2026-09-01",
                lastWorkingDay = "2026-08-31",
                leavingReason = "MutualAgreement"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_LeavingProcess_Returns_Forbidden_For_Manager_Role()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerRoleUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                leavingDate = "2026-09-01",
                lastWorkingDay = "2026-08-31",
                leavingReason = "MutualAgreement"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_LeavingProcess_Returns_Conflict_When_LeavingDate_Is_Backdated_And_Not_Confirmed()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User5.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                leavingDate = "2020-01-01",
                lastWorkingDay = "2019-12-31",
                leavingReason = "MutualAgreement"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.NotNull(payload);
        Assert.Contains("Confirm to backdate", payload!.Error);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leaving-process");
        getResponse.EnsureSuccessStatusCode();
        var getPayload = await getResponse.Content.ReadFromJsonAsync<GetLeavingProcessPayload>();
        Assert.NotNull(getPayload);
        Assert.Equal(new DateOnly(2026, 8, 1), getPayload!.LeavingDate);
        Assert.Equal("InProgress", getPayload.Status);
    }

    [Fact]
    public async Task Put_LeavingProcess_Finalises_Employee_Departure_When_LeavingDate_Is_Backdated_And_Confirmed()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User6.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                leavingDate = "2020-01-01",
                lastWorkingDay = "2019-12-31",
                leavingReason = "MutualAgreement",
                confirmBackdatedLeavingDate = true
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AmendLeavingProcessPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Completed", payload!.Status);

        var employeeResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}");
        employeeResponse.EnsureSuccessStatusCode();
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeeStatusPayload>();
        Assert.NotNull(employee);
        Assert.Equal("FormerEmployee", employee!.Status);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record ErrorPayload(string Error);

    private sealed record EmployeeStatusPayload(string Status);

    private sealed record AmendLeavingProcessPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        DateOnly ResignationReceivedDate,
        DateOnly LeavingDate,
        DateOnly LastWorkingDay,
        string NoticePeriodUnit,
        int NoticePeriodLength,
        string NoticeSource,
        string LeavingReason,
        string Status,
        bool OffboardingAlreadyStarted);

    private sealed record GetLeavingProcessPayload(
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
