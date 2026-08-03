using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class StartLeavingProcessEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ffffffff-1000-0000-0000-000000000001");
    private static readonly Guid User2 = new("ffffffff-1000-0000-0000-000000000002");
    private static readonly Guid User3 = new("ffffffff-1000-0000-0000-000000000003");
    private static readonly Guid User4 = new("ffffffff-1000-0000-0000-000000000004");
    private static readonly Guid User5 = new("ffffffff-1000-0000-0000-000000000005");
    private static readonly Guid User6 = new("ffffffff-1000-0000-0000-000000000006");
    private static readonly Guid User7 = new("ffffffff-1000-0000-0000-000000000009");
    private static readonly Guid User8 = new("ffffffff-1000-0000-0000-00000000000a");
    private static readonly Guid EmployeeRoleUser = new("ffffffff-1000-0000-0000-000000000007");
    private static readonly Guid ManagerRoleUser = new("ffffffff-1000-0000-0000-000000000008");

    // Relative to "today" rather than hardcoded literals ("2026-07-01"/"2026-08-01" etc.) — those
    // were comfortably in the future when this file was written, but StartLeavingProcessHandler
    // treats any LeavingDate before "today" as backdated (requiring explicit confirmation), so a
    // fixed near-term literal silently starts failing once real time catches up to it. Computed
    // once per test run so every test in this file agrees on the same non-backdated window.
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly ResignationReceivedDate = Today.AddDays(-14);
    private static readonly DateOnly LeavingDate = Today.AddDays(30);
    private static readonly DateOnly LastWorkingDay = LeavingDate.AddDays(-1);
    private static string ResignationReceivedDateString => ResignationReceivedDate.ToString("yyyy-MM-dd");
    private static string LeavingDateString => LeavingDate.ToString("yyyy-MM-dd");
    private static string LastWorkingDayString => LastWorkingDay.ToString("yyyy-MM-dd");

    public StartLeavingProcessEndpointTests(ApiWebApplicationFactory factory)
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
            await TestRoleSeeder.AssignRoleAsync(factory, User7, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User7, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, User8, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User8, SystemRoles.Employee);
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
                companyId, refData, "Leaving", "Employee", $"leaving.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    [Fact]
    public async Task Post_LeavingProcess_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = ResignationReceivedDateString,
                leavingDate = LeavingDateString,
                lastWorkingDay = LastWorkingDayString,
                leavingReason = "Resignation"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_LeavingProcess_Creates_LeavingProcess_And_Is_Retrievable()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = ResignationReceivedDateString,
                leavingDate = LeavingDateString,
                lastWorkingDay = LastWorkingDayString,
                leavingReason = "Resignation"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<LeavingProcessPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal(ResignationReceivedDate, payload.ResignationReceivedDate);
        Assert.Equal(LeavingDate, payload.LeavingDate);
        Assert.Equal(LastWorkingDay, payload.LastWorkingDay);
        Assert.Equal("Resignation", payload.LeavingReason);
        Assert.Equal("InProgress", payload.Status);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leaving-process");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getPayload = await getResponse.Content.ReadFromJsonAsync<GetLeavingProcessPayload>();
        Assert.NotNull(getPayload);
        Assert.Equal(payload.Id, getPayload!.Id);
        Assert.Equal("InProgress", getPayload.Status);
    }

    [Fact]
    public async Task Post_LeavingProcess_Returns_Conflict_When_Already_InProgress()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = ResignationReceivedDateString,
                leavingDate = LeavingDateString,
                lastWorkingDay = LastWorkingDayString,
                leavingReason = "Resignation"
            });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = ResignationReceivedDateString,
                leavingDate = LeavingDate.AddDays(4).ToString("yyyy-MM-dd"),
                lastWorkingDay = LeavingDate.AddDays(3).ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_LeavingProcess_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = ResignationReceivedDateString,
                leavingDate = LeavingDateString,
                lastWorkingDay = LastWorkingDayString,
                leavingReason = "Resignation"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_LeavingProcess_Returns_UnprocessableEntity_When_LastWorkingDay_After_LeavingDate()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = ResignationReceivedDateString,
                leavingDate = LeavingDateString,
                lastWorkingDay = LeavingDate.AddDays(1).ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_LeavingProcess_Returns_UnprocessableEntity_When_LeavingReason_Is_Invalid()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User5.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);

        // An out-of-range integer (rather than an unparseable string) is used here so the
        // request still successfully model-binds to the LeavingReason enum — deserializing an
        // unrecognized *string* fails at JSON binding time (400 Bad Request, before FluentValidation
        // ever runs), whereas an out-of-range *integer* binds fine and is only rejected by the
        // validator's IsInEnum() rule, which is what this test is actually meant to exercise (422).
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = ResignationReceivedDateString,
                leavingDate = LeavingDateString,
                lastWorkingDay = LastWorkingDayString,
                leavingReason = 999
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_LeavingProcess_Sets_Employee_Status_To_Leaving()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User6.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = ResignationReceivedDateString,
                leavingDate = LeavingDateString,
                lastWorkingDay = LastWorkingDayString,
                leavingReason = "Resignation"
            });
        response.EnsureSuccessStatusCode();

        var employeeResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}");
        employeeResponse.EnsureSuccessStatusCode();
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeeStatusPayload>();

        Assert.NotNull(employee);
        Assert.Equal("Leaving", employee!.Status);
        Assert.True(employee.ShowLeavingTab);
    }

    // StartLeavingProcess is gated by the employee:manage policy (HrAdministrator only) — Employee
    // and Manager roles must be rejected with 403, same regression class CompanyAuthorizationTests
    // guards for company:manage.
    [Fact]
    public async Task Post_LeavingProcess_Returns_Forbidden_For_Employee_Role()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeRoleUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = ResignationReceivedDateString,
                leavingDate = LeavingDateString,
                lastWorkingDay = LastWorkingDayString,
                leavingReason = "Resignation"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_LeavingProcess_Returns_Forbidden_For_Manager_Role()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerRoleUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = ResignationReceivedDateString,
                leavingDate = LeavingDateString,
                lastWorkingDay = LastWorkingDayString,
                leavingReason = "Resignation"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_LeavingProcess_Returns_Conflict_When_LeavingDate_Is_Backdated_And_Not_Confirmed()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User7.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = "2019-12-01",
                leavingDate = "2020-01-01",
                lastWorkingDay = "2019-12-31",
                leavingReason = "Resignation"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.NotNull(payload);
        Assert.Contains("Confirm to backdate", payload!.Error);

        var getResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/leaving-process");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Post_LeavingProcess_Finalises_Employee_Departure_When_LeavingDate_Is_Backdated_And_Confirmed()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User8.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = "2019-12-01",
                leavingDate = "2020-01-01",
                lastWorkingDay = "2019-12-31",
                leavingReason = "Resignation",
                confirmBackdatedLeavingDate = true
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LeavingProcessPayload>();
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

    private sealed record EmployeeStatusPayload(string Status, bool ShowLeavingTab);

    private sealed record LeavingProcessPayload(
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
        DateTimeOffset StartedAt);

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
