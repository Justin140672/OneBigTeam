using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 2 (Role Review): Proves that recruitment administration endpoints
/// (vacancies, external recruiters, recruitment stages, position profile review)
/// now require "recruitment:manage" exclusively for Recruiters, and are forbidden
/// for Employee, Manager, HR Administrator (even though HR Admin historically held
/// recruitment permissions), Company Administrator, and unauthenticated callers.
///
/// This ensures clear role separation: recruitment is a distinct function separate
/// from HR administration, and HR Administrator does not automatically inherit
/// Recruiter capabilities.
///
/// Endpoints tested:
/// 1. GET /api/companies/{companyId}/vacancies (ListVacancies)
/// 2. GET /api/companies/{companyId}/vacancies/{vacancyId} (GetVacancy)
/// 3. GET /api/companies/{companyId}/external-recruiters (ListExternalRecruiters)
/// 4. GET /api/companies/{companyId}/external-recruiters/{externalRecruiterId} (GetExternalRecruiter)
/// 5. GET /api/companies/{companyId}/external-recruiters/{externalRecruiterId}/activity-summary (GetExternalRecruiterActivitySummary)
/// 6. GET /api/companies/{companyId}/recruitment-stages (ListRecruitmentStages)
/// 7. GET /api/companies/{companyId}/vacancies/position-profile-matches/review (GetVacanciesNeedingPositionProfileReview)
///
/// Per-endpoint scenarios:
/// - Recruiter can access (200 OK, returns response body with valid data)
/// - Employee cannot access (403 Forbidden)
/// - Manager cannot access (403 Forbidden)
/// - HR Administrator cannot access (403 Forbidden)
/// - Anonymous/unauthenticated gets 401 Unauthorized
/// </summary>
[Collection("Integration")]
public class RecruitmentAdministrationAuthorizationTests
{
    private readonly ApiWebApplicationFactory _factory;

