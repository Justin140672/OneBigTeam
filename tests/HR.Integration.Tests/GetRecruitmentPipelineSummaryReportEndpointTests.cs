using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetRecruitmentPipelineSummaryReportEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetRecruitmentPipelineSummaryReportEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Recruiter, companyId);
        return client;
    }

    private sealed record SeededPipeline(
        RecruitmentStage ApplicationReceived, RecruitmentStage Interview,
        Guid OpenVacancyId, Guid ClosedVacancyId);

    private async Task<SeededPipeline> SeedPipelineAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var now = DateTimeOffset.UtcNow;

        var applicationReceived = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Application Received", 1, false, RecruitmentStageTerminalOutcome.None, now);
        var interview = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Interview", 2, false, RecruitmentStageTerminalOutcome.None, now);
        db.RecruitmentStages.AddRange(applicationReceived, interview);

        var positionProfileId = Guid.NewGuid();
        var openVacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Open Role", null, Guid.NewGuid(), now);
        openVacancy.Open(now, new DateOnly(2026, 1, 1));
        db.Vacancies.Add(openVacancy);

        var closedVacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Closed Role", null, Guid.NewGuid(), now);
        closedVacancy.Open(now, new DateOnly(2026, 1, 1));
        closedVacancy.Close(now, new DateOnly(2026, 2, 1));
        db.Vacancies.Add(closedVacancy);

        var candidateA = Candidate.Create(Guid.NewGuid(), companyId, "First", "LastA", $"a.{Guid.NewGuid():N}@example.com", null, null, now);
        var candidateB = Candidate.Create(Guid.NewGuid(), companyId, "First", "LastB", $"b.{Guid.NewGuid():N}@example.com", null, null, now);
        var candidateC = Candidate.Create(Guid.NewGuid(), companyId, "First", "LastC", $"c.{Guid.NewGuid():N}@example.com", null, null, now);
        db.Candidates.AddRange(candidateA, candidateB, candidateC);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, openVacancy.Id, candidateA.Id, applicationReceived.Id, null, now),
            Application.Create(Guid.NewGuid(), companyId, openVacancy.Id, candidateB.Id, applicationReceived.Id, null, now),
            Application.Create(Guid.NewGuid(), companyId, openVacancy.Id, candidateC.Id, interview.Id, null, now));

        await db.SaveChangesAsync();

        return new SeededPipeline(applicationReceived, interview, openVacancy.Id, closedVacancy.Id);
    }

    [Fact]
    public async Task Get_RecruitmentPipelineSummary_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/reporting/recruitment-pipeline-summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_RecruitmentPipelineSummary_Returns_Forbidden_For_NonRecruiter()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/recruitment-pipeline-summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_RecruitmentPipelineSummary_Excludes_Closed_Vacancy_By_Default_And_Counts_Stages()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(userId, companyId);
        var seeded = await SeedPipelineAsync(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/recruitment-pipeline-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);

        var row = Assert.Single(payload!.Vacancies);
        Assert.Equal(seeded.OpenVacancyId, row.VacancyId);
        Assert.Equal(3, row.CandidateCount);
        Assert.Equal(2, row.CandidatesByStage[seeded.ApplicationReceived.Id]);
        Assert.Equal(1, row.CandidatesByStage[seeded.Interview.Id]);
    }

    [Fact]
    public async Task Get_RecruitmentPipelineSummary_Includes_Closed_Vacancy_When_IncludeClosed_Is_True()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(userId, companyId);
        var seeded = await SeedPipelineAsync(companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/reporting/recruitment-pipeline-summary?includeClosed=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Vacancies.Count);
        Assert.Contains(payload.Vacancies, v => v.VacancyId == seeded.ClosedVacancyId);
    }

    [Fact]
    public async Task Get_RecruitmentPipelineSummary_Isolates_By_Company()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await ClientFor(userId, companyId);
        await SeedPipelineAsync(otherCompanyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/reporting/recruitment-pipeline-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ReportPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Vacancies);
    }

    private sealed record ReportPayload(List<RowPayload> Vacancies, List<StagePayload> Stages);

    private sealed record StagePayload(Guid StageId, string StageName);

    private sealed record RowPayload(
        Guid VacancyId,
        string VacancyTitle,
        string? PositionProfileTitle,
        string? DepartmentName,
        string Status,
        DateOnly? OpenedAt,
        int CandidateCount,
        Dictionary<Guid, int> CandidatesByStage);
}
