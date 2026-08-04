using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetApplicationsByStatusEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000006-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc000006-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetApplicationsByStatusEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
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

    [Fact]
    public async Task Get_ApplicationsByStatus_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/recruitment/applications?stageId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ApplicationsByStatus_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(PlainEmployeeUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment/applications?stageId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ApplicationsByStatus_Returns_UnprocessableEntity_For_Empty_StageId()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment/applications?stageId={Guid.Empty}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_ApplicationsByStatus_Returns_Empty_List_When_No_Matches()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment/applications?stageId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_ApplicationsByStatus_Returns_Matching_Applications_With_Candidate_And_Vacancy_Details()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        Guid applicationId = Guid.Empty, candidateId = Guid.Empty, vacancyId = Guid.Empty, stageId = Guid.Empty;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
            var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
            var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
            var applicationReceivedStageId = stages.Single(s => s.Name == "Application Received").Id;
            var cvReviewStageId = stages.Single(s => s.Name == "CV Review").Id;
            var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, applicationReceivedStageId, null, Now);

            vacancyId = vacancy.Id;
            candidateId = candidate.Id;
            applicationId = application.Id;
            stageId = applicationReceivedStageId;

            db.RecruitmentStages.AddRange(stages);
            db.Vacancies.Add(vacancy);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);

            // Noise: an application on a different stage that should not be returned for this stageId.
            var otherStageCandidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", $"liam.{Guid.NewGuid():N}@example.com", null, null, Now);
            var otherStageApplication = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, otherStageCandidate.Id, cvReviewStageId, null, Now);
            db.Candidates.Add(otherStageCandidate);
            db.Applications.Add(otherStageApplication);

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment/applications?stageId={stageId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(applicationId, item.ApplicationId);
        Assert.Equal(candidateId, item.CandidateId);
        Assert.Equal("Emma Clarke", item.CandidateName);
        Assert.Equal(vacancyId, item.VacancyId);
        Assert.Equal("Senior Software Engineer", item.VacancyTitle);
    }

    [Fact]
    public async Task Get_ApplicationsByStatus_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await ClientAs(RecruiterUser, companyId);

        Guid otherStageId = Guid.Empty;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
            var candidate = Candidate.Create(Guid.NewGuid(), otherCompanyId, "Nina", "Patel", $"nina.{Guid.NewGuid():N}@example.com", null, null, Now);
            var stages = RecruitmentStageSeeder.BuildDefaultStages(otherCompanyId, Now);
            otherStageId = stages.Single(s => s.Name == "Application Received").Id;
            var application = Application.Create(Guid.NewGuid(), otherCompanyId, vacancy.Id, candidate.Id, otherStageId, null, Now);
            db.RecruitmentStages.AddRange(stages);
            db.Vacancies.Add(vacancy);
            db.Candidates.Add(candidate);
            db.Applications.Add(application);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/recruitment/applications?stageId={otherStageId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record ListPayload(List<ApplicationItem> Items);

    private sealed record ApplicationItem(
        Guid ApplicationId,
        Guid CandidateId,
        string CandidateName,
        string CandidateEmail,
        Guid VacancyId,
        string VacancyTitle,
        DateTimeOffset AppliedAt);
}
