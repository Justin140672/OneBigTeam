using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class PreviewBackfillEmployeeNumbersEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid User1 = new("60000000-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("60000000-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("60000000-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("60000000-0000-0000-0000-000000000004");

    public PreviewBackfillEmployeeNumbersEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            foreach (var userId in new[] { User1, User2, User3, User4 })
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
        return await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Backfill Preview Test Co {Guid.NewGuid():N}");
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

    private static string PreviewUrl(Guid companyId) =>
        $"/api/companies/{companyId}/employees/backfill-employee-numbers/preview";

    /// <summary>
    /// Seeds an employee with a genuinely blank EmployeeNumber directly via EF. Employees created
    /// through the CreateEmployee endpoint always end up with a non-blank EmployeeNumber (either
    /// file-supplied in Manual mode, or generator-assigned in Automatic mode) — a blank value only
    /// occurs for records that pre-date Automatic mode being turned on for the company, which in a
    /// fresh test company can only be reproduced by writing directly to the database.
    /// </summary>
    private async Task<Employee> SeedEmployeeMissingNumberAsync(
        Guid companyId,
        HttpClient client,
        string firstName,
        string lastName,
        DateOnly startDate)
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

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(PreviewUrl(Guid.NewGuid()));

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

        var response = await client.GetAsync(PreviewUrl(companyId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Ok_With_Empty_Candidates_When_No_Employees_Are_Missing_A_Number()
    {
        using var client = await AuthenticatedClient(User2, Guid.NewGuid());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        await SetEmployeeNumberModeAsync(client, companyId, "Automatic", prefix: "EMP-", nextEmployeeNumber: 1, minimumLength: 3);

        var response = await client.GetAsync(PreviewUrl(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PreviewPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Candidates);
    }

    [Fact]
    public async Task Returns_Ok_With_Predicted_Numbers_For_Employees_Missing_A_Number_Ordered_By_StartDate()
    {
        using var client = await AuthenticatedClient(User3, Guid.NewGuid());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        // Employees are seeded directly with a blank EmployeeNumber, simulating records that
        // pre-date Automatic mode being turned on for this company.
        await SeedEmployeeMissingNumberAsync(companyId, client, "Zoe", "Adams", new DateOnly(2024, 3, 1));
        await SeedEmployeeMissingNumberAsync(companyId, client, "Alice", "Smith", new DateOnly(2024, 1, 1));

        await SetEmployeeNumberModeAsync(client, companyId, "Automatic", prefix: "EMP-", nextEmployeeNumber: 100, minimumLength: 4);

        var response = await client.GetAsync(PreviewUrl(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PreviewPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Candidates.Count);
        Assert.Equal(new DateOnly(2024, 1, 1), payload.Candidates[0].StartDate);
        Assert.Equal("EMP-0100", payload.Candidates[0].PredictedEmployeeNumber);
        Assert.Equal(new DateOnly(2024, 3, 1), payload.Candidates[1].StartDate);
        Assert.Equal("EMP-0101", payload.Candidates[1].PredictedEmployeeNumber);
    }

    [Fact]
    public async Task Preview_Does_Not_Advance_The_Real_Counter_Or_Mutate_Any_Employee()
    {
        using var client = await AuthenticatedClient(User4, Guid.NewGuid());
        var companyId = await CreateCompanyAsync(client);
        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        await SeedEmployeeMissingNumberAsync(companyId, client, "Alice", "Smith", new DateOnly(2024, 1, 1));

        await SetEmployeeNumberModeAsync(client, companyId, "Automatic", prefix: "EMP-", nextEmployeeNumber: 1, minimumLength: 3);

        var firstPreview = await client.GetAsync(PreviewUrl(companyId));
        firstPreview.EnsureSuccessStatusCode();
        var firstPayload = await firstPreview.Content.ReadFromJsonAsync<PreviewPayload>();

        var secondPreview = await client.GetAsync(PreviewUrl(companyId));
        secondPreview.EnsureSuccessStatusCode();
        var secondPayload = await secondPreview.Content.ReadFromJsonAsync<PreviewPayload>();

        // Calling preview repeatedly must predict the same number every time — proof the real
        // NextEmployeeNumber counter is never advanced by a read-only preview call.
        Assert.NotNull(firstPayload);
        Assert.NotNull(secondPayload);
        Assert.Single(firstPayload!.Candidates);
        Assert.Single(secondPayload!.Candidates);
        Assert.Equal(firstPayload.Candidates[0].PredictedEmployeeNumber, secondPayload.Candidates[0].PredictedEmployeeNumber);

        var settingsResponse = await client.GetAsync($"/api/companies/{companyId}/hr-settings");
        settingsResponse.EnsureSuccessStatusCode();
        var settings = await settingsResponse.Content.ReadFromJsonAsync<SettingsPayload>();
        Assert.NotNull(settings);
        Assert.Equal(1, settings!.NextEmployeeNumber);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record SettingsPayload(int NextEmployeeNumber);

    private sealed record CandidatePayload(
        Guid EmployeeId, string FirstName, string LastName, DateOnly StartDate, string PredictedEmployeeNumber);

    private sealed record PreviewPayload(List<CandidatePayload> Candidates);
}
