using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2: the shared HR.SharedKernel.VersionAdvancingSaveChangesInterceptor advances
// IVersionedAggregate.Version on ANY modified versioned entity, even for writers that use plain
// SaveChangesAsync (AssignManager) rather than SaveChangesWithConcurrencyAsync. This locks in that
// a manager assignment bumps the Employee version and therefore invalidates a stale employment save.
[Collection("Integration")]
public class EmployeeVersionAdvanceOnManagerAssignmentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("aade0000-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("aade0000-0000-0000-0000-000000000002");

    public EmployeeVersionAdvanceOnManagerAssignmentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            foreach (var u in new[] { User1, User2 })
            {
                await TestRoleSeeder.AssignRoleAsync(factory, u, SystemRoles.HrAdministrator);
                await TestRoleSeeder.AssignRoleAsync(factory, u, SystemRoles.Employee);
            }
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Assign_Manager_Advances_Employee_Version_Making_A_Stale_Employment_Save_Conflict()
    {
        var (client, companyId) = await NewCompanyClientAsync(User1);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);

        var employeeE = await CreateEmployeeAsync(client, companyId, "Edith", "Employee");
        var managerM = await CreateEmployeeAsync(client, companyId, "Marcus", "Manager");

        var v0 = (await GetEmployeeAsync(client, companyId, employeeE.Id)).Version;

        var assign = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeE.Id}/manager",
            new { companyId, id = employeeE.Id, managerId = managerM.Id });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        var v1 = (await GetEmployeeAsync(client, companyId, employeeE.Id)).Version;
        Assert.Equal(v0 + 1, v1);

        var stale = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeE.Id}/employment",
            EmploymentBody(companyId, employeeE.Id, "EMP-STALE", notes: "stale", expectedVersion: v0));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await stale.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", body!.Code);

        var fresh = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeE.Id}/employment",
            EmploymentBody(companyId, employeeE.Id, "EMP-FRESH", notes: "fresh", expectedVersion: v1));
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Fact]
    public async Task Assign_Manager_Happy_Path_Returns_Ok()
    {
        var (client, companyId) = await NewCompanyClientAsync(User2);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);

        var employeeE = await CreateEmployeeAsync(client, companyId, "Ada", "Report");
        var managerM = await CreateEmployeeAsync(client, companyId, "Mabel", "Boss");

        var assign = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeE.Id}/manager",
            new { companyId, id = employeeE.Id, managerId = managerM.Id });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        using var anonymous = _factory.CreateClient();
        var unauthorized = await anonymous.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeE.Id}/manager",
            new { companyId, id = employeeE.Id, managerId = managerM.Id });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    private static object EmploymentBody(Guid companyId, Guid id, string employeeNumber, string? notes, int? expectedVersion)
        => new
        {
            companyId,
            id,
            employeeNumber,
            employmentTypeId = (Guid?)null,
            status = "Active",
            startDate = "2026-01-15",
            notes,
            expectedVersion
        };

    private async Task<(HttpClient Client, Guid CompanyId)> NewCompanyClientAsync(Guid userId)
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        return (client, companyId);
    }

    private static async Task<EmployeeRef> CreateEmployeeAsync(HttpClient client, Guid companyId, string firstName, string lastName)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, firstName, lastName, $"test.{Guid.NewGuid():N}@example.com",
                startDate: new DateOnly(2026, 1, 15), gender: "Male"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeeRef>())!;
    }

    private static async Task<EmployeeSnapshot> GetEmployeeAsync(HttpClient client, Guid companyId, Guid id)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeeSnapshot>())!;
    }

    private sealed record EmployeeRef(Guid Id);
    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record EmployeeSnapshot(Guid Id, int Version);
}
