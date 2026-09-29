using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class LeavingProcessTenantIsolationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUser = new("dd000001-0000-0000-0000-000000000001");

    private static readonly DateOnly LeavingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    private static readonly DateOnly LastWorkingDay = LeavingDate.AddDays(-1);

    public LeavingProcessTenantIsolationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, HrAdminUser, SystemRoles.HrAdministrator, companyId);
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
                resignationReceivedDate = LeavingDate.AddDays(-30).ToString("yyyy-MM-dd"),
                leavingDate = LeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = LastWorkingDay.ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Get_LeavingProcess_For_Company_As_Employee_From_Company_B_Returns_NotFound()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = await AuthenticatedClient(companyA);
        var employeeAId = await CreateEmployeeAsync(clientA, companyA);
        await StartLeavingProcessAsync(clientA, companyA, employeeAId);

        using var clientB = await AuthenticatedClient(companyB);
        var response = await clientB.GetAsync($"/api/companies/{companyB}/employees/{employeeAId}/leaving-process");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Start_LeavingProcess_For_Company_As_Employee_From_Company_B_Returns_NotFound()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = await AuthenticatedClient(companyA);
        var employeeAId = await CreateEmployeeAsync(clientA, companyA);

        using var clientB = await AuthenticatedClient(companyB);
        var response = await clientB.PostAsJsonAsync(
            $"/api/companies/{companyB}/employees/{employeeAId}/leaving-process",
            new
            {
                companyId = companyB,
                employeeId = employeeAId,
                resignationReceivedDate = LeavingDate.AddDays(-30).ToString("yyyy-MM-dd"),
                leavingDate = LeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = LastWorkingDay.ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Amend_LeavingProcess_For_Company_As_Employee_From_Company_B_Returns_NotFound()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = await AuthenticatedClient(companyA);
        var employeeAId = await CreateEmployeeAsync(clientA, companyA);
        await StartLeavingProcessAsync(clientA, companyA, employeeAId);

        using var clientB = await AuthenticatedClient(companyB);
        var response = await clientB.PutAsJsonAsync(
            $"/api/companies/{companyB}/employees/{employeeAId}/leaving-process",
            new
            {
                companyId = companyB,
                employeeId = employeeAId,
                leavingDate = LeavingDate.AddDays(31).ToString("yyyy-MM-dd"),
                lastWorkingDay = LeavingDate.AddDays(30).ToString("yyyy-MM-dd"),
                leavingReason = "MutualAgreement",
                // Ticket 2: amend now requires a loaded version; supply one so the request clears
                // validation and the tenant-scoping 404 is what's actually asserted.
                expectedVersion = 1
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Confirm Company A's leaving process was genuinely untouched by the rejected attempt.
        var getResponse = await clientA.GetAsync($"/api/companies/{companyA}/employees/{employeeAId}/leaving-process");
        getResponse.EnsureSuccessStatusCode();
        var payload = await getResponse.Content.ReadFromJsonAsync<GetLeavingProcessPayload>();
        Assert.NotNull(payload);
        Assert.Equal(LeavingDate, payload!.LeavingDate);
        Assert.Equal("Resignation", payload.LeavingReason);
    }

    [Fact]
    public async Task Cancel_LeavingProcess_For_Company_As_Employee_From_Company_B_Returns_NotFound()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using var clientA = await AuthenticatedClient(companyA);
        var employeeAId = await CreateEmployeeAsync(clientA, companyA);
        await StartLeavingProcessAsync(clientA, companyA, employeeAId);

        using var clientB = await AuthenticatedClient(companyB);
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
