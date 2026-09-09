using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// Ticket 2 (item 5): the combined "Edit employee" save — one transactional PUT that applies both
// the profile tab and the employment tab against a single Employee.Version guard.
[Collection("Integration")]
public class UpdateEmployeeProfileAndEmploymentEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("cea50000-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("cea50000-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("cea50000-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("cea50000-0000-0000-0000-000000000004");
    private static readonly Guid NoManageUser = new("cea50000-0000-0000-0000-0000000000ff");

    public UpdateEmployeeProfileAndEmploymentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            foreach (var u in new[] { User1, User2, User3, User4 })
            {
                await TestRoleSeeder.AssignRoleAsync(factory, u, SystemRoles.HrAdministrator);
                await TestRoleSeeder.AssignRoleAsync(factory, u, SystemRoles.Employee);
            }
            await TestRoleSeeder.AssignRoleAsync(factory, NoManageUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_Combined_Happy_Path_Saves_Both_Halves_And_Bumps_Version()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User1);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);

        var version = await GetVersionAsync(client, companyId, employee.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/profile-and-employment",
            Body(companyId, employee.Id, firstName: "Combined", employeeNumber: "EMP-COMBINED", notes: "combined-note", expectedVersion: version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CombinedPayload>();
        Assert.Equal("Combined", payload!.FirstName);
        Assert.Equal("EMP-COMBINED", payload.EmployeeNumber);
        Assert.Equal("combined-note", payload.Notes);
        Assert.Equal(version + 1, payload.Version);

        var reloaded = await client.GetFromJsonAsync<EmployeeSnapshot>(
            $"/api/companies/{companyId}/employees/{employee.Id}");
        Assert.Equal("Combined", reloaded!.FirstName);
        Assert.Equal(version + 1, reloaded.Version);
    }

    [Fact]
    public async Task Put_Combined_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/profile-and-employment",
            Body(Guid.NewGuid(), Guid.NewGuid(), "A", "EMP-1", "n", expectedVersion: 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Combined_Returns_403_For_User_Without_Employee_Manage()
    {
        var (adminClient, companyId, employee) = await CreateEmployeeAsync(User2);

        using var noManage = _factory.CreateClient();
        noManage.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, NoManageUser.ToString());
        noManage.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, NoManageUser, SystemRoles.Employee, companyId);

        var response = await noManage.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/profile-and-employment",
            Body(companyId, employee.Id, "Blocked", "EMP-X", "n", expectedVersion: 1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Combined_Returns_404_For_Unknown_Employee()
    {
        var (client, companyId, _) = await CreateEmployeeAsync(User3);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/profile-and-employment",
            Body(companyId, Guid.NewGuid(), "Ghost", "EMP-GHOST", "n", expectedVersion: 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_Combined_Returns_422_When_ExpectedVersion_Missing_And_Writes_Nothing()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User4);
        var versionBefore = await GetVersionAsync(client, companyId, employee.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/profile-and-employment",
            Body(companyId, employee.Id, "NoVersion", "EMP-NV", "n", expectedVersion: null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(versionBefore, await GetVersionAsync(client, companyId, employee.Id));
    }

    [Fact]
    public async Task Put_Combined_Returns_409_When_ExpectedVersion_Stale_And_Writes_Nothing()
    {
        var (client, companyId, employee) = await CreateEmployeeAsync(User1);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);

        var version = await GetVersionAsync(client, companyId, employee.Id);

        var first = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/profile-and-employment",
            Body(companyId, employee.Id, "FirstWrite", "EMP-FIRST", "first-note", expectedVersion: version));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var stale = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/profile-and-employment",
            Body(companyId, employee.Id, "StaleWrite", "EMP-STALE", "stale-note", expectedVersion: version));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await stale.Content.ReadFromJsonAsync<ErrorPayload>();
        Assert.Equal("concurrency", body!.Code);

        var reloaded = await client.GetFromJsonAsync<EmployeeSnapshot>(
            $"/api/companies/{companyId}/employees/{employee.Id}");
        Assert.Equal("FirstWrite", reloaded!.FirstName);              // profile field unchanged
        Assert.Equal("EMP-FIRST", reloaded.EmployeeNumber);           // employment field unchanged
    }

    // ── Regression: clearing optional employment fields must persist as NULL ──────────────────
    // The Employee Edit screen previously resurrected stale values (employment?.X ?? _employee?.X)
    // when a field was deliberately cleared. The screen now passes loaded values through verbatim
    // including nulls; these tests pin that the server handler also writes the nulls straight
    // through (it assigns request.X directly — no `?? employee.X` coalescing on these fields).

    [Fact]
    public async Task Put_Combined_Clears_ContinuousServiceDate_ProbationEndDate_Notes_And_ManagerId()
    {
        var (client, companyId, refData, subject) = await CreateEmployeeWithRefDataAsync(User1);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);
        var manager = await CreateAdditionalEmployeeAsync(client, companyId, refData, "Manager", "One");
        var empNo = $"EMP-CLR1-{Guid.NewGuid():N}"[..14];

        // Populate all four optional fields.
        var v1 = await GetVersionAsync(client, companyId, subject.Id);
        var populate = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{subject.Id}/profile-and-employment",
            FullBody(companyId, subject.Id, empNo, v1,
                continuousServiceDate: "2020-03-01",
                probationEndDate: "2026-10-01",
                notes: "seed notes",
                managerId: manager.Id));
        Assert.Equal(HttpStatusCode.OK, populate.StatusCode);

        var populated = await client.GetFromJsonAsync<EmploymentSnapshot>(
            $"/api/companies/{companyId}/employees/{subject.Id}");
        Assert.Equal(new DateOnly(2020, 3, 1), populated!.ContinuousServiceDate);
        Assert.Equal(new DateOnly(2026, 10, 1), populated.ProbationEndDate);
        Assert.Equal("seed notes", populated.Notes);
        Assert.Equal(manager.Id, populated.ManagerId);

        // Now send the same update with all four explicitly null.
        var clear = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{subject.Id}/profile-and-employment",
            FullBody(companyId, subject.Id, empNo, populated.Version,
                continuousServiceDate: null, probationEndDate: null, notes: null, managerId: null));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);

        var cleared = await client.GetFromJsonAsync<EmploymentSnapshot>(
            $"/api/companies/{companyId}/employees/{subject.Id}");
        Assert.Null(cleared!.ContinuousServiceDate);
        Assert.Null(cleared.ProbationEndDate);
        Assert.Null(cleared.Notes);
        Assert.Null(cleared.ManagerId);
    }

    [Fact]
    public async Task Put_Combined_Clears_WorkingPattern_And_NoticePeriod_Overrides()
    {
        var (client, companyId, _, subject) = await CreateEmployeeWithRefDataAsync(User2);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(client, companyId);
        var empNo = $"EMP-CLR2-{Guid.NewGuid():N}"[..14];

        var v1 = await GetVersionAsync(client, companyId, subject.Id);
        var populate = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{subject.Id}/profile-and-employment",
            FullBody(companyId, subject.Id, empNo, v1,
                workingDaysOverride: "Monday, Tuesday, Wednesday, Thursday",
                hoursPerDayOverride: 6.5m,
                noticePeriodUnitOverride: "Weeks",
                noticePeriodLengthOverride: 8));
        Assert.Equal(HttpStatusCode.OK, populate.StatusCode);

        var populated = await client.GetFromJsonAsync<EmploymentSnapshot>(
            $"/api/companies/{companyId}/employees/{subject.Id}");
        Assert.Equal("Monday, Tuesday, Wednesday, Thursday", populated!.WorkingDaysOverride);
        Assert.Equal(6.5m, populated.HoursPerDayOverride);
        Assert.Equal("Weeks", populated.NoticePeriodUnitOverride);
        Assert.Equal(8, populated.NoticePeriodLengthOverride);

        var clear = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{subject.Id}/profile-and-employment",
            FullBody(companyId, subject.Id, empNo, populated.Version,
                workingDaysOverride: null, hoursPerDayOverride: null,
                noticePeriodUnitOverride: null, noticePeriodLengthOverride: null));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);

        var cleared = await client.GetFromJsonAsync<EmploymentSnapshot>(
            $"/api/companies/{companyId}/employees/{subject.Id}");
        Assert.Null(cleared!.WorkingDaysOverride);
        Assert.Null(cleared.HoursPerDayOverride);
        Assert.Null(cleared.NoticePeriodUnitOverride);
        Assert.Null(cleared.NoticePeriodLengthOverride);
    }

    private static object FullBody(
        Guid companyId, Guid id, string employeeNumber, int? expectedVersion,
        string? continuousServiceDate = null,
        string? probationEndDate = null,
        string? notes = null,
        Guid? managerId = null,
        string? workingDaysOverride = null,
        decimal? hoursPerDayOverride = null,
        string? noticePeriodUnitOverride = null,
        int? noticePeriodLengthOverride = null)
        => new
        {
            companyId,
            id,
            firstName = "Clear",
            lastName = "Test",
            workEmail = $"clear.{id:N}@example.com",
            hasSystemAccess = true,
            employeeNumber,
            employmentTypeId = (Guid?)null,
            status = "Active",
            startDate = "2026-01-15",
            continuousServiceDate,
            probationEndDate,
            leavingDate = (string?)null,
            notes,
            managerId,
            workingDaysOverride,
            hoursPerDayOverride,
            noticePeriodUnitOverride,
            noticePeriodLengthOverride,
            expectedVersion,
        };

    private async Task<(HttpClient Client, Guid CompanyId, EmployeeReferenceDataSeeder.ReferenceData RefData, EmployeeRef Employee)>
        CreateEmployeeWithRefDataAsync(Guid userId)
    {
        var companyId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var employee = await CreateAdditionalEmployeeAsync(client, companyId, refData, "Subject", "Employee");
        return (client, companyId, refData, employee);
    }

    private static async Task<EmployeeRef> CreateAdditionalEmployeeAsync(
        HttpClient client, Guid companyId, EmployeeReferenceDataSeeder.ReferenceData refData,
        string firstName, string lastName)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, firstName, lastName, $"{firstName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
                startDate: new DateOnly(2026, 1, 15), gender: "Male"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeeRef>())!;
    }

    private sealed record EmploymentSnapshot(
        DateOnly? ContinuousServiceDate,
        DateOnly? ProbationEndDate,
        DateOnly? LeavingDate,
        string? Notes,
        Guid? ManagerId,
        string? WorkingDaysOverride,
        decimal? HoursPerDayOverride,
        string? NoticePeriodUnitOverride,
        int? NoticePeriodLengthOverride,
        int Version);

    private static object Body(
        Guid companyId, Guid id, string firstName, string employeeNumber, string? notes, int? expectedVersion)
        => new
        {
            companyId,
            id,
            firstName,
            lastName = "Smith",
            workEmail = $"combined.{Guid.NewGuid():N}@example.com",
            hasSystemAccess = true,
            employeeNumber,
            employmentTypeId = (Guid?)null,
            status = "Active",
            startDate = "2026-01-15",
            notes,
            expectedVersion,
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

    private static async Task<int> GetVersionAsync(HttpClient client, Guid companyId, Guid id)
    {
        var r = await client.GetAsync($"/api/companies/{companyId}/employees/{id}");
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<EmployeeSnapshot>())!.Version;
    }

    private sealed record EmployeeRef(Guid Id);
    private sealed record ErrorPayload(string? Error, string? Code);
    private sealed record EmployeeSnapshot(Guid Id, string FirstName, string? EmployeeNumber, int Version);
    private sealed record CombinedPayload(Guid Id, string FirstName, string LastName, string? EmployeeNumber, string? Notes, int Version);
}
