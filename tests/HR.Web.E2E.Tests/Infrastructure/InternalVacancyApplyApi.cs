using System.Net.Http.Json;

namespace HR.Web.E2E.Tests.Infrastructure;

internal static class InternalVacancyApplyApi
{
    public static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static readonly Guid LauraUserId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    public static readonly Guid JamesId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private static readonly Guid DepartmentId      = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId        = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId = Guid.Parse("20000000-0000-0000-0000-00000000000B");
    private static readonly Guid EmploymentTypeId  = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid DefaultLeavePolicyId = Guid.Parse("C0000000-0000-0000-0000-000000000001");

    public sealed record FreshEmployee(Guid Id, string FirstName, string LastName, string WorkEmail)
    {
        public string FullName => $"{FirstName} {LastName}";
    }

    public sealed record FreshVacancy(Guid Id, string Title);

    public static Task<HttpClient> CreateHrAdminApiClientAsync(string apiBaseUrl) =>
        CreatePersonaApiClientAsync(apiBaseUrl, LauraUserId);

    private static async Task<HttpClient> CreatePersonaApiClientAsync(string apiBaseUrl, Guid userId)
    {
        var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };

        HttpResponseMessage? sessionResponse = null;
        string? sessionBody = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            sessionResponse = await http.PostAsync($"/api/dev/persona/{userId}", content: null);
            if (sessionResponse.IsSuccessStatusCode) break;

