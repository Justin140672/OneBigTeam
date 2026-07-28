using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class MoveApplicationStageEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc00001d-0000-0000-0000-000000000001");
    private static readonly Guid PlainEmployeeUser = new("cc00001d-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public MoveApplicationStageEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<(Guid VacancyId, Guid ApplicationId, Guid ApplicationReceivedStageId, Guid CvReviewStageId, Guid HiredStageId)>
        SeedApplicationAsync(Guid companyId, Guid positionProfileId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);
        db.RecruitmentStages.AddRange(stages);
        var applicationReceivedStageId = stages.Single(s => s.Name == "Application Received").Id;
        var cvReviewStageId = stages.Single(s => s.Name == "CV Review").Id;
        var hiredStageId = stages.Single(s => s.Name == "Hired").Id;

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, applicationReceivedStageId, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();
        return (vacancy.Id, application.Id, applicationReceivedStageId, cvReviewStageId, hiredStageId);
    }

    [Fact]
    public async Task Post_MoveStage_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}/applications/{Guid.NewGuid()}/move-stage",
            new { newStageId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_MoveStage_Returns_Forbidden_For_RecruitmentView_Only_User()
    {
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var (vacancyId, applicationId, _, cvReviewStageId, _) = await SeedApplicationAsync(companyId, referenceData.PositionProfileId);

        // Plain Employee holds recruitment:view but not recruitment:manage (ticket #68).
        using var client = AuthenticatedClient(PlainEmployeeUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/move-stage",
            new { companyId, vacancyId, applicationId, newStageId = cvReviewStageId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_MoveStage_Returns_Ok_For_Valid_Move_By_RecruitmentManage_User()
    {
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var (vacancyId, applicationId, _, cvReviewStageId, _) = await SeedApplicationAsync(companyId, referenceData.PositionProfileId);

        using var client = AuthenticatedClient(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/move-stage",
            new { companyId, vacancyId, applicationId, newStageId = cvReviewStageId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MoveStagePayload>();
        Assert.NotNull(payload);
        Assert.Equal(cvReviewStageId, payload!.CurrentStageId);
    }

    [Fact]
    public async Task Post_MoveStage_Returns_NotFound_For_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var (vacancyId, _, _, cvReviewStageId, _) = await SeedApplicationAsync(companyId, referenceData.PositionProfileId);

        using var client = AuthenticatedClient(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{Guid.NewGuid()}/move-stage",
            new { companyId, vacancyId, applicationId = Guid.NewGuid(), newStageId = cvReviewStageId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_MoveStage_Returns_NotFound_For_Application_In_Different_Company()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var referenceDataA = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyA);
        var (vacancyId, applicationId, _, cvReviewStageId, _) = await SeedApplicationAsync(companyA, referenceDataA.PositionProfileId);

        using var clientB = AuthenticatedClient(RecruiterUser, companyB);

        var response = await clientB.PostAsJsonAsync(
            $"/api/companies/{companyB}/vacancies/{vacancyId}/applications/{applicationId}/move-stage",
            new { companyId = companyB, vacancyId, applicationId, newStageId = cvReviewStageId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_MoveStage_Returns_BadRequest_When_Application_Already_On_Terminal_Stage()
    {
        var companyId = Guid.NewGuid();
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);
        var (vacancyId, applicationId, _, cvReviewStageId, hiredStageId) = await SeedApplicationAsync(companyId, referenceData.PositionProfileId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var application = await db.Applications.SingleAsync(a => a.Id == applicationId);
            application.MoveToStage(hiredStageId, Now);
            await db.SaveChangesAsync();
        }

        using var client = AuthenticatedClient(RecruiterUser, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/move-stage",
            new { companyId, vacancyId, applicationId, newStageId = cvReviewStageId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var savedApplication = await verifyDb.Applications.SingleAsync(a => a.Id == applicationId);
        Assert.Equal(hiredStageId, savedApplication.CurrentStageId);
    }

    private sealed record MoveStagePayload(Guid Id, Guid VacancyId, Guid CandidateId, Guid CurrentStageId);
}
