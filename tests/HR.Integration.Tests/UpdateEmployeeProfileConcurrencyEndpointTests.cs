using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2: optimistic-concurrency coverage for the admin "Edit employee" workflows, exercised
// against the real Postgres-backed ApiWebApplicationFactory where the Employee.version
// concurrency token is genuinely enforced by the database (unlike the EF-InMemory handler tests
// in HR.Modules.Employees.Tests/EmployeeConcurrencyHandlerTests.cs).
[Collection("Integration")]
public class UpdateEmployeeProfileConcurrencyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ceec0000-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("ceec0000-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("ceec0000-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("ceec0000-0000-0000-0000-000000000004");

    public UpdateEmployeeProfileConcurrencyEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Put_Profile_With_ExpectedVersion_Rejects_Second_Stale_Save_With_409_Concurrency()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User1);

        var loaded = await GetEmployeeAsync(client, companyId, employee.Id);
        var version = loaded.Version;

        var firstEmail = $"first.{Guid.NewGuid():N}@example.com";
        var first = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/profile",
            ProfileBody(companyId, employee.Id, firstName: "FirstWrite", workEmail: firstEmail, expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var stale = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/profile",
            ProfileBody(companyId, employee.Id, firstName: "StaleWrite", workEmail: $"stale.{Guid.NewGuid():N}@example.com", expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await stale.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", body!.Code);

        var after = await GetEmployeeAsync(client, companyId, employee.Id);
        Assert.Equal("FirstWrite", after.FirstName);
        Assert.Equal(firstEmail, after.WorkEmail);
    }

    [Fact]
    public async Task Two_Editors_Employment_Save_Then_Stale_Profile_Save_Returns_409_Concurrency()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User2);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);

        var loaded = await GetEmployeeAsync(client, companyId, employee.Id);
        var version = loaded.Version;

        // Editor B saves employment details against the shared Employee.Version token.
        var editorB = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/employment",
            new
            {
                companyId,
                id = employee.Id,
                employeeNumber = "EMP-XSCREEN",
                employmentTypeId = (Guid?)null,
                status = "Active",
                startDate = "2026-01-15",
                expectedVersion = version
            });
        Assert.Equal(HttpStatusCode.OK, editorB.StatusCode);

        // Editor A, still on the old screen, saves the profile with the now-stale version.
        var editorA = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/profile",
            ProfileBody(companyId, employee.Id, firstName: "EditorA", workEmail: $"a.{Guid.NewGuid():N}@example.com", expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, editorA.StatusCode);
        var body = await editorA.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", body!.Code);
    }

    [Fact]
    public async Task Put_Profile_Without_ExpectedVersion_Returns_422_And_Writes_Nothing()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User3);

        var before = await GetEmployeeAsync(client, companyId, employee.Id);

        var r1 = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/profile",
            ProfileBody(companyId, employee.Id, firstName: "NoVersion1", workEmail: $"nv1.{Guid.NewGuid():N}@example.com", expectedVersion: null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);

        var after = await GetEmployeeAsync(client, companyId, employee.Id);
        Assert.Equal(before.FirstName, after.FirstName);
        Assert.Equal(before.Version, after.Version);
    }

    [Fact]
    public async Task Put_Profile_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/profile",
            ProfileBody(Guid.NewGuid(), Guid.NewGuid(), "Alice", "alice@example.com", expectedVersion: 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Employee_Response_Includes_Version()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User4);

        var loaded = await GetEmployeeAsync(client, companyId, employee.Id);
        Assert.True(loaded.Version >= 1);
    }

    [Fact]
    public async Task Put_Profile_Returns_404_For_Unknown_Employee()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(User4);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/profile",
            ProfileBody(companyId, Guid.NewGuid(), firstName: "Ghost", workEmail: $"ghost.{Guid.NewGuid():N}@example.com", expectedVersion: 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("not_found", body!.Code);
    }

    [Fact]
    public async Task Put_Profile_Returns_400_For_Validation_Failure()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User3);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/profile",
            ProfileBody(companyId, employee.Id, firstName: "", workEmail: "not-an-email", expectedVersion: null));

        // FastEndpoints request-validation failures surface as 422, not 400.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private static object ProfileBody(Guid companyId, Guid id, string firstName, string workEmail, int? expectedVersion)
        => new
        {
            companyId,
            id,
            firstName,
            lastName = "Smith",
            workEmail,
            startDate = "2026-01-15",
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
    private sealed record EmployeeSnapshot(Guid Id, string FirstName, string LastName, string WorkEmail, int Version);
}
