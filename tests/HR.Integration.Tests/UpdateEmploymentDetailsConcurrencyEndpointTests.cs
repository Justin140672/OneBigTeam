using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2: optimistic-concurrency coverage for the admin "Employment details" workflow against
// the real Postgres-backed factory. Symmetric to UpdateEmployeeProfileConcurrencyEndpointTests.
[Collection("Integration")]
public class UpdateEmploymentDetailsConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ceed0000-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("ceed0000-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("ceed0000-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("ceed0000-0000-0000-0000-000000000004");

    public UpdateEmploymentDetailsConcurrencyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            foreach (var u in new[] { User1, User2, User3, User4 })
            {
                await TestRoleSeeder.AssignRoleAsync(factory, u, SystemRoles.HrAdministrator);
                await TestRoleSeeder.AssignRoleAsync(factory, u, SystemRoles.Employee);
            }
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_Employment_With_Stale_ExpectedVersion_Returns_409_Concurrency()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User1);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);

        var version = (await GetEmployeeAsync(client, companyId, employee.Id)).Version;

        var first = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            EmploymentBody(companyId, employee.Id, "EMP-FIRST", notes: "first", expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var stale = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            EmploymentBody(companyId, employee.Id, "EMP-STALE", notes: "stale", expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await stale.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", body!.Code);
    }

    [Fact]
    public async Task Profile_Save_Then_Stale_Employment_Save_Returns_409_Concurrency()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User2);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);

        var version = (await GetEmployeeAsync(client, companyId, employee.Id)).Version;

        var profile = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/profile",
            new
            {
                companyId,
                id = employee.Id,
                firstName = "ProfileFirst",
                lastName = "Smith",
                workEmail = $"pf.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-15",
                expectedVersion = version
            });
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);

        var employment = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            EmploymentBody(companyId, employee.Id, "EMP-AFTER", notes: "after", expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, employment.StatusCode);
        var body = await employment.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", body!.Code);
    }

    [Fact]
    public async Task Put_Employment_Happy_Path_Bumps_Version()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User3);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);

        var version = (await GetEmployeeAsync(client, companyId, employee.Id)).Version;

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            EmploymentBody(companyId, employee.Id, "EMP-HAPPY", notes: "happy", expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmploymentPayload>();
        Assert.Equal(version + 1, payload!.Version);

        // And another save with the returned version also succeeds.
        var next = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            EmploymentBody(companyId, employee.Id, "EMP-HAPPY2", notes: "happy2", expectedVersion: payload.Version));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User4);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);

        var versionBefore = (await GetEmployeeAsync(client, companyId, employee.Id)).Version;

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            EmploymentBody(companyId, employee.Id, "EMP-NV1", notes: "nv1", expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        Assert.Equal(versionBefore, (await GetEmployeeAsync(client, companyId, employee.Id)).Version);
    }

    [Fact]
    public async Task Put_Employment_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/employment",
            EmploymentBody(Guid.NewGuid(), Guid.NewGuid(), "EMP-001", notes: null, expectedVersion: 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employment_Returns_404_For_Unknown_Employee()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(User1);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/employment",
            EmploymentBody(companyId, Guid.NewGuid(), "EMP-GHOST", notes: null, expectedVersion: 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("not_found", body!.Code);
    }

    [Fact]
    public async Task Put_Employment_Returns_400_For_Validation_Failure()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User2);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            EmploymentBody(companyId, employee.Id, employeeNumber: "", notes: null, expectedVersion: null));

        // FastEndpoints request-validation failures surface as 422, not 400.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
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

    private async Task<(HttpClient Client, Guid CompanyId, EmployeeRef Employee)> CreateEmployeeAsync(Guid userId)
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Test", "Employee", $"test.{Guid.NewGuid():N}@example.com",
                startDate: new DateOnly(2026, 1, 15), gender: "Male"));
        response.EnsureSuccessStatusCode();
        var employee = (await response.Content.ReadFromJsonAsync<EmployeeRef>())!;
        return (client, companyId, employee);
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
    private sealed record EmploymentPayload(Guid Id, string? EmployeeNumber, string Status, int Version);
}