            sessionBody = await sessionResponse.Content.ReadAsStringAsync();
            if (attempt < 3) await Task.Delay(1000 * attempt);
        }

        Assert.True(sessionResponse!.IsSuccessStatusCode,
            $"Expected /api/dev/persona/{userId} to succeed, got {sessionResponse.StatusCode}. Response body: {sessionBody}");
        var session = await sessionResponse.Content.ReadFromJsonAsync<DevPersonaSessionResult>();
        Assert.NotNull(session);

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);
        return http;
    }

    public static async Task<FreshEmployee> CreateActiveEmployeeWithLoginAsync(HttpClient hrAdminApi, string apiBaseUrl)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        const string firstName = "E2E";
        var lastName = $"Applicant{unique}";
        var workEmail = $"e2e.apply.{unique}@acme.example";
        var employeeNumber = $"E2E-APPLY-{unique}";

        var createResponse = await hrAdminApi.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/employees",
            new
            {
                companyId = AcmeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId,
                managerId = JamesId,
                firstName,
                lastName,
                workEmail,
                startDate = "2026-03-01",
                dateOfBirth = "1990-06-15",
                nationality = "British",
                gender = "Male",
                employeeNumber,
                employmentTypeId = EmploymentTypeId,
                hasSystemAccess = true,
            });
        Assert.True(createResponse.IsSuccessStatusCode,
            $"Create employee failed with {createResponse.StatusCode}: {await createResponse.Content.ReadAsStringAsync()}");
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEntity>();
        Assert.NotNull(created);

        var getResponse = await hrAdminApi.GetAsync($"/api/companies/{AcmeId}/employees/{created!.Id}");
        Assert.True(getResponse.IsSuccessStatusCode,
            $"GET employee failed with {getResponse.StatusCode}: {await getResponse.Content.ReadAsStringAsync()}");
        var current = await getResponse.Content.ReadFromJsonAsync<VersionPayload>();
        Assert.NotNull(current);

        var activateResponse = await hrAdminApi.PutAsJsonAsync(
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
                managerId = JamesId,
                startDate = "2026-03-01",
                expectedVersion = current!.Version,
            });
        Assert.True(activateResponse.IsSuccessStatusCode,
            $"Activate employee failed with {activateResponse.StatusCode}: {await activateResponse.Content.ReadAsStringAsync()}");

        await EnsureEmployeeLoginAsync(apiBaseUrl, created.Id, workEmail, firstName, lastName);

        return new FreshEmployee(created.Id, firstName, lastName, workEmail);
    }

    private static async Task EnsureEmployeeLoginAsync(
        string apiBaseUrl, Guid employeeId, string email, string firstName, string lastName)
    {
        using var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };

        HttpResponseMessage? response = null;
        string? body = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            response = await http.PostAsJsonAsync("/api/dev/ensure-employee-login", new
            {
                EmployeeId = employeeId,
                CompanyId  = AcmeId,
                Email      = email,
                FirstName  = firstName,
                LastName   = lastName,
            });

            if (response.IsSuccessStatusCode) return;

            body = await response.Content.ReadAsStringAsync();
            if (attempt < 3) await Task.Delay(1000 * attempt);
        }

        Assert.True(response!.IsSuccessStatusCode,
            $"Expected /api/dev/ensure-employee-login to succeed, got {response.StatusCode}. Response body: {body}");
    }

    public static async Task<FreshVacancy> CreateOpenInternalVacancyAsync(HttpClient hrAdminApi, HttpClient recruiterApi)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];

        var profileResponse = await hrAdminApi.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/position-profiles",
            new
            {
                companyId = AcmeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                title = $"E2E Apply Profile {unique}",
                defaultLeavePolicyId = DefaultLeavePolicyId,
            });
        Assert.True(profileResponse.IsSuccessStatusCode,
            $"Create position profile failed with {profileResponse.StatusCode}: {await profileResponse.Content.ReadAsStringAsync()}");
        var profile = await profileResponse.Content.ReadFromJsonAsync<CreatedEntity>();
        Assert.NotNull(profile);

        var title = $"E2E Apply Vacancy {unique}";
        var vacancyResponse = await recruiterApi.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/vacancies",
            new
            {
                companyId = AcmeId,
                positionProfileId = profile!.Id,
                advertTitle = title,
                advertDescription = $"Internal apply E2E vacancy {unique}.",
                hiringManagerId = JamesId,
                isAdvertisedInternally = true,
            });
        Assert.True(vacancyResponse.IsSuccessStatusCode,
            $"Create vacancy failed with {vacancyResponse.StatusCode}: {await vacancyResponse.Content.ReadAsStringAsync()}");
        var vacancy = await vacancyResponse.Content.ReadFromJsonAsync<CreatedEntity>();
        Assert.NotNull(vacancy);

        var publishResponse = await recruiterApi.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/vacancies/{vacancy!.Id}/publish",
            new { companyId = AcmeId, vacancyId = vacancy.Id });
        Assert.True(publishResponse.IsSuccessStatusCode,
            $"Publish vacancy failed with {publishResponse.StatusCode}: {await publishResponse.Content.ReadAsStringAsync()}");

        return new FreshVacancy(vacancy.Id, title);
    }

    private const string DevPersonaPassword = "Dev-Only-Password-1!";

    public sealed record InternalApplication(Guid ApplicationId, Guid CandidateId);

    public static async Task<HttpClient> CreateEmployeeApiClientAsync(string apiBaseUrl, string workEmail)
    {
        var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };

        HttpResponseMessage? response = null;
        string? body = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            response = await http.PostAsJsonAsync("/api/login", new { email = workEmail, password = DevPersonaPassword });
            if (response.IsSuccessStatusCode) break;

            body = await response.Content.ReadAsStringAsync();
            if (attempt < 3) await Task.Delay(1000 * attempt);
        }

        Assert.True(response!.IsSuccessStatusCode,
            $"Expected /api/login for {workEmail} to succeed, got {response.StatusCode}. Response body: {body}");
        var session = await response.Content.ReadFromJsonAsync<DevPersonaSessionResult>();
        Assert.NotNull(session);

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);
        return http;
    }

    public static async Task<InternalApplication> ApplyAsEmployeeAsync(HttpClient employeeApi, Guid vacancyId, string cvFileName)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(CandidateCvApi.BuildTestPdf());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "CvFile", cvFileName);

        var response = await employeeApi.PostAsync(
            $"/api/companies/{AcmeId}/internal-vacancies/{vacancyId}/applications", content);
        Assert.True(response.IsSuccessStatusCode,
            $"Internal apply failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var created = await response.Content.ReadFromJsonAsync<InternalApplyResult>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created!.ApplicationId);
        return new InternalApplication(created.ApplicationId, created.CandidateId);
    }

    private sealed record InternalApplyResult(Guid ApplicationId, Guid CandidateId);

    private sealed record DevPersonaSessionResult(string AccessToken, string RefreshToken, int ExpiresIn);

    private sealed record CreatedEntity(Guid Id);

    private sealed record VersionPayload(int Version);
}