    // Fixed per-role personas for Ticket 2 tests. Namespaced under "00020000" prefix to avoid collisions.
    private static readonly Guid EmployeeUser = new("00020000-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUser = new("00020000-0000-0000-0000-000000000002");
    private static readonly Guid RecruiterUser = new("00020000-0000-0000-0000-000000000003");
    private static readonly Guid HrAdminUser = new("00020000-0000-0000-0000-000000000004");
    private static readonly Guid CompanyAdminUser = new("00020000-0000-0000-0000-000000000005");

    public RecruitmentAdministrationAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUser, SystemRoles.Employee);

            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Manager);

            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);

            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);

            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.CompanyAdministrator);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private HttpClient AnonymousClient() => _factory.CreateClient();

    private static void AssertForbidden(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Expected 401/403 but got {(int)response.StatusCode} {response.StatusCode}");

    private static void AssertReachedHandler(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized),
            $"Expected the request to pass authorization (not 401/403) but got {(int)response.StatusCode} {response.StatusCode}");


    private async Task<Guid> SeedVacancyAsync(HttpClient recruiterClient, Guid companyId)
    {
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var response = await recruiterClient.PostAsJsonAsync($"/api/companies/{companyId}/vacancies", new
        {
            companyId,
            positionProfileId = referenceData.PositionProfileId,
            advertTitle = "Senior Software Engineer",
            hiringManagerId = Guid.NewGuid()
        });

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to seed vacancy: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");

        var vacancy = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        return vacancy!.Id;
    }

    private async Task<Guid> SeedExternalRecruiterAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var recruiter = ExternalRecruiter.Create(
            Guid.NewGuid(), companyId, "Acme Recruiting", null, null, null, null, null, DateTimeOffset.UtcNow);
        db.ExternalRecruiters.Add(recruiter);
        await db.SaveChangesAsync();
        return recruiter.Id;
    }


    [Fact]
    public async Task ListVacancies_IsAllowed_ForRecruiter()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        AssertReachedHandler(response);
    }

    [Fact]
    public async Task ListVacancies_IsForbidden_ForEmployee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(EmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListVacancies_IsForbidden_ForManager()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(ManagerUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListVacancies_IsForbidden_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(HrAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListVacancies_IsForbidden_ForCompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(CompanyAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListVacancies_IsUnauthorized_ForAnonymous()
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/vacancies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }


    [Fact]
    public async Task GetVacancy_IsAllowed_ForRecruiter()
    {
        var companyId = Guid.NewGuid();
        using var recruiterClient = await ClientAs(RecruiterUser, companyId);
        var vacancyId = await SeedVacancyAsync(recruiterClient, companyId);

        var response = await recruiterClient.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}");

        AssertReachedHandler(response);
        Assert.NotNull(response.Content);
    }

    [Fact]
    public async Task GetVacancy_IsForbidden_ForEmployee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(EmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{Guid.NewGuid()}");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetVacancy_IsForbidden_ForManager()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(ManagerUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{Guid.NewGuid()}");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetVacancy_IsForbidden_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(HrAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{Guid.NewGuid()}");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetVacancy_IsForbidden_ForCompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(CompanyAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{Guid.NewGuid()}");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetVacancy_IsUnauthorized_ForAnonymous()
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }


    [Fact]
    public async Task ListExternalRecruiters_IsAllowed_ForRecruiter()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters");

        AssertReachedHandler(response);
    }

    [Fact]
    public async Task ListExternalRecruiters_IsForbidden_ForEmployee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(EmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListExternalRecruiters_IsForbidden_ForManager()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(ManagerUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListExternalRecruiters_IsForbidden_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(HrAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListExternalRecruiters_IsForbidden_ForCompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(CompanyAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListExternalRecruiters_IsUnauthorized_ForAnonymous()
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/external-recruiters");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }


    [Fact]
    public async Task GetExternalRecruiter_IsAllowed_ForRecruiter()
    {
        var companyId = Guid.NewGuid();
        var externalRecruiterId = await SeedExternalRecruiterAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters/{externalRecruiterId}");

        AssertReachedHandler(response);
        Assert.NotNull(response.Content);
    }

    [Fact]
    public async Task GetExternalRecruiter_IsForbidden_ForEmployee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(EmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters/{Guid.NewGuid()}");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetExternalRecruiter_IsForbidden_ForManager()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(ManagerUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters/{Guid.NewGuid()}");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetExternalRecruiter_IsForbidden_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(HrAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters/{Guid.NewGuid()}");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetExternalRecruiter_IsForbidden_ForCompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(CompanyAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/external-recruiters/{Guid.NewGuid()}");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetExternalRecruiter_IsUnauthorized_ForAnonymous()
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/external-recruiters/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }


    [Fact]
    public async Task GetExternalRecruiterActivitySummary_IsAllowed_ForRecruiter()
    {
        var companyId = Guid.NewGuid();
        var externalRecruiterId = await SeedExternalRecruiterAsync(companyId);
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/external-recruiters/{externalRecruiterId}/activity-summary");

        AssertReachedHandler(response);
        Assert.NotNull(response.Content);
    }

    [Fact]
    public async Task GetExternalRecruiterActivitySummary_IsForbidden_ForEmployee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(EmployeeUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/external-recruiters/{Guid.NewGuid()}/activity-summary");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetExternalRecruiterActivitySummary_IsForbidden_ForManager()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(ManagerUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/external-recruiters/{Guid.NewGuid()}/activity-summary");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetExternalRecruiterActivitySummary_IsForbidden_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(HrAdminUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/external-recruiters/{Guid.NewGuid()}/activity-summary");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetExternalRecruiterActivitySummary_IsForbidden_ForCompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(CompanyAdminUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/external-recruiters/{Guid.NewGuid()}/activity-summary");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetExternalRecruiterActivitySummary_IsUnauthorized_ForAnonymous()
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/external-recruiters/{Guid.NewGuid()}/activity-summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }


    [Fact]
    public async Task ListRecruitmentStages_IsAllowed_ForRecruiter()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment-stages");

        AssertReachedHandler(response);
    }

    [Fact]
    public async Task ListRecruitmentStages_IsForbidden_ForEmployee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(EmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment-stages");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListRecruitmentStages_IsForbidden_ForManager()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(ManagerUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment-stages");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListRecruitmentStages_IsForbidden_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(HrAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment-stages");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListRecruitmentStages_IsForbidden_ForCompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(CompanyAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/recruitment-stages");

        AssertForbidden(response);
    }

    [Fact]
    public async Task ListRecruitmentStages_IsUnauthorized_ForAnonymous()
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/recruitment-stages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }


    [Fact]
    public async Task GetVacanciesNeedingPositionProfileReview_IsAllowed_ForRecruiter()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/position-profile-matches/review");

        AssertReachedHandler(response);
    }

    [Fact]
    public async Task GetVacanciesNeedingPositionProfileReview_IsForbidden_ForEmployee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(EmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/position-profile-matches/review");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetVacanciesNeedingPositionProfileReview_IsForbidden_ForManager()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(ManagerUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/position-profile-matches/review");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetVacanciesNeedingPositionProfileReview_IsForbidden_ForHrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(HrAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/position-profile-matches/review");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetVacanciesNeedingPositionProfileReview_IsForbidden_ForCompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(CompanyAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/position-profile-matches/review");

        AssertForbidden(response);
    }

    [Fact]
    public async Task GetVacanciesNeedingPositionProfileReview_IsUnauthorized_ForAnonymous()
    {
        using var client = AnonymousClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/vacancies/position-profile-matches/review");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }


    private sealed record VacancyPayload(Guid Id, Guid CompanyId);
}
