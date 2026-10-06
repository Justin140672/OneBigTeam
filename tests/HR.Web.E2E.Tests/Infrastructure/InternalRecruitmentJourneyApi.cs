using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// API arrange/probe helpers for internal recruitment Ticket 8 (InternalRecruitmentJourneyTests and
/// InternalRecruitmentSecurityBoundaryTests). Builds on the Ticket 5/7 helpers
/// (InternalVacancyApplyApi, InternalAppointmentApi, CandidateCvApi) and only adds what those don't
/// offer:
///   • a vacancy whose position profile sits in a DIFFERENT department and location from the fresh
///     applicant's (so an appointment demonstrably changes both), in any lifecycle state
///     (Draft / Open / Closed, advertised internally or not);
///   • a fuller employee snapshot (employee number, start date, continuous service date) to prove an
///     appointment leaves the employment record's identity untouched;
///   • RAW request helpers that return the HttpResponseMessage instead of asserting success, for the
///     security-boundary probes (impersonation, cross-company identifiers, non-open vacancies).
///
/// Every helper creates brand-new, GUID-suffixed data through the real HR.Api. The only fixed ids
/// used are SEEDED reference data: Acme's Sales department and Home location, and Beta Corp's seeded
/// company / Alice (employee) / Backend Engineer vacancy / Sophie (candidate + application) — see
/// EmployeesModule.SeedEmployeesAsync and RecruitmentModule's Beta Corp block. Those Beta Corp ids
/// are only ever used as FOREIGN identifiers in requests made by Acme users, which must be rejected,
/// so nothing here mutates another tenant's data.
/// </summary>
internal static class InternalRecruitmentJourneyApi
{
    private static readonly Guid AcmeId = InternalVacancyApplyApi.AcmeId;

    public static readonly Guid SalesDepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    public const string SalesDepartmentName = "Sales";
    public static readonly Guid HomeLocationId = Guid.Parse("70000000-0000-0000-0000-000000000002");
    public const string HomeLocationName = "Home";
    private static readonly Guid DefaultLeavePolicyId = Guid.Parse("C0000000-0000-0000-0000-000000000001");

    public static readonly Guid BetaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid BetaAliceEmployeeId = Guid.Parse("30000000-0000-0000-0000-000000000011");
    public static readonly Guid BetaBackendVacancyId = Guid.Parse("e0000000-0000-0000-0000-000000000011");
    public static readonly Guid BetaSophieCandidateId = Guid.Parse("e1000000-0000-0000-0000-000000000011");
    public static readonly Guid BetaSophieApplicationId = Guid.Parse("e2000000-0000-0000-0000-000000000011");

    public enum VacancyState
    {
        Draft,
        Open,
        Closed,
    }

