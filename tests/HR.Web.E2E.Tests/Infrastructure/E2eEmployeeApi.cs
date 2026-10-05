using System.Net.Http.Json;

namespace HR.Web.E2E.Tests.Infrastructure;

public static class E2eEmployeeApi
{
    public static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid LauraUserId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    private static readonly Guid DepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId = Guid.Parse("20000000-0000-0000-0000-00000000000B");
    private static readonly Guid EmploymentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid DefaultLeavePolicyId = Guid.Parse("C0000000-0000-0000-0000-000000000001");

    public sealed record CreatedEmployee(Guid Id, string FirstName, string LastName)
    {
        public string FullName => $"{FirstName} {LastName}";
    }

    private static readonly SemaphoreSlim FillerManagerGate = new(1, 1);
    private static Guid? _fillerManagerId;

    public static async Task<Guid> GetSharedFillerManagerIdAsync(string apiBaseUrl)
    {
        if (_fillerManagerId is { } existing)
            return existing;

        await FillerManagerGate.WaitAsync();
        try
        {
            if (_fillerManagerId is { } created)
                return created;

            var manager = await CreateAcmeEmployeeAsync(apiBaseUrl, "FillerMgr", activate: true);
            _fillerManagerId = manager.Id;
            return manager.Id;
        }
        finally
        {
            FillerManagerGate.Release();
        }
    }

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
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                gender = "Male",
                employeeNumber,
                employmentTypeId = EmploymentTypeId,
                salary = 45000m,
                salaryFrequency = "Annual",
                currency = "GBP",
                hasSystemAccess = true,
            });
        Assert.True(response.IsSuccessStatusCode,
            $"Expected employee creation to succeed, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var created = await response.Content.ReadFromJsonAsync<IdPayload>();
        Assert.NotNull(created);

        if (activate)
        {
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

    public static async Task<string> CreateVacantPositionProfileAsync(string apiBaseUrl, string titlePrefix)
    {
        using var http = await CreateHrAdminClientAsync(apiBaseUrl);

        var title = $"{titlePrefix} {Guid.NewGuid():N}"[..Math.Min(60, titlePrefix.Length + 9)];
        var response = await http.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/position-profiles",
            new
            {
                companyId = AcmeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                title,
                defaultLeavePolicyId = DefaultLeavePolicyId,
            });
        Assert.True(response.IsSuccessStatusCode,
            $"Expected position profile creation to succeed, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return title;
    }

    public static async Task WaitForProbationRecordAsync(string apiBaseUrl, Guid employeeId)
    {
        using var http = await CreateHrAdminClientAsync(apiBaseUrl);

        var deadline = DateTime.UtcNow.AddSeconds(45);
        HttpResponseMessage? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = await http.GetAsync($"/api/companies/{AcmeId}/employees/{employeeId}/probation-record");
            if (last.IsSuccessStatusCode)
                return;

            await Task.Delay(500);
        }

        Assert.Fail(
            $"Expected a probation record to be created for employee {employeeId}, last status was {(int?)last?.StatusCode}.");
    }

    public static async Task StartLeavingProcessAsync(
        string apiBaseUrl, Guid employeeId, DateOnly? leavingDate = null, string reason = "Resignation",
        string? notes = null)
    {
        using var http = await CreateHrAdminClientAsync(apiBaseUrl);

        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
        var leaving = leavingDate ?? today;

        var response = await http.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId = AcmeId,
                employeeId,
                resignationReceivedDate = leaving.AddDays(-7).ToString("yyyy-MM-dd"),
                leavingDate = leaving.ToString("yyyy-MM-dd"),
                lastWorkingDay = leaving.ToString("yyyy-MM-dd"),
                leavingReason = reason,
                confirmBackdatedLeavingDate = leaving < today,
                notes,
            });
        Assert.True(response.IsSuccessStatusCode,
            $"Expected start-leaving-process to succeed, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public static async Task CancelLeavingProcessAsync(
        string apiBaseUrl, Guid employeeId, string cancellationReason = "E2E test cancellation")
    {
        using var http = await CreateHrAdminClientAsync(apiBaseUrl);

        var response = await http.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/employees/{employeeId}/leaving-process/cancel",
            new { companyId = AcmeId, employeeId, cancellationReason });
        Assert.True(response.IsSuccessStatusCode,
            $"Expected cancel-leaving-process to succeed, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
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
