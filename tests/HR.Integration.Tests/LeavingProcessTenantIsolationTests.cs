using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Proves an Employee Leaving Process in one company can never be read or mutated by another
/// company's caller, even when that caller supplies Company A's real employee id while
/// authenticated (with a matching route/tenant header) as Company B — mirrors the reasoning in
/// RecruitmentPositionProfileTenantIsolationTests, but for the four leaving-process endpoints.
///
/// This is distinct from the existing "Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant"
/// tests in the individual *EndpointTests.cs files, which exercise TenantRouteAuthorizationMiddleware
/// rejecting a route/header mismatch before the handler ever runs. Here the route and tenant header
/// always agree (both Company B) — isolation has to come from the handlers' own CompanyId-scoped
/// queries, which is what these tests actually verify.
/// </summary>
public class LeavingProcessTenantIsolationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUser = new("dd000001-0000-0000-0000-000000000001");

    public LeavingProcessTenantIsolationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Isolated", "Employee", $"isolated.{Guid.NewGuid():N}@example.com"));
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
    public async Task Get_LeavingProcess_For_Company_As_Employee_From_Company_B_Returns_NotFound()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = AuthenticatedClient(companyA);
        var employeeAId = await CreateEmployeeAsync(clientA, companyA);
        await StartLeavingProcessAsync(clientA, companyA, employeeAId);

        // Authenticated as Company B (route and tenant header both companyB — no middleware
        // rejection), but the employee id belongs to Company A's leaving process.
        using var clientB = AuthenticatedClient(companyB);
        var response = await clientB.GetAsync($"/api/companies/{companyB}/employees/{employeeAId}/leaving-process");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Start_LeavingProcess_For_Company_As_Employee_From_Company_B_Returns_NotFound()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = AuthenticatedClient(companyA);
        var employeeAId = await CreateEmployeeAsync(clientA, companyA);

        using var clientB = AuthenticatedClient(companyB);
        var response = await clientB.PostAsJsonAsync(
            $"/api/companies/{companyB}/employees/{employeeAId}/leaving-process",
            new
            {
                companyId = companyB,
                employeeId = employeeAId,
                resignationReceivedDate = "2026-07-01",
                leavingDate = "2026-08-01",
                lastWorkingDay = "2026-07-31",
                leavingReason = "Resignation"
            });

        // StartLeavingProcessHandler looks up the employee scoped by (Id, CompanyId) — Company A's
        // employee doesn't exist under Company B, so this must never succeed against Company A's data.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Amend_LeavingProcess_For_Company_As_Employee_From_Company_B_Returns_NotFound()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = AuthenticatedClient(companyA);
        var employeeAId = await CreateEmployeeAsync(clientA, companyA);
        await StartLeavingProcessAsync(clientA, companyA, employeeAId);

        using var clientB = AuthenticatedClient(companyB);
        var response = await clientB.PutAsJsonAsync(
            $"/api/companies/{companyB}/employees/{employeeAId}/leaving-process",
            new
            {
                companyId = companyB,
                employeeId = employeeAId,
                leavingDate = "2026-09-01",
                lastWorkingDay = "2026-08-31",
                leavingReason = "MutualAgreement"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Confirm Company A's leaving process was genuinely untouched by the rejected attempt.
        var getResponse = await clientA.GetAsync($"/api/companies/{companyA}/employees/{employeeAId}/leaving-process");
        getResponse.EnsureSuccessStatusCode();
        var payload = await getResponse.Content.ReadFromJsonAsync<GetLeavingProcessPayload>();
        Assert.NotNull(payload);
        Assert.Equal(new DateOnly(2026, 8, 1), payload!.LeavingDate);
        Assert.Equal("Resignation", payload.LeavingReason);
    }

    [Fact]
    public async Task Cancel_LeavingProcess_For_Company_As_Employee_From_Company_B_Returns_NotFound()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = AuthenticatedClient(companyA);
        var employeeAId = await CreateEmployeeAsync(clientA, companyA);
        await StartLeavingProcessAsync(clientA, companyA, employeeAId);

        using var clientB = AuthenticatedClient(companyB);
        var response = await clientB.PostAsJsonAsync(
            $"/api/companies/{companyB}/employees/{employeeAId}/leaving-process/cancel",
            new { companyId = companyB, employeeId = employeeAId, cancellationReason = "Attempted cross-tenant cancellation." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Confirm Company A's leaving process is still InProgress — the rejected Company B
        // attempt must not have cancelled it nor reactivated Company A's employee.
        var getResponse = await clientA.GetAsync($"/api/companies/{companyA}/employees/{employeeAId}/leaving-process");
        getResponse.EnsureSuccessStatusCode();
        var payload = await getResponse.Content.ReadFromJsonAsync<GetLeavingProcessPayload>();
        Assert.NotNull(payload);
        Assert.Equal("InProgress", payload!.Status);
    }

    private sealed record IdPayload(Guid Id);

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