    public static async Task<InternalVacancyApplyApi.FreshVacancy> CreateVacancyAsync(
        HttpClient hrAdminApi,
        HttpClient recruiterApi,
        bool advertisedInternally,
        VacancyState state,
        Guid? departmentId = null,
        Guid? locationId = null)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];

        var profileResponse = await hrAdminApi.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/position-profiles",
            new
            {
                companyId = AcmeId,
                departmentId = departmentId ?? SalesDepartmentId,
                locationId = locationId ?? HomeLocationId,
                title = $"E2E Journey Profile {unique}",
                defaultLeavePolicyId = DefaultLeavePolicyId,
            });
        Assert.True(profileResponse.IsSuccessStatusCode,
            $"Create position profile failed with {profileResponse.StatusCode}: {await profileResponse.Content.ReadAsStringAsync()}");
        var profile = await profileResponse.Content.ReadFromJsonAsync<CreatedEntity>();
        Assert.NotNull(profile);

        var title = $"E2E Journey Vacancy {unique}";
        var vacancyResponse = await recruiterApi.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/vacancies",
            new
            {
                companyId = AcmeId,
                positionProfileId = profile!.Id,
                advertTitle = title,
                advertDescription = $"Internal recruitment journey E2E vacancy {unique}.",
                hiringManagerId = InternalVacancyApplyApi.JamesId,
                isAdvertisedInternally = advertisedInternally,
                employmentTypeId = InternalVacancyApplyApi.EmploymentTypeId,
            });
        Assert.True(vacancyResponse.IsSuccessStatusCode,
            $"Create vacancy failed with {vacancyResponse.StatusCode}: {await vacancyResponse.Content.ReadAsStringAsync()}");
        var vacancy = await vacancyResponse.Content.ReadFromJsonAsync<CreatedEntity>();
        Assert.NotNull(vacancy);

        if (state is VacancyState.Open or VacancyState.Closed)
        {
            var publishResponse = await recruiterApi.PostAsJsonAsync(
                $"/api/companies/{AcmeId}/vacancies/{vacancy!.Id}/publish",
                new { companyId = AcmeId, vacancyId = vacancy.Id });
            Assert.True(publishResponse.IsSuccessStatusCode,
                $"Publish vacancy failed with {publishResponse.StatusCode}: {await publishResponse.Content.ReadAsStringAsync()}");
        }

        if (state is VacancyState.Closed)
        {
            var closeResponse = await recruiterApi.PostAsJsonAsync(
                $"/api/companies/{AcmeId}/vacancies/{vacancy!.Id}/close",
                new { companyId = AcmeId, vacancyId = vacancy.Id });
            Assert.True(closeResponse.IsSuccessStatusCode,
                $"Close vacancy failed with {closeResponse.StatusCode}: {await closeResponse.Content.ReadAsStringAsync()}");
        }

        return new InternalVacancyApplyApi.FreshVacancy(vacancy!.Id, title);
    }

    /// <summary>The employment-record fields an internal appointment must change (or must NOT change).</summary>
    public sealed record EmployeeRecord(
        Guid Id,
        Guid? DepartmentId,
        Guid? LocationId,
        Guid? PositionProfileId,
        Guid? ManagerId,
        string FirstName,
        string LastName,
        string WorkEmail,
        DateOnly StartDate,
        string? EmployeeNumber,
        DateOnly? ContinuousServiceDate,
        Guid? EmploymentTypeId);

    public static async Task<EmployeeRecord> GetEmployeeRecordAsync(HttpClient hrAdminApi, Guid employeeId)
    {
        var response = await hrAdminApi.GetAsync($"/api/companies/{AcmeId}/employees/{employeeId}");
        Assert.True(response.IsSuccessStatusCode,
            $"GET employee {employeeId} failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var employee = await response.Content.ReadFromJsonAsync<EmployeeRecord>();
        Assert.NotNull(employee);
        return employee!;
    }

    public sealed record CandidateRecord(Guid Id, string FirstName, string LastName, string Email, Guid? EmployeeId);

    public static async Task<CandidateRecord> GetCandidateAsync(HttpClient recruiterApi, Guid candidateId)
    {
        var response = await recruiterApi.GetAsync($"/api/companies/{AcmeId}/candidates/{candidateId}");
        Assert.True(response.IsSuccessStatusCode,
            $"GET candidate {candidateId} failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var candidate = await response.Content.ReadFromJsonAsync<CandidateRecord>();
        Assert.NotNull(candidate);
        return candidate!;
    }

    public static Task<ApplicationCvSnapshot> GetApplicationAsync(HttpClient recruiterApi, Guid vacancyId, Guid applicationId) =>
        CandidateCvApi.GetApplicationAsync(recruiterApi, AcmeId, vacancyId, applicationId);


    public static async Task<HttpResponseMessage> PostInternalApplicationAsync(
        HttpClient employeeApi,
        Guid companyId,
        Guid vacancyId,
        string cvFileName,
        IReadOnlyDictionary<string, string>? extraFormFields = null)
    {
        using var content = new MultipartFormDataContent();
        foreach (var (name, value) in extraFormFields ?? new Dictionary<string, string>())
            content.Add(new StringContent(value), name);

        var file = new ByteArrayContent(CandidateCvApi.BuildTestPdf());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "CvFile", cvFileName);

        return await employeeApi.PostAsync(
            $"/api/companies/{companyId}/internal-vacancies/{vacancyId}/applications", content);
    }

    public static Task<HttpResponseMessage> PostAppointAsync(
        HttpClient appointerApi, Guid companyId, Guid vacancyId, Guid applicationId, Guid? managerId = null) =>
        appointerApi.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/appoint",
            new
            {
                companyId,
                vacancyId,
                applicationId,
                managerId,
                noManager = false,
            });

    public static async Task<HttpResponseMessage> PostNewCandidateApplicationAsync(
        HttpClient recruiterApi, Guid companyId, Guid vacancyId, string lastName)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent("E2E"), "FirstName" },
            { new StringContent(lastName), "LastName" },
            { new StringContent($"e2e.{lastName.ToLowerInvariant()}@example.com"), "Email" },
        };
        return await recruiterApi.PostAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/new-candidate", content);
    }

    public static Task<HttpResponseMessage> PostExistingCandidateApplicationAsync(
        HttpClient recruiterApi, Guid companyId, Guid vacancyId, Guid candidateId) =>
        recruiterApi.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications",
            new { CandidateId = candidateId });

    public static Task<HttpResponseMessage> PutApplicationCvAsync(
        HttpClient recruiterApi, Guid companyId, Guid vacancyId, Guid applicationId, Guid cvDocumentId, int expectedVersion) =>
        recruiterApi.PutAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/cv",
            new { CvDocumentId = cvDocumentId, ExpectedVersion = expectedVersion });

    public static async Task<string?> ReadRejectionCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                   json.RootElement.TryGetProperty("code", out var code) &&
                   code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected, string because)
    {
        if (response.StatusCode == expected) return;
        var body = await response.Content.ReadAsStringAsync();
        Assert.Fail($"{because}: expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
    }

    private sealed record CreatedEntity(Guid Id);
}
