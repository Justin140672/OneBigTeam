using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class CancelLeavingProcessEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ffffffff-4000-0000-0000-000000000001");
    private static readonly Guid User2 = new("ffffffff-4000-0000-0000-000000000002");
    private static readonly Guid User3 = new("ffffffff-4000-0000-0000-000000000003");
    private static readonly Guid User4 = new("ffffffff-4000-0000-0000-000000000004");
    private static readonly Guid EmployeeRoleUser = new("ffffffff-4000-0000-0000-000000000005");
    private static readonly Guid ManagerRoleUser = new("ffffffff-4000-0000-0000-000000000006");

    public CancelLeavingProcessEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User4, SystemRoles.HrAdministrator);
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
                companyId, refData, "Cancel", "Employee", $"cancel.{Guid.NewGuid():N}@example.com"));
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
    public async Task Post_CancelLeavingProcess_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel",
            new { companyId, employeeId, cancellationReason = "Employee retracted resignation." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_CancelLeavingProcess_Cancels_Process_And_Reactivates_Employee()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel",
            new { companyId, employeeId, cancellationReason = "Employee retracted resignation." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CancelLeavingProcessPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal("Cancelled", payload.Status);
        // StartLeavingProcessHandler (slice 5) always auto-triggers offboarding via
        // IOffboardingPlanCoordinator.StartAsync immediately after starting a leaving process, so
        // by the time this test cancels it, an offboarding plan already exists and its outstanding
        // tasks are expected to have been cancelled too.
        Assert.True(payload.OffboardingTasksCancelled);

        var getLeavingProcessResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process");
        getLeavingProcessResponse.EnsureSuccessStatusCode();
        var leavingProcessPayload = await getLeavingProcessResponse.Content.ReadFromJsonAsync<GetLeavingProcessPayload>();
        Assert.NotNull(leavingProcessPayload);
        Assert.Equal("Cancelled", leavingProcessPayload!.Status);

        var employeeResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}");
        employeeResponse.EnsureSuccessStatusCode();
        var employeePayload = await employeeResponse.Content.ReadFromJsonAsync<EmployeeStatusPayload>();
        Assert.NotNull(employeePayload);
        Assert.Equal("Active", employeePayload!.Status);
    }

    [Fact]
    public async Task Post_CancelLeavingProcess_Returns_NotFound_When_No_InProgress_LeavingProcess_Exists()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel",
            new { companyId, employeeId, cancellationReason = "Employee retracted resignation." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_CancelLeavingProcess_Returns_UnprocessableEntity_When_CancellationReason_Is_Empty()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel",
            new { companyId, employeeId, cancellationReason = "" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Post_CancelLeavingProcess_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{otherCompanyId}/employees/{employeeId}/leaving-process/cancel",
            new { companyId = otherCompanyId, employeeId, cancellationReason = "Employee retracted resignation." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // CancelLeavingProcess is gated by the employee:manage policy (HrAdministrator only) —
    // Employee and Manager roles must be rejected with 403.
    [Fact]
    public async Task Post_CancelLeavingProcess_Returns_Forbidden_For_Employee_Role()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeRoleUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel",
            new { companyId, employeeId, cancellationReason = "Employee retracted resignation." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_CancelLeavingProcess_Returns_Forbidden_For_Manager_Role()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ManagerRoleUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel",
            new { companyId, employeeId, cancellationReason = "Employee retracted resignation." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record CancelLeavingProcessPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        string Status,
        bool OffboardingTasksCancelled);

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

    private sealed record EmployeeStatusPayload(string Status);
}
