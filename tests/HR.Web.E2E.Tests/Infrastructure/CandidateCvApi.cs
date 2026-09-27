using System.Net.Http.Json;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// API seeding/inspection helpers shared by the internal-recruitment CV E2E tests
/// (CandidateCvReviewTests, CandidateCvDocumentsTests). Same transport pattern as
/// EmployeeNotesTabTests.SeedNotesAsync: a real Supabase session for a known dev persona via
/// POST /api/dev/persona/{userId}, then direct calls to the same HR.Api endpoints the web app uses.
/// Marcus Diallo (Recruiter) holds recruitment:manage / candidate:view.
/// </summary>
internal static class CandidateCvApi
{
    public const string MarcusUserId = "30000000-0000-0000-0000-000000000006";

    public static async Task<HttpClient> CreateRecruiterApiClientAsync(string apiBaseUrl)
    {
        var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };

        // /api/dev/persona/{userId} makes a real, network-dependent Supabase password-grant login —
        // retry transient failures, surface the body on a final failure.
        HttpResponseMessage? sessionResponse = null;
        string? sessionBody = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            sessionResponse = await http.PostAsync($"/api/dev/persona/{MarcusUserId}", content: null);
            if (sessionResponse.IsSuccessStatusCode) break;

            sessionBody = await sessionResponse.Content.ReadAsStringAsync();
            if (attempt < 3) await Task.Delay(1000 * attempt);
        }

        Assert.True(sessionResponse!.IsSuccessStatusCode,
            $"Expected /api/dev/persona/{{userId}} to succeed, got {sessionResponse.StatusCode}. Response body: {sessionBody}");
        var session = await sessionResponse.Content.ReadFromJsonAsync<DevPersonaSessionResult>();
        Assert.NotNull(session);

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);
        return http;
    }

    /// <summary>POST /api/companies/{companyId}/candidates. Returns the new candidate id.</summary>
    public static async Task<Guid> CreateCandidateAsync(
        HttpClient api, Guid companyId, string firstName, string lastName, string email)
    {
        var response = await api.PostAsJsonAsync(
            $"/api/companies/{companyId}/candidates",
            new { FirstName = firstName, LastName = lastName, Email = email });
        Assert.True(response.IsSuccessStatusCode,
            $"Create candidate failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var created = await response.Content.ReadFromJsonAsync<CreatedEntity>();
        Assert.NotNull(created);
        return created!.Id;
    }

    /// <summary>POST /api/companies/{companyId}/vacancies/{vacancyId}/applications (no CV). Returns the application id.</summary>
    public static async Task<Guid> CreateApplicationAsync(HttpClient api, Guid companyId, Guid vacancyId, Guid candidateId)
    {
        var response = await api.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications",
            new { CandidateId = candidateId });
        Assert.True(response.IsSuccessStatusCode,
            $"Create application failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var created = await response.Content.ReadFromJsonAsync<CreatedEntity>();
        Assert.NotNull(created);
        return created!.Id;
    }

    public static async Task<ApplicationCvSnapshot> GetApplicationAsync(
        HttpClient api, Guid companyId, Guid vacancyId, Guid applicationId)
    {
        var response = await api.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}");
        Assert.True(response.IsSuccessStatusCode,
            $"GET application failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var app = await response.Content.ReadFromJsonAsync<ApplicationCvSnapshot>();
        Assert.NotNull(app);
        return app!;
    }

    /// <summary>POST .../candidates/{candidateId}/documents multipart with Kind=Cv — the same form
    /// fields CandidateService.UploadCandidateDocumentAsync sends. Returns the new document id.</summary>
    public static async Task<Guid> UploadCandidateCvAsync(HttpClient api, Guid companyId, Guid candidateId, string fileName)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(fileName), "Title");
        content.Add(new StringContent("Cv"), "Kind");
        var file = new ByteArrayContent(BuildTestPdf());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "File", fileName);

        var response = await api.PostAsync($"/api/companies/{companyId}/candidates/{candidateId}/documents", content);
        Assert.True(response.IsSuccessStatusCode,
            $"Candidate CV upload failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var created = await response.Content.ReadFromJsonAsync<CreatedEntity>();
        Assert.NotNull(created);
        return created!.Id;
    }

    /// <summary>PUT .../applications/{applicationId}/cv — records <paramref name="cvDocumentId"/> as the application's CV.</summary>
    public static async Task SetApplicationCvAsync(
        HttpClient api, Guid companyId, Guid vacancyId, Guid applicationId, Guid cvDocumentId, int expectedVersion)
    {
        var response = await api.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/cv",
            new { CvDocumentId = cvDocumentId, ExpectedVersion = expectedVersion });
        Assert.True(response.IsSuccessStatusCode,
            $"PUT application CV failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>Convenience: reads the application's current Version, then PUTs its CV.</summary>
    public static async Task AttachCvToApplicationAsync(
        HttpClient api, Guid companyId, Guid vacancyId, Guid applicationId, Guid cvDocumentId)
    {
        var app = await GetApplicationAsync(api, companyId, vacancyId, applicationId);
        await SetApplicationCvAsync(api, companyId, vacancyId, applicationId, cvDocumentId, app.Version);
    }

    /// <summary>GET .../vacancies/{vacancyId}/applications — every application on the vacancy.</summary>
    public static async Task<IReadOnlyList<VacancyApplicationSnapshot>> ListApplicationsForVacancyAsync(
        HttpClient api, Guid companyId, Guid vacancyId)
    {
        var response = await api.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/applications");
        Assert.True(response.IsSuccessStatusCode,
            $"List applications failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var list = await response.Content.ReadFromJsonAsync<VacancyApplicationListSnapshot>();
        Assert.NotNull(list);
        return list!.Items;
    }

    /// <summary>POST /api/companies/{companyId}/external-recruiters (active by default). Returns the new recruiter id.</summary>
    public static async Task<Guid> CreateExternalRecruiterAsync(HttpClient api, Guid companyId, string agencyName)
    {
        var response = await api.PostAsJsonAsync(
            $"/api/companies/{companyId}/external-recruiters",
            new { CompanyId = companyId, AgencyName = agencyName });
        Assert.True(response.IsSuccessStatusCode,
            $"Create external recruiter failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var created = await response.Content.ReadFromJsonAsync<CreatedEntity>();
        Assert.NotNull(created);
        return created!.Id;
    }

    /// <summary>POST .../candidates/{candidateId}/deactivate with the (required) reason.</summary>
    public static async Task DeactivateCandidateAsync(HttpClient api, Guid companyId, Guid candidateId, string reason)
    {
        var response = await api.PostAsJsonAsync(
            $"/api/companies/{companyId}/candidates/{candidateId}/deactivate",
            new { CompanyId = companyId, CandidateId = candidateId, Reason = reason });
        Assert.True(response.IsSuccessStatusCode,
            $"Deactivate candidate failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>Minimal valid-looking PDF ("%PDF-" magic + padding) — same as SelfServiceDocumentTests.</summary>
    public static byte[] BuildTestPdf()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 500];
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    private sealed record DevPersonaSessionResult(string AccessToken, string RefreshToken, int ExpiresIn);

    private sealed record CreatedEntity(Guid Id);
}

internal sealed record ApplicationCvSnapshot(
    Guid Id,
    Guid CandidateId,
    int Version,
    Guid? CvDocumentId,
    string? CvFileName,
    Guid? CurrentCandidateCvDocumentId,
    string? CurrentCandidateCvFileName,
    // Internal recruitment Ticket 8 (journey/security tests): stage + internal flag from the same GET.
    string? CurrentStageName = null,
    bool IsInternal = false);

internal sealed record VacancyApplicationListSnapshot(IReadOnlyList<VacancyApplicationSnapshot> Items);

internal sealed record VacancyApplicationSnapshot(
    Guid Id,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    string CandidateEmail,
    bool IsWithdrawn);
