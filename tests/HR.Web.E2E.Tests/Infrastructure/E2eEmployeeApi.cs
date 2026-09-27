using System.Net.Http.Json;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Fast API *arrange* for tests that need "a fresh Acme employee of my own" but aren't testing
/// employee creation itself — so each test mutates only data it created, never a shared seeded
/// persona (see GroupSerializedTestBases.cs: prefer uniquely-created data over serialization
/// gates). Calls the same POST /api/companies/{companyId}/employees endpoint the New Employee form
/// uses, under Laura Bennett's (HR Administrator) dev-persona session — the same transport
/// ManagerTeamProfileTests / EmployeeNotesTabTests / BulkEmployeeInvitationWorkEmailPolicyTests use.
///
/// Always supplies an explicit employee number, so creation succeeds whatever the company's
/// numbering mode is (CreateEmployeeHandler uses a supplied number in both Manual and Automatic
/// mode; only an omitted number depends on the mode).
/// </summary>
public static class E2eEmployeeApi
{
    public static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid LauraUserId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    // Seeded Acme reference data (EmployeesModule.SeedEmployeesAsync): Engineering / London Office
    // / "QA Engineer" / "Permanent" — the same combination the E2E test pool itself uses.
    private static readonly Guid DepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId = Guid.Parse("20000000-0000-0000-0000-00000000000B");
    private static readonly Guid EmploymentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    public sealed record CreatedEmployee(Guid Id, string FirstName, string LastName)
    {
        public string FullName => $"{FirstName} {LastName}";
    }

    /// <summary>
    /// Creates a fresh Acme employee named "E2E {lastNamePrefix}{unique}". New employees are always
    /// created as Draft (Employee.Create); pass <paramref name="activate"/> to move it to Active via
    /// the Employment tab's own PUT .../employment endpoint — required for anything that only lists
    /// Active employees (My Team widget/roster, directory).
    /// </summary>
    public static async Task<CreatedEmployee> CreateAcmeEmployeeAsync(
        string apiBaseUrl, string lastNamePrefix, Guid? managerId = null, bool activate = false)
    {
        using var http = await CreateHrAdminClientAsync(apiBaseUrl);

        var unique = Guid.NewGuid().ToString("N")[..8];
        const string firstName = "E2E";
        var lastName = $"{lastNamePrefix}{unique}";
        var employeeNumber = $"E2E-API-{unique}".ToUpperInvariant();

        var response = await http.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/employees",
            new
            {
                companyId = AcmeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId,
                managerId,
                firstName,
                lastName,
                workEmail = $"e2e.api.{unique}@acme.example",
                startDate = "2026-03-01",
                dateOfBirth = "1990-06-15",
                nationality = "British",
                gender = "Male",
                employeeNumber,
                employmentTypeId = EmploymentTypeId,
                hasSystemAccess = true,
            });
        Assert.True(response.IsSuccessStatusCode,
            $"Expected employee creation to succeed, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var created = await response.Content.ReadFromJsonAsync<IdPayload>();
        Assert.NotNull(created);

        if (activate)
        {
            // UpdateEmploymentDetailsValidator requires ExpectedVersion (optimistic concurrency) —
            // read the just-created record's real Version first.
            var getResponse = await http.GetAsync($"/api/companies/{AcmeId}/employees/{created!.Id}");
            getResponse.EnsureSuccessStatusCode();
            var current = await getResponse.Content.ReadFromJsonAsync<VersionPayload>();
            Assert.NotNull(current);

            var activateResponse = await http.PutAsJsonAsync(
                $"/api/companies/{AcmeId}/employees/{created.Id}/employment",
                new
                {
                    companyId = AcmeId,
                    id = created.Id,
                    employeeNumber,
                    employmentTypeId = EmploymentTypeId,
                    status = "Active",
                    departmentId = DepartmentId,
                    locationId = LocationId,
                    positionProfileId = PositionProfileId,
                    managerId,
                    startDate = "2026-03-01",
                    expectedVersion = current!.Version,
                });
            Assert.True(activateResponse.IsSuccessStatusCode,
                $"Expected employee activation to succeed, got {(int)activateResponse.StatusCode}: {await activateResponse.Content.ReadAsStringAsync()}");
        }

        return new CreatedEmployee(created!.Id, firstName, lastName);
    }

    private static async Task<HttpClient> CreateHrAdminClientAsync(string apiBaseUrl)
    {
        var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };

        HttpResponseMessage? sessionResponse = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            sessionResponse = await http.PostAsync($"/api/dev/persona/{LauraUserId}", content: null);
            if (sessionResponse.IsSuccessStatusCode) break;
            if (attempt < 3) await Task.Delay(1000 * attempt);
        }
        Assert.True(sessionResponse!.IsSuccessStatusCode,
            $"Expected /api/dev/persona/{{userId}} to succeed, got {sessionResponse.StatusCode}.");
        var session = await sessionResponse.Content.ReadFromJsonAsync<DevPersonaSessionResult>();
        Assert.NotNull(session);
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);
        return http;
    }

    private sealed record DevPersonaSessionResult(string AccessToken, string RefreshToken, int ExpiresIn);
    private sealed record IdPayload(Guid Id);
    private sealed record VersionPayload(int Version);
}
