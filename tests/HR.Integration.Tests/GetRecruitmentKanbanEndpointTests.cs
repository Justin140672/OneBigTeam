using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class GetRecruitmentKanbanEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00001c-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc00001c-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetRecruitmentKanbanEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid> SeedVacancyAsync(Guid companyId, Guid positionProfileId, string advertTitle = "Backend Engineer")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, advertTitle, null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    [Fact]
    public async Task Get_Kanban_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/kanban");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Kanban_Returns_NotFound_When_Vacancy_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(RecruiterUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/vacancies/{Guid.NewGuid()}/kanban");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Kanban_Returns_Forbidden_For_Plain_Employee()
    {
        // The Kanban board is Recruiter-only (recruitment:manage) — it's an operational recruiting
        // tool, not general vacancy visibility, unlike recruitment:view elsewhere in this module.
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);

        using var client = AuthenticatedClient(PlainEmployeeUser, companyId);
        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/kanban");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Kanban_Returns_Ok_With_A_Column_Per_Active_Stage_And_Grouped_Applicants_For_Recruiter()
    {
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var vacancyId = await SeedVacancyAsync(companyId, referenceData.PositionProfileId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
            var applicationReceivedStageId = stages.Single(s => s.Name == "Application Received").Id;
            var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
            var application = Application.Create(Guid.NewGuid(), companyId, vacancyId, candidate.Id, applicationReceivedStageId, null, Now);
            db.RecruitmentStages.AddRange(stages);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);
            await db.SaveChangesAsync();
        }

        using var client = AuthenticatedClient(RecruiterUser, companyId);
        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}/kanban");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<KanbanPayload>();
        Assert.NotNull(payload);
        Assert.Equal(vacancyId, payload!.VacancyId);
        Assert.Equal(6, payload.Columns.Count);

        var appliedColumn = payload.Columns.Single(c => c.StageName == "Application Received");
        Assert.Equal(1, appliedColumn.Count);
        Assert.Single(appliedColumn.Applicants);

        var otherColumns = payload.Columns.Where(c => c.StageName != "Application Received");
        Assert.All(otherColumns, c =>
        {
            Assert.Equal(0, c.Count);
            Assert.Empty(c.Applicants);
        });
    }

    [Fact]
    public async Task Get_Kanban_Returns_NotFound_For_Vacancy_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var referenceDataA = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyA);
        var vacancyId = await SeedVacancyAsync(companyA, referenceDataA.PositionProfileId);

        using var clientB = AuthenticatedClient(RecruiterUser, companyB);
        var response = await clientB.GetAsync($"/api/companies/{companyB}/vacancies/{vacancyId}/kanban");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record KanbanPayload(Guid VacancyId, string VacancyTitle, List<KanbanColumnPayload> Columns);
    private sealed record KanbanColumnPayload(Guid StageId, string StageName, bool IsTerminal, int Count, List<KanbanApplicantPayload> Applicants);
    private sealed record KanbanApplicantPayload(Guid ApplicationId, Guid CandidateId, string CandidateFirstName, string CandidateLastName);
}
