using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Proves the recruitment:manage / recruitment:view / candidate:view FastEndpoints policy
/// declarations actually enforce access end-to-end over real HTTP. Unit tests on handlers cannot
/// exercise policy middleware, so this coverage lives exclusively at this layer.
///
/// Vacancy-only reads (recruitment:view) remain visible to any authenticated employee (internal
/// job board visibility). Candidate/application/interview/document reads (candidate:view) and
/// recruitment writes — creating/updating vacancies and candidates, applications, interviews,
/// offers, hires (recruitment:manage) — are Recruiter-only: recruitment is a distinct function
/// with its own role, and HR Administrator does not automatically inherit it, the same
/// non-overlap principle as company:manage/shared-document management elsewhere in this system.
/// Company Administrator is scoped to company profile/settings and does not hold recruitment
/// permissions either.
/// </summary>
[Collection("Integration")]
public class RecruitmentAuthorizationTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid PlainEmployeeUser = new("aa000002-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUser = new("aa000002-0000-0000-0000-000000000002");
    private static readonly Guid RecruiterUser = new("aa000002-0000-0000-0000-000000000003");
    private static readonly Guid HrAdminUser = new("aa000002-0000-0000-0000-000000000004");
    private static readonly Guid CompanyAdministratorUser = new("aa000002-0000-0000-0000-000000000005");

    public RecruitmentAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdministratorUser, SystemRoles.CompanyAdministrator);
        }).GetAwaiter().GetResult();
    }

    private HttpClient ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<(Guid VacancyId, Guid CandidateId, Guid ApplicationId)> SeedApplicationAsync(HttpClient client, Guid companyId)
    {
        // PositionProfileId is required on Vacancy creation (recruitment:manage) but seeding one
        // requires employee:manage, a permission Recruiter does not hold, so it is seeded directly
        // via EF (EmployeeReferenceDataSeeder) rather than through the HTTP-authenticated client.
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var vacancyResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = referenceData.PositionProfileId,
            title = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });
        vacancyResponse.EnsureSuccessStatusCode();
        var vacancy = await vacancyResponse.Content.ReadFromJsonAsync<VacancyPayload>();

        var candidateResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates", new
        {
            companyId,
            firstName = "Emma",
            lastName = "Clarke",
            email = $"emma.clarke.{Guid.NewGuid():N}@example.com"
        });
        candidateResponse.EnsureSuccessStatusCode();
        var candidate = await candidateResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        var applicationResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancy!.Id}/applications", new
            {
                companyId,
                vacancyId = vacancy.Id,
                candidateId = candidate!.Id
            });
        applicationResponse.EnsureSuccessStatusCode();
        var application = await applicationResponse.Content.ReadFromJsonAsync<ApplicationPayload>();

        return (vacancy.Id, candidate.Id, application!.Id);
    }

    // ── recruitment:view — GetVacancy / ListVacancies remain broadly visible ──────

    [Fact]
    public async Task PlainEmployee_Gets_Ok_Listing_Vacancies()
    {
        var companyId = Guid.NewGuid();
        using var recruiterClient = ClientAs(RecruiterUser, companyId);
        await SeedApplicationAsync(recruiterClient, companyId);

        using var client = ClientAs(PlainEmployeeUser, companyId);
        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Ok_Getting_A_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var recruiterClient = ClientAs(RecruiterUser, companyId);
        var (vacancyId, _, _) = await SeedApplicationAsync(recruiterClient, companyId);

        using var client = ClientAs(PlainEmployeeUser, companyId);
        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Manager_Gets_Ok_Listing_Vacancies()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(ManagerUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Ok_Listing_Vacancies()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(HrAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Gets_Unauthorized_Listing_Vacancies()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/vacancies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── candidate:view — plain Employee/Manager forbidden from candidate reads ───

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Listing_Candidates()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/candidates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Getting_A_Candidate()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/candidates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_Gets_Forbidden_Listing_Candidates()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(ManagerUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/candidates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Company Administrator is scoped to company profile/settings management only and does
    // not hold candidate:view — see the narrowing in
    // HR.Modules.Identity.IdentityModule.AddRolePolicies.
    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Listing_Candidates()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(CompanyAdministratorUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/candidates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Getting_A_Candidate()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(CompanyAdministratorUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/candidates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Getting_An_Application()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/vacancies/{Guid.NewGuid()}/applications/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Listing_Applications_For_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/vacancies/{Guid.NewGuid()}/applications");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Listing_Interviews_For_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/vacancies/{Guid.NewGuid()}/interviews");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Getting_Interviews_Today_Count()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/interviews/today-count");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Listing_Candidate_Documents()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/candidates/{Guid.NewGuid()}/documents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Downloading_Candidate_Document()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/candidates/{Guid.NewGuid()}/documents/{Guid.NewGuid()}/download");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── recruitment:manage — Recruiter-only writes; HR Administrator forbidden ────
    // SeedApplicationAsync (uses recruitment:manage internally to create the vacancy/candidate/
    // application) already proves Recruiter succeeds at these writes everywhere else in this
    // file, so this section only needs to prove HR Administrator is now blocked from them.

    [Fact]
    public async Task HrAdministrator_Gets_Forbidden_Creating_A_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(HrAdminUser, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            title = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Forbidden_Creating_A_Candidate()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(HrAdminUser, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates", new
        {
            companyId,
            firstName = "Priya",
            lastName = "Rao",
            email = $"priya.rao.{Guid.NewGuid():N}@example.com"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── candidate:view — Recruiter-only; HR Administrator no longer included ──────

    [Fact]
    public async Task Recruiter_Gets_Ok_Listing_Candidates()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/candidates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Forbidden_Listing_Candidates()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(HrAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/candidates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Recruiter_Gets_Ok_Getting_A_Candidate()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);
        var candidateResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/candidates", new
        {
            companyId,
            firstName = "Nina",
            lastName = "Patel",
            email = $"nina.patel.{Guid.NewGuid():N}@example.com"
        });
        candidateResponse.EnsureSuccessStatusCode();
        var candidate = await candidateResponse.Content.ReadFromJsonAsync<CandidatePayload>();

        var response = await client.GetAsync($"/api/companies/{companyId}/candidates/{candidate!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Forbidden_Getting_An_Application()
    {
        var companyId = Guid.NewGuid();
        using var recruiterClient = ClientAs(RecruiterUser, companyId);
        var (vacancyId, _, applicationId) = await SeedApplicationAsync(recruiterClient, companyId);

        using var client = ClientAs(HrAdminUser, companyId);
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Forbidden_Listing_Applications_For_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var recruiterClient = ClientAs(RecruiterUser, companyId);
        var (vacancyId, _, _) = await SeedApplicationAsync(recruiterClient, companyId);

        using var client = ClientAs(HrAdminUser, companyId);
        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/applications");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Forbidden_Listing_Interviews_For_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var recruiterClient = ClientAs(RecruiterUser, companyId);
        var (vacancyId, _, _) = await SeedApplicationAsync(recruiterClient, companyId);

        using var client = ClientAs(HrAdminUser, companyId);
        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/interviews");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Forbidden_Getting_Interviews_Today_Count()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(HrAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/interviews/today-count");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Forbidden_Listing_Candidate_Documents()
    {
        var companyId = Guid.NewGuid();
        using var recruiterClient = ClientAs(RecruiterUser, companyId);
        var (_, candidateId, _) = await SeedApplicationAsync(recruiterClient, companyId);

        using var client = ClientAs(HrAdminUser, companyId);
        var response = await client.GetAsync($"/api/companies/{companyId}/candidates/{candidateId}/documents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Anonymous requests are unauthorized, not forbidden ────────────────────────

    [Fact]
    public async Task Anonymous_Gets_Unauthorized_Listing_Candidates()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/candidates");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Gets_Unauthorized_Downloading_Candidate_Document()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/candidates/{Guid.NewGuid()}/documents/{Guid.NewGuid()}/download");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record VacancyPayload(Guid Id);
    private sealed record CandidatePayload(Guid Id);
    private sealed record ApplicationPayload(Guid Id);
}
