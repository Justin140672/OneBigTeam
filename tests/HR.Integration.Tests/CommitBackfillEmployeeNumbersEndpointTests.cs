using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CommitBackfillEmployeeNumbersEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid User1 = new("61000000-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("61000000-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("61000000-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("61000000-0000-0000-0000-000000000004");
    private static readonly Guid User5 = new("61000000-0000-0000-0000-000000000005");

    public CommitBackfillEmployeeNumbersEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            foreach (var userId in new[] { User1, User2, User3, User4, User5 })
            {
                await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.HrAdministrator);
                await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.CompanyAdministrator);
                await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee);
            }
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    // POST /api/companies (CreateCompany) was removed in 78a43344; this now provisions the
    // company directly via CompaniesDbContext, mirroring TestRoleSeeder.EnsureActiveSubscriptionAsync.
    private async Task<Guid> CreateCompanyAsync(HttpClient client)
    {
        _ = client;
        return await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Backfill Commit Test Co {Guid.NewGuid():N}");
    }

    // Was calling PUT /api/companies/{id}/settings (UpdateCompanySettingsHandler), which only
    // persists TimeZone/Locale and silently ignores every other field in the request body
    // (including employeeNumberMode) — it still returned 200 OK. The actual employee-number/HR
    // settings live behind PUT /api/companies/{id}/hr-settings (UpdateHrSettingsHandler).
    private static async Task SetEmployeeNumberModeAsync(
        HttpClient client, Guid companyId, string mode, string? prefix = null, int nextEmployeeNumber = 1, int minimumLength = 1)
    {
        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            id = companyId,
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6,
            employeeNumberMode = mode,
            employeeNumberPrefix = prefix,
            nextEmployeeNumber,
            employeeNumberMinimumLength = minimumLength
        });
        response.EnsureSuccessStatusCode();
    }

    private static string CommitUrl(Guid companyId) =>
        $"/api/companies/{companyId}/employees/backfill-employee-numbers/commit";

    private static StringContent EmptyJson() => new("{}", System.Text.Encoding.UTF8, "application/json");

    /// <summary>
    /// Seeds an employee with a genuinely blank EmployeeNumber directly via EF — see the identical
    /// helper in PreviewBackfillEmployeeNumbersEndpointTests for why this can't be done through the
    /// CreateEmployee endpoint itself.
    /// </summary>
    private async Task<Employee> SeedEmployeeMissingNumberAsync(
        Guid companyId, HttpClient client, string firstName, string lastName, DateOnly startDate)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var now = DateTimeOffset.UtcNow;

        var employee = Employee.Create(
            Guid.NewGuid(), companyId, firstName, lastName,
            $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
            startDate, hasSystemAccess: false, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            employeeNumber: "", refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId,
            refData.PositionProfileId, now);

        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        return employee;
    }

    private async Task<string?> GetSavedEmployeeNumberAsync(Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == employeeId);
        return employee.EmployeeNumber;
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(CommitUrl(Guid.NewGuid()), EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Company_Is_In_Manual_Mode()
    {
        using var client = await AuthenticatedClient(User1, Guid.NewGuid());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        await SetEmployeeNumberModeAsync(client, companyId, "Manual");

        var response = await client.PostAsync(CommitUrl(companyId), EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Ok_With_Empty_Result_When_No_Employees_Are_Missing_A_Number()
    {
        using var client = await AuthenticatedClient(User2, Guid.NewGuid());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        await SetEmployeeNumberModeAsync(client, companyId, "Automatic", prefix: "EMP-", nextEmployeeNumber: 1, minimumLength: 3);

        var response = await client.PostAsync(CommitUrl(companyId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CommitPayload>();
        Assert.NotNull(payload);
        Assert.Equal(0, payload!.TotalCount);
        Assert.Empty(payload.Items);
    }

    [Fact]
    public async Task Assigns_Numbers_Only_To_Employees_Missing_One_And_Leaves_Existing_Numbers_Untouched()
    {
        using var client = await AuthenticatedClient(User3, Guid.NewGuid());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        // Numbered employee, created via the API while still in Manual mode.
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var numberedResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Zack", "Existing", $"zack.{Guid.NewGuid():N}@example.com",
                employeeNumber: "EMP-EXISTING", startDate: new DateOnly(2023, 1, 1)));
        numberedResponse.EnsureSuccessStatusCode();
        var numberedEmployeeId = (await numberedResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        // Two unnumbered employees, simulating pre-existing records.
        var first = await SeedEmployeeMissingNumberAsync(companyId, client, "Alice", "Smith", new DateOnly(2024, 1, 1));
        var second = await SeedEmployeeMissingNumberAsync(companyId, client, "Bob", "Jones", new DateOnly(2024, 2, 1));

        await SetEmployeeNumberModeAsync(client, companyId, "Automatic", prefix: "EMP-", nextEmployeeNumber: 100, minimumLength: 4);

        var response = await client.PostAsync(CommitUrl(companyId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CommitPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.TotalCount);
        Assert.NotEqual(Guid.Empty, payload.BackfillOperationId);
        Assert.Equal(first.Id, payload.Items[0].EmployeeId);
        Assert.Equal("EMP-0100", payload.Items[0].AssignedEmployeeNumber);
        Assert.Equal(second.Id, payload.Items[1].EmployeeId);
        Assert.Equal("EMP-0101", payload.Items[1].AssignedEmployeeNumber);

        Assert.Equal("EMP-0100", await GetSavedEmployeeNumberAsync(first.Id));
        Assert.Equal("EMP-0101", await GetSavedEmployeeNumberAsync(second.Id));
        // Pre-existing number is never touched by the backfill.
        Assert.Equal("EMP-EXISTING", await GetSavedEmployeeNumberAsync(numberedEmployeeId));

        var settingsResponse = await client.GetAsync($"/api/companies/{companyId}/hr-settings");
        settingsResponse.EnsureSuccessStatusCode();
        var settings = await settingsResponse.Content.ReadFromJsonAsync<SettingsPayload>();
        Assert.NotNull(settings);
        Assert.Equal(102, settings!.NextEmployeeNumber);
    }

    [Fact]
    public async Task Second_Commit_Call_Assigns_Nothing_Once_All_Employees_Are_Numbered()
    {
        using var client = await AuthenticatedClient(User4, Guid.NewGuid());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        await SeedEmployeeMissingNumberAsync(companyId, client, "Alice", "Smith", new DateOnly(2024, 1, 1));
        await SetEmployeeNumberModeAsync(client, companyId, "Automatic", prefix: "EMP-", nextEmployeeNumber: 1, minimumLength: 3);

        var firstCommit = await client.PostAsync(CommitUrl(companyId), EmptyJson());
        firstCommit.EnsureSuccessStatusCode();
        var firstPayload = await firstCommit.Content.ReadFromJsonAsync<CommitPayload>();
        Assert.Equal(1, firstPayload!.TotalCount);

        var secondCommit = await client.PostAsync(CommitUrl(companyId), EmptyJson());

        Assert.Equal(HttpStatusCode.OK, secondCommit.StatusCode);
        var secondPayload = await secondCommit.Content.ReadFromJsonAsync<CommitPayload>();
        Assert.NotNull(secondPayload);
        Assert.Equal(0, secondPayload!.TotalCount);
        Assert.Empty(secondPayload.Items);

        // No number was re-claimed for the already-numbered employee on the second call.
        var settingsResponse = await client.GetAsync($"/api/companies/{companyId}/hr-settings");
        settingsResponse.EnsureSuccessStatusCode();
        var settings = await settingsResponse.Content.ReadFromJsonAsync<SettingsPayload>();
        Assert.NotNull(settings);
        Assert.Equal(2, settings!.NextEmployeeNumber);
    }

    // Forcing a genuine mid-batch failure (to prove the whole-transaction rollback works, not
    // just that a successful batch commits) would require a test seam this codebase doesn't
    // currently expose — e.g. a way to make IEmployeeNumberGenerator.GenerateNextAsync throw for
    // exactly the Nth call within a real Postgres-backed integration test, or a duplicate-key
    // constraint violation engineered mid-loop. CommitBackfillEmployeeNumbersHandler's
    // transactional structure is covered here for the successful (commit) path, and the
    // handler-level unit tests in HR.Modules.Employees.Tests exercise the same code without a
    // live database; genuine rollback-on-failure is not covered by an automated test in either
    // layer and is a known gap.

    private sealed record IdPayload(Guid Id);

    private sealed record SettingsPayload(int NextEmployeeNumber);

    private sealed record CommitItemPayload(Guid EmployeeId, string AssignedEmployeeNumber);

    private sealed record CommitPayload(Guid BackfillOperationId, List<CommitItemPayload> Items, int TotalCount);
}
