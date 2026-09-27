using System.Net.Http.Json;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// API arrange helpers for InternalVacancyApplyTests (the employee "Apply" flow on the Internal
/// Vacancies page). Every helper creates brand-new, GUID-suffixed data through the real HR.Api so
/// each test owns its applicant and its vacancy outright — nothing here touches a shared seeded
/// persona's data. Transport pattern mirrors CandidateCvApi / ManagerTeamProfileTests:
/// a real session for a known dev persona via POST /api/dev/persona/{userId}, then direct calls to
/// the same endpoints the web app uses.
/// </summary>
internal static class InternalVacancyApplyApi
{
    public static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Laura Bennett — HR Administrator (employee:manage): creates employees and position profiles.
    public static readonly Guid LauraUserId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    // James Okafor — used as line manager of the fresh employee and hiring manager of the vacancy.
    public static readonly Guid JamesId = Guid.Parse("30000000-0000-0000-0000-000000000002");

    // Seeded Acme reference data (same ids as ManagerTeamProfileTests).
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

    /// <summary>HttpClient authenticated as Laura Bennett (HR Administrator).</summary>
    public static Task<HttpClient> CreateHrAdminApiClientAsync(string apiBaseUrl) =>
        CreatePersonaApiClientAsync(apiBaseUrl, LauraUserId);

    private static async Task<HttpClient> CreatePersonaApiClientAsync(string apiBaseUrl, Guid userId)
    {
        var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };

        // /api/dev/persona/{userId} makes a real, network-dependent Supabase password-grant login —
        // retry transient failures, surface the body on a final failure.
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

    /// <summary>
    /// Creates a brand-new Acme employee (FirstName "E2E", GUID-suffixed last name and
    /// @acme.example work email — the work-email policy requires the company domain), activates it
    /// (CreateEmployee always creates Draft; only Active employees may apply), and gives it a real
    /// login via POST /api/dev/ensure-employee-login. Same approach as
    /// ManagerTeamProfileTests.CreateEmployeeViaApiAsync + AssetAcknowledgementTaskTests.EnsureEmployeeLoginAsync.
    /// The returned employee can then sign in through the UI with LoginPage.LoginAsync(WorkEmail).
    /// </summary>
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

        // Activate: the PUT .../employment validator requires the loaded Version (optimistic concurrency).
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

    /// <summary>
    /// POST /api/dev/ensure-employee-login (anonymous, dev-only) — see
    /// AssetAcknowledgementTaskTests.EnsureEmployeeLoginAsync for the full rationale. Makes a real
    /// Supabase Admin API call, so retries transient failures.
    /// </summary>
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

    /// <summary>
    /// Creates a fresh position profile (HR admin — employee:manage), a vacancy on it that is
    /// advertised internally (Recruiter — recruitment:manage), then publishes it so it is Open.
    /// The result is exactly what the employee Internal Vacancies list shows: same company +
    /// Status == Open + IsAdvertisedInternally.
    /// </summary>
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

    // Same password every dev Supabase account is provisioned with (EnsureDevSupabaseUserAsync /
    // SeedDevSupabaseUsersAsync) — mirrors LoginPage.DevPersonaPassword, whose canonical definition
    // (SupabaseAuthGateway.DevSupabasePassword) is internal and not referenceable from here.
    private const string DevPersonaPassword = "Dev-Only-Password-1!";

    /// <summary>The ids an internal application creates: the application itself and the candidate record it was filed under.</summary>
    public sealed record InternalApplication(Guid ApplicationId, Guid CandidateId);

    /// <summary>
    /// HttpClient authenticated AS a freshly provisioned employee (see
    /// <see cref="CreateActiveEmployeeWithLoginAsync"/>) — POST /api/login with the dev password,
    /// the same sign-in HR.Web's login form performs, so no browser login is needed just to apply.
    /// /api/login is rate-limited per IP + email; the email is unique per test, so retries here only
    /// cover transient failures.
    /// </summary>
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

    /// <summary>
    /// POST /api/companies/{companyId}/internal-vacancies/{vacancyId}/applications as the signed-in
    /// employee — the same multipart request (single "CvFile" part) InternalVacancyService sends when
    /// the employee clicks Apply. Produces a genuine internal application (Source == Internal).
    /// </summary>
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
