using System.Net.Http.Json;

namespace HR.Web.E2E.Tests.Infrastructure;

internal sealed class RecruitmentSeedApi : IDisposable
{
    public static readonly Guid AcmeId = InternalVacancyApplyApi.AcmeId;

    private readonly HttpClient _hrAdmin;
    private readonly HttpClient _recruiter;

    private RecruitmentSeedApi(HttpClient hrAdmin, HttpClient recruiter)
    {
        _hrAdmin = hrAdmin;
        _recruiter = recruiter;
    }

    public sealed record SeededCandidate(Guid Id, string FirstName, string LastName, string Email)
    {
        public string FullName => $"{FirstName} {LastName}";
    }

    public static async Task<RecruitmentSeedApi> CreateAsync(string apiBaseUrl)
    {
        var hrAdmin = await InternalVacancyApplyApi.CreateHrAdminApiClientAsync(apiBaseUrl);
        var recruiter = await CandidateCvApi.CreateRecruiterApiClientAsync(apiBaseUrl);
        return new RecruitmentSeedApi(hrAdmin, recruiter);
    }

    public Task<InternalVacancyApplyApi.FreshVacancy> CreateOpenVacancyAsync() =>
        InternalRecruitmentJourneyApi.CreateVacancyAsync(
            _hrAdmin, _recruiter, advertisedInternally: false, InternalRecruitmentJourneyApi.VacancyState.Open);

    public async Task<SeededCandidate> CreateCandidateAsync(string firstName = "E2E")
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"Seed{unique}";
        var email = $"e2e.seed.{unique}@example.com";
        var id = await CandidateCvApi.CreateCandidateAsync(_recruiter, AcmeId, firstName, lastName, email);
        return new SeededCandidate(id, firstName, lastName, email);
    }

    public Task<Guid> CreateApplicationAsync(Guid vacancyId, Guid candidateId) =>
        CandidateCvApi.CreateApplicationAsync(_recruiter, AcmeId, vacancyId, candidateId);

    public Task<Guid> CreateExternalRecruiterAsync(string agencyName) =>
        CandidateCvApi.CreateExternalRecruiterAsync(_recruiter, AcmeId, agencyName);

    public async Task CloseVacancyAsync(Guid vacancyId)
    {
        var response = await _recruiter.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/vacancies/{vacancyId}/close",
            new { companyId = AcmeId, vacancyId });
        Assert.True(response.IsSuccessStatusCode,
            $"Close vacancy failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public void Dispose()
    {
        _hrAdmin.Dispose();
        _recruiter.Dispose();
    }
}
