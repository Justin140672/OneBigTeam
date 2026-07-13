using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class GetStaleVacanciesEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000008-0000-0000-0000-000000000001");
    private static readonly Guid HrAdminUser = new("cc000008-0000-0000-0000-000000000002");
    private static readonly Guid PlainEmployeeUser = new("cc000008-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetStaleVacanciesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient ClientAs(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_StaleVacancies_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/vacancies/stale");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_StaleVacancies_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/stale");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_StaleVacancies_Returns_Ok_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(HrAdminUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/stale");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_StaleVacancies_Returns_Empty_List_When_No_Open_Vacancies()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/stale");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_StaleVacancies_Returns_Open_Vacancy_With_No_Recent_Activity()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        Guid vacancyId = Guid.Empty;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var openedAt = Now.AddDays(-30);
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, Guid.NewGuid(), openedAt);
            vacancy.Open(openedAt, DateOnly.FromDateTime(openedAt.Date));
            vacancyId = vacancy.Id;

            db.Vacancies.Add(vacancy);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/stale");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(vacancyId, item.VacancyId);
    }

    [Fact]
    public async Task Get_StaleVacancies_Excludes_Vacancy_With_Recent_Application_Activity()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var openedAt = Now.AddDays(-30);
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Product Designer", null, null, Guid.NewGuid(), openedAt);
            vacancy.Open(openedAt, DateOnly.FromDateTime(openedAt.Date));
            var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
            // Recent activity (2 days ago) — well within the default 14-day window.
            var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now.AddDays(-2));

            db.Vacancies.Add(vacancy);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/stale");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_StaleVacancies_Excludes_NonOpen_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            // Never opened — stays in Draft status.
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Data Analyst", null, null, Guid.NewGuid(), Now.AddDays(-60));
            db.Vacancies.Add(vacancy);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/stale");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_StaleVacancies_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = ClientAs(RecruiterUser, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var openedAt = Now.AddDays(-30);
            var otherVacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, null, "Product Designer", null, null, Guid.NewGuid(), openedAt);
            otherVacancy.Open(openedAt, DateOnly.FromDateTime(openedAt.Date));
            db.Vacancies.Add(otherVacancy);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/stale");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record ListPayload(List<VacancyItem> Items);

    private sealed record VacancyItem(
        Guid VacancyId,
        string Title,
        DateOnly? OpenedAt,
        DateTimeOffset? LastActivityAt,
        int DaysSinceActivity);
}
